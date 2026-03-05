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

    [SerializeField] private DatasetServerClient serverClient;
    [SerializeField] private string filePrefix = "PlasmaVR_";
    [SerializeField] private ImageFormat imageFormat = ImageFormat.Jpg;
    [SerializeField, Range(10, 100)] private int jpgQuality = 85;

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

        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
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