using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Provides UI slider control for adjusting directional light and ambient lighting intensity.
/// Automatically finds or uses assigned lights and applies intensity ranges for brightness control.
/// </summary>
[DisallowMultipleComponent]
public class SceneBrightnessSlider : MonoBehaviour
{
    [Header("UI")]
    public Slider slider;

    [Header("Directional Light")]
    [Tooltip("Optional: assign the main directional light to control. If empty, the script will find the first directional light in the scene.")]
    public Light directionalLight;
    [Tooltip("Intensity range applied to the directional light.")]
    public float dirMinIntensity = 0.2f;
    public float dirMaxIntensity = 1.5f;

    [Header("Ambient (optional)")]
    [Tooltip("When enabled, the script will also lerp RenderSettings.ambientIntensity.")]
    public bool controlAmbient = true;
    public float ambientMin = 0.0f;
    public float ambientMax = 1.0f;

    // cached original values for reset
    float originalDirIntensity = 1f;
    float originalAmbientIntensity = 1f;
    bool _started = false;

    private void Awake()
    {
        if (slider == null)
        {
            Debug.LogWarning("[SceneBrightness] No Slider assigned. You can assign one in the inspector.");
        }

        if (directionalLight == null)
        {
            // find first directional light in scene
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l != null && l.type == LightType.Directional)
                {
                    directionalLight = l;
                    break;
                }
            }
        }

        if (directionalLight != null)
        {
            originalDirIntensity = directionalLight.intensity;
        }

        // RenderSettings.ambientIntensity may not be used by all pipelines, but cache it anyway
        originalAmbientIntensity = RenderSettings.ambientIntensity;
    }

    private void OnEnable()
    {
        // Only register after Start has set the slider to the correct position.
        // On first enable the listener is added at the end of Start instead.
        if (_started && slider != null)
            slider.onValueChanged.AddListener(OnSliderChanged);
    }

    private void OnDisable()
    {
        if (slider != null)
            slider.onValueChanged.RemoveListener(OnSliderChanged);
    }

    private void Start()
    {
        // Compute slider position from the values captured in Awake (before the slider UI
        // could fire onValueChanged and corrupt scene lighting during initialization).
        float t = directionalLight != null
            ? Mathf.InverseLerp(dirMinIntensity, dirMaxIntensity, originalDirIntensity)
            : Mathf.InverseLerp(ambientMin, ambientMax, originalAmbientIntensity);
        t = Mathf.Clamp01(t);

        // Restore lighting to the true scene startup values in case the slider already
        // fired before Start ran (common when the slider's parent is initially inactive).
        if (directionalLight != null)
            directionalLight.intensity = originalDirIntensity;
        if (controlAmbient)
            RenderSettings.ambientIntensity = originalAmbientIntensity;

        if (slider != null)
            slider.SetValueWithoutNotify(t);

        // Register the listener now that slider position and lighting are in sync.
        // OnEnable already fired before this point so we add it here manually.
        _started = true;
        if (slider != null)
            slider.onValueChanged.AddListener(OnSliderChanged);
    }

    /// <summary>
    /// Callback bound to the slider. `value` is expected in range [0,1].
    /// </summary>
    public void OnSliderChanged(float value)
    {
        value = Mathf.Clamp01(value);

        if (directionalLight != null)
        {
            directionalLight.intensity = Mathf.Lerp(dirMinIntensity, dirMaxIntensity, value);
        }

        if (controlAmbient)
        {
            RenderSettings.ambientIntensity = Mathf.Lerp(ambientMin, ambientMax, value);
        }
    }

    /// <summary>
    /// Helper to reset values to the originals captured at Awake.
    /// </summary>
    [ContextMenu("Reset Brightness")]
    public void ResetBrightness()
    {
        if (directionalLight != null)
            directionalLight.intensity = originalDirIntensity;
        RenderSettings.ambientIntensity = originalAmbientIntensity;

        if (slider != null)
        {
            float t = directionalLight != null
                ? Mathf.InverseLerp(dirMinIntensity, dirMaxIntensity, originalDirIntensity)
                : Mathf.InverseLerp(ambientMin, ambientMax, originalAmbientIntensity);
            slider.SetValueWithoutNotify(Mathf.Clamp01(t));
        }
    }
}
