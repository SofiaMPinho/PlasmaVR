using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Captures a screenshot and uploads it to the PlasmaVR server.
/// Call TriggerScreenshot() from a UI button or input event.
/// </summary>
public class ScreenshotCapture : MonoBehaviour
{
    private enum ImageFormat
    {
        Jpg,
        Png
    }

    private enum PerspectiveMode
    {
        Center,
        LeftEye,
        RightEye
    }

    [SerializeField] private DatasetServerClient serverClient;
    [SerializeField] private string filePrefix = "PlasmaVR_";
    [SerializeField] private ImageFormat imageFormat = ImageFormat.Jpg;
    [SerializeField, Range(10, 100)] private int jpgQuality = 85;

    [Header("Capture")]
    [SerializeField, Tooltip("Optional camera used as capture source. If null, Camera.main is used.")]
    private Camera sourceCamera;
    [SerializeField, Tooltip("Requested output width in pixels")]
    private int captureWidth = 1280;
    [SerializeField, Tooltip("Requested output height in pixels")]
    private int captureHeight = 720;
    [SerializeField, Tooltip("Eye projection used for screenshot framing")]
    private PerspectiveMode perspectiveMode = PerspectiveMode.Center;
    [SerializeField, Tooltip("Fallback to ScreenCapture API if camera-based capture fails")]
    private bool allowLegacyFallback = true;

    [Header("Input")]
    [SerializeField, Tooltip("Input action to trigger a screenshot")]
    private InputActionReference screenshotAction;

    [Header("On-Screen Indicator")]
    [SerializeField, Tooltip("Optional UI Image shown briefly when a screenshot is taken")]
    private Image screenshotIndicator;
    [SerializeField, Range(0f, 1f)] private float indicatorAnchorX = 0.5f;
    [SerializeField, Range(0f, 1f)] private float indicatorAnchorY = 0.9f;
    [SerializeField] private float indicatorDuration = 0.8f;
    [SerializeField] private bool fadeIndicator = true;
    [SerializeField] private float fadeDuration = 0.25f;
    
    [Header("Sound")]
    [SerializeField, Tooltip("Optional sound to play when a screenshot is taken")]
    private AudioClip screenshotSound;
    [SerializeField, Tooltip("Optional AudioSource to play the screenshot sound; falls back to PlayClipAtPoint if not set")]
    private AudioSource screenshotAudioSource;
    [SerializeField, Range(0f, 1f)] private float screenshotSoundVolume = 1f;

    private bool isBusy = false;
    private Coroutine indicatorCoroutine;

    private void OnEnable()
    {
        if (screenshotAction != null)
        {
            screenshotAction.action.performed += OnScreenshotPerformed;
            screenshotAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        if (screenshotAction != null)
        {
            screenshotAction.action.performed -= OnScreenshotPerformed;
            screenshotAction.action.Disable();
        }
    }

    public void TriggerScreenshot()
    {
        if (isBusy)
        {
            Debug.LogWarning("[Screenshot] Trigger ignored: already busy capturing/uploading.");
            return;
        }
        // Show a brief on-screen indicator for user feedback
        if (screenshotIndicator != null)
        {
            if (indicatorCoroutine != null)
            {
                StopCoroutine(indicatorCoroutine);
            }
            indicatorCoroutine = StartCoroutine(ShowIndicatorCoroutine());
        }

        // Play optional screenshot sound for immediate feedback
        if (screenshotSound != null)
        {
            if (screenshotAudioSource != null)
            {
                screenshotAudioSource.PlayOneShot(screenshotSound, screenshotSoundVolume);
            }
            else
            {
                var camPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                AudioSource.PlayClipAtPoint(screenshotSound, camPos, screenshotSoundVolume);
            }
        }

        Debug.Log("[Screenshot] TriggerScreenshot called. Starting capture coroutine.");
        StartCoroutine(CaptureAndUpload());
    }

    private void OnScreenshotPerformed(InputAction.CallbackContext context)
    {
        TriggerScreenshot();
    }

    private IEnumerator CaptureAndUpload()
    {
        isBusy = true;
        yield return new WaitForEndOfFrame();

        Texture2D tex = CaptureFromCurrentView();

        if (tex == null && allowLegacyFallback)
        {
            Debug.LogWarning("[Screenshot] Camera capture failed. Falling back to ScreenCapture API.");
            tex = ScreenCapture.CaptureScreenshotAsTexture();
        }

        if (tex == null)
        {
            Debug.LogWarning("[Screenshot] Capture failed: Texture2D is null.");
            isBusy = false;
            yield break;
        }
        Debug.Log($"[Screenshot] Capture succeeded. Texture size: {tex.width}x{tex.height}");

        bool useJpg = imageFormat == ImageFormat.Jpg;
        byte[] data = useJpg ? tex.EncodeToJPG(jpgQuality) : tex.EncodeToPNG();
        Destroy(tex);

        string ext = useJpg ? "jpg" : "png";
        string filename = $"{filePrefix}{DateTime.Now:yyyyMMdd_HHmmss}.{ext}";
        string contentType = useJpg ? "image/jpeg" : "image/png";

        if (serverClient == null)
        {
            serverClient = FindFirstObjectByType<DatasetServerClient>();
        }

        if (serverClient == null)
        {
            Debug.LogError("[Screenshot] DatasetServerClient not found. Cannot upload screenshot.");
            isBusy = false;
            yield break;
        }

        var uploadTask = serverClient.UploadScreenshotAsync(data, filename, contentType);

        while (!uploadTask.IsCompleted)
        {
            yield return null;
        }

        if (uploadTask.IsFaulted)
        {
            Debug.LogError($"[Screenshot] Upload threw an exception for {filename}: {uploadTask.Exception}");
        }
        else if (uploadTask.IsCanceled)
        {
            Debug.LogWarning($"[Screenshot] Upload was canceled for {filename}");
        }
        else if (uploadTask.Result)
        {
            Debug.Log($"[Screenshot] Uploaded {filename} successfully.");
        }
        else
        {
            Debug.LogError($"[Screenshot] Upload failed for {filename}");
        }

        isBusy = false;
    }

    private Texture2D CaptureFromCurrentView()
    {
        Camera src = sourceCamera != null ? sourceCamera : Camera.main;
        if (src == null)
        {
            Debug.LogWarning("[Screenshot] No source camera found for capture.");
            return null;
        }

        int width = Mathf.Max(64, captureWidth);
        int height = Mathf.Max(64, captureHeight);

        var captureGO = new GameObject("_ScreenshotCaptureCamera");
        captureGO.hideFlags = HideFlags.HideAndDontSave;

        var captureCam = captureGO.AddComponent<Camera>();
        captureCam.CopyFrom(src);
        captureCam.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
        captureCam.stereoTargetEye = StereoTargetEyeMask.None;
        captureCam.targetDisplay = 0;

        var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        rt.name = "_ScreenshotRT";
        captureCam.targetTexture = rt;

        // In XR, choose an eye projection for a predictable screenshot perspective.
        if (src.stereoEnabled)
        {
            switch (perspectiveMode)
            {
                case PerspectiveMode.LeftEye:
                    captureCam.projectionMatrix = src.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
                    break;
                case PerspectiveMode.RightEye:
                    captureCam.projectionMatrix = src.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);
                    break;
                case PerspectiveMode.Center:
                default:
                    captureCam.ResetProjectionMatrix();
                    break;
            }
        }

        var previousActive = RenderTexture.active;
        Texture2D tex = null;

        try
        {
            captureCam.Render();
            RenderTexture.active = rt;

            tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply(false, false);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Screenshot] Camera capture error: {ex.Message}");
            if (tex != null)
            {
                Destroy(tex);
                tex = null;
            }
        }
        finally
        {
            RenderTexture.active = previousActive;
            captureCam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(captureGO);
        }

        return tex;
    }

    private IEnumerator ShowIndicatorCoroutine()
    {
        if (screenshotIndicator == null)
        {
            Debug.LogWarning("[Screenshot] ShowIndicatorCoroutine called but screenshotIndicator is null.");
            yield break;
        }

        // Position using anchors (viewport coords)
        var rt = screenshotIndicator.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(indicatorAnchorX, indicatorAnchorY);
        rt.anchoredPosition = Vector2.zero;

        screenshotIndicator.raycastTarget = false;
        Color baseColor = screenshotIndicator.color;

        if (!fadeIndicator)
        {
            screenshotIndicator.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1f);
            screenshotIndicator.gameObject.SetActive(true);
            yield return new WaitForSeconds(indicatorDuration);
            screenshotIndicator.gameObject.SetActive(false);
            indicatorCoroutine = null;
            yield break;
        }

        float half = Mathf.Max(0.001f, fadeDuration);

        // Fade in
        screenshotIndicator.gameObject.SetActive(true);
        // start fully transparent to ensure visible fade
        screenshotIndicator.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            float a = Mathf.Clamp01(t / half);
            screenshotIndicator.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
            yield return null;
        }
        screenshotIndicator.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1f);

        // Hold visible
        float hold = Mathf.Max(0f, indicatorDuration - fadeDuration);
        yield return new WaitForSeconds(hold);

        // Fade out
        for (float t = 0f; t < half; t += Time.deltaTime)
        {
            float a = Mathf.Clamp01(1f - (t / half));
            screenshotIndicator.color = new Color(baseColor.r, baseColor.g, baseColor.b, a);
            yield return null;
        }

        screenshotIndicator.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
        screenshotIndicator.gameObject.SetActive(false);
        indicatorCoroutine = null;
    }
}