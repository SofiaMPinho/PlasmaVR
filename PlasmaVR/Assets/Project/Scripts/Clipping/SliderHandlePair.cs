using UnityEngine;

/// <summary>
/// Enforces ordering and minimum separation constraints between two slider handles.
/// Prevents handles from overlapping and ensures proper ordering (A ≤ B or B ≤ A).
/// </summary>
[DisallowMultipleComponent]
public class SliderHandlePair : MonoBehaviour
{
    [Tooltip("First handle (A)")]
    public ConstrainedSliderHandle handleA;

    [Tooltip("Second handle (B)")]
    public ConstrainedSliderHandle handleB;

    [Tooltip("Minimum separation between handles expressed in slider value units (B.Value - A.Value >= minSeparation)")]
    public float minSeparation = 0f;

    [Tooltip("If true, enforces handleA.Value <= handleB.Value. If false, enforces handleB.Value <= handleA.Value.")]
    public bool enforceALessOrEqualB = true;

    // Prevent recursive adjustments when we call SetValue from code
    private bool internalUpdate = false;

    void OnEnable()
    {
        if (handleA != null)
            handleA.onValueChanged.AddListener(OnAChanged);
        if (handleB != null)
            handleB.onValueChanged.AddListener(OnBChanged);
    }

    void OnDisable()
    {
        if (handleA != null)
            handleA.onValueChanged.RemoveListener(OnAChanged);
        if (handleB != null)
            handleB.onValueChanged.RemoveListener(OnBChanged);
    }

    private void OnAChanged(float newValue)
    {
        if (internalUpdate) return;
        if (handleA == null || handleB == null) return;

        internalUpdate = true;

        // Compute allowed max for A (so it does not overlap B)
        if (enforceALessOrEqualB)
        {
            float maxA = handleB.Value - minSeparation;
            float clamped = Mathf.Clamp(newValue, handleA.MinValue, handleA.MaxValue);
            // If maxA is less than MinValue, push B up so pair can still satisfy separation
            if (maxA < handleA.MinValue)
            {
                float desiredB = handleA.MinValue + minSeparation;
                desiredB = Mathf.Clamp(desiredB, handleB.MinValue, handleB.MaxValue);
                if (!Mathf.Approximately(desiredB, handleB.Value)) handleB.SetValue(desiredB);
                // recompute maxA
                maxA = handleB.Value - minSeparation;
            }

            if (clamped > maxA)
            {
                float newA = Mathf.Clamp(maxA, handleA.MinValue, handleA.MaxValue);
                if (!Mathf.Approximately(newA, handleA.Value)) handleA.SetValue(newA);
            }
        }
        else // enforce B <= A (B on left)
        {
            float minA = handleB.Value + minSeparation;
            float clamped = Mathf.Clamp(newValue, handleA.MinValue, handleA.MaxValue);
            if (minA > handleA.MaxValue)
            {
                float desiredB = handleA.MaxValue - minSeparation;
                desiredB = Mathf.Clamp(desiredB, handleB.MinValue, handleB.MaxValue);
                if (!Mathf.Approximately(desiredB, handleB.Value)) handleB.SetValue(desiredB);
                minA = handleB.Value + minSeparation;
            }

            if (clamped < minA)
            {
                float newA = Mathf.Clamp(minA, handleA.MinValue, handleA.MaxValue);
                if (!Mathf.Approximately(newA, handleA.Value)) handleA.SetValue(newA);
            }
        }

        internalUpdate = false;
    }

    private void OnBChanged(float newValue)
    {
        if (internalUpdate) return;
        if (handleA == null || handleB == null) return;

        internalUpdate = true;

        if (enforceALessOrEqualB)
        {
            // ensure B >= A + minSeparation
            float minB = handleA.Value + minSeparation;
            float clamped = Mathf.Clamp(newValue, handleB.MinValue, handleB.MaxValue);

            if (minB > handleB.MaxValue)
            {
                float desiredA = handleB.MaxValue - minSeparation;
                desiredA = Mathf.Clamp(desiredA, handleA.MinValue, handleA.MaxValue);
                if (!Mathf.Approximately(desiredA, handleA.Value)) handleA.SetValue(desiredA);
                minB = handleA.Value + minSeparation;
            }

            if (clamped < minB)
            {
                float newB = Mathf.Clamp(minB, handleB.MinValue, handleB.MaxValue);
                if (!Mathf.Approximately(newB, handleB.Value)) handleB.SetValue(newB);
            }
        }
        else
        {
            // enforce B <= A - minSeparation (i.e. B on left of A)
            float maxB = handleA.Value - minSeparation;
            float clamped = Mathf.Clamp(newValue, handleB.MinValue, handleB.MaxValue);

            if (maxB < handleB.MinValue)
            {
                float desiredA = handleB.MinValue + minSeparation;
                desiredA = Mathf.Clamp(desiredA, handleA.MinValue, handleA.MaxValue);
                if (!Mathf.Approximately(desiredA, handleA.Value)) handleA.SetValue(desiredA);
                maxB = handleA.Value - minSeparation;
            }

            if (clamped > maxB)
            {
                float newB = Mathf.Clamp(maxB, handleB.MinValue, handleB.MaxValue);
                if (!Mathf.Approximately(newB, handleB.Value)) handleB.SetValue(newB);
            }
        }

        internalUpdate = false;
    }
}
