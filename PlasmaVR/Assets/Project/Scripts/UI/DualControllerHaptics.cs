using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

/// <summary>
/// Centralized haptic feedback manager for dual VR controllers.
/// Provides configurable amplitude, duration, and frequency for left, right, or both controller vibrations.
/// Methods can be wired to UI events or called programmatically for feedback on user interactions.
/// </summary>
public class DualControllerHaptics : MonoBehaviour
{
    [Header("Controller Haptics")]
    [SerializeField]
    [Tooltip("HapticImpulsePlayer component for the left controller.")]
    HapticImpulsePlayer leftControllerHaptics;

    [SerializeField]
    [Tooltip("HapticImpulsePlayer component for the right controller.")]
    HapticImpulsePlayer rightControllerHaptics;

    [Header("Haptic Feedback Settings")]
    [SerializeField, Range(0f, 1f)]
    [Tooltip("Amplitude (0-1) for haptic feedback.")]
    float amplitude = 0.5f;

    [SerializeField, Range(0.01f, 1f)]
    [Tooltip("Duration in seconds for haptic feedback.")]
    float duration = 0.1f;

    [SerializeField, Range(0f, 500f)]
    [Tooltip("Frequency in Hz for haptic feedback. 0 = device default.")]
    float frequency = 0f;

    /// <summary>
    /// Plays haptic feedback on the left controller.
    /// </summary>
    public void PlayLeftHaptic()
    {
        if (leftControllerHaptics != null)
            leftControllerHaptics.SendHapticImpulse(amplitude, duration, frequency);
    }

    /// <summary>
    /// Plays haptic feedback on the right controller.
    /// </summary>
    public void PlayRightHaptic()
    {
        if (rightControllerHaptics != null)
            rightControllerHaptics.SendHapticImpulse(amplitude, duration, frequency);
    }

    /// <summary>
    /// Plays haptic feedback on both controllers simultaneously.
    /// </summary>
    public void PlayBothHaptics()
    {
        PlayLeftHaptic();
        PlayRightHaptic();
    }

    /// <summary>
    /// Plays haptic feedback on the left controller with custom parameters.
    /// </summary>
    public void PlayLeftHaptic(float customAmplitude, float customDuration)
    {
        if (leftControllerHaptics != null)
            leftControllerHaptics.SendHapticImpulse(customAmplitude, customDuration, frequency);
    }

    /// <summary>
    /// Plays haptic feedback on the right controller with custom parameters.
    /// </summary>
    public void PlayRightHaptic(float customAmplitude, float customDuration)
    {
        if (rightControllerHaptics != null)
            rightControllerHaptics.SendHapticImpulse(customAmplitude, customDuration, frequency);
    }

    /// <summary>
    /// Plays haptic feedback on both controllers with custom parameters.
    /// </summary>
    public void PlayBothHaptics(float customAmplitude, float customDuration)
    {
        PlayLeftHaptic(customAmplitude, customDuration);
        PlayRightHaptic(customAmplitude, customDuration);
    }
}
