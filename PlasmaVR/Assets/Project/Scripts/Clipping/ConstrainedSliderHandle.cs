using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.Events;

/// <summary>
/// A VR-grabable slider handle that constrains movement to a fixed axis between two endpoints.
/// Maintains alignment with the slider axis and invokes a value-changed event when moved.
/// </summary>
public class ConstrainedSliderHandle : UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable
{
    /// <summary>
    /// Configuration for slider axis and value range constraints.
    /// </summary>
    [Header("Slider Constraints")]
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform endPoint;

    [Header("Slider Values")]
    [SerializeField] private float minValue = 0f;
    [SerializeField] private float maxValue = 1f;
    [SerializeField] private float currentValue = 0f;
    [SerializeField] private bool wholeNumbers = false;

    [Header("Events")]
    public UnityEvent<float> onValueChanged;

    private Vector3 sliderAxis;
    private float sliderLength;
    private bool isGrabbed = false;
    private float previousValue;

    protected override void Awake()
    {
        base.Awake();

        // Force settings
        movementType = MovementType.Instantaneous;
        trackPosition = false;
        trackRotation = false;
        throwOnDetach = false;

        CalculateSliderParameters();
        previousValue = currentValue;
    }

    private void Start()
    {
        // Set initial position based on current value
        SetPositionFromValue(currentValue);
    }

    private void CalculateSliderParameters()
    {
        if (startPoint != null && endPoint != null)
        {
            sliderAxis = (endPoint.position - startPoint.position).normalized;
            sliderLength = Vector3.Distance(startPoint.position, endPoint.position);
        }
    }

    private void Update()
    {
        // Recalculate axis and position every frame to handle parent rotation
        CalculateSliderParameters();
        
        // Update position/rotation based on current value to follow parent
        if (!isGrabbed)
        {
            SetPositionFromValue(currentValue);
        }
    }

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        isGrabbed = true;
        CalculateSliderParameters();
    }

    protected override void OnSelectExited(SelectExitEventArgs args)
    {
        base.OnSelectExited(args);
        isGrabbed = false;
    }

    private void LateUpdate()
    {
        if (isGrabbed)
        {
            UpdateConstrainedPosition();
        }
    }

    private void UpdateConstrainedPosition()
    {
        var interactor = firstInteractorSelecting;
        if (interactor == null) return;

        Vector3 targetPosition = interactor.GetAttachTransform(this).position;

        Vector3 toTarget = targetPosition - startPoint.position;
        float projectedDistance = Vector3.Dot(toTarget, sliderAxis);

        // Clamp to slider bounds
        projectedDistance = Mathf.Clamp(projectedDistance, 0f, sliderLength);

        float normalizedPosition = sliderLength > 0 ? projectedDistance / sliderLength : 0;

        currentValue = Mathf.Lerp(minValue, maxValue, normalizedPosition);

        if (wholeNumbers)
        {
            currentValue = Mathf.Round(currentValue);
            normalizedPosition = Mathf.InverseLerp(minValue, maxValue, currentValue);
            projectedDistance = normalizedPosition * sliderLength;
        }

        // Apply constrained position and rotation aligned with slider axis
        Vector3 constrainedPosition = startPoint.position + sliderAxis * projectedDistance;
        transform.position = constrainedPosition;
        
        // Only align rotation for X and Z axes; Y axis maintains parent rotation
        Vector3 absAxis = new Vector3(Mathf.Abs(sliderAxis.x), Mathf.Abs(sliderAxis.y), Mathf.Abs(sliderAxis.z));
        if (!(absAxis.y > absAxis.x && absAxis.y > absAxis.z))
        {
            AlignHandleToAxis(sliderAxis);
        }

        if (!Mathf.Approximately(currentValue, previousValue))
        {
            onValueChanged?.Invoke(currentValue);
            previousValue = currentValue;
        }
    }

    // Public method to set value programmatically
    public void SetValue(float value)
    {
        currentValue = Mathf.Clamp(value, minValue, maxValue);
        if (wholeNumbers)
        {
            currentValue = Mathf.Round(currentValue);
        }
        SetPositionFromValue(currentValue);
        onValueChanged?.Invoke(currentValue);
    }

    // Public getters for manager scripts
    public float Value => currentValue;
    public float MinValue => minValue;
    public float MaxValue => maxValue;

    // Helper to position handle based on value
    private void SetPositionFromValue(float value)
    {
        if (startPoint == null || endPoint == null) return;

        CalculateSliderParameters();

        float normalizedPosition = Mathf.InverseLerp(minValue, maxValue, value);
        float distance = normalizedPosition * sliderLength;

        Vector3 newPosition = startPoint.position + sliderAxis * distance;
        transform.position = newPosition;
        
        // Orient handle to align with slider axis
        AlignHandleToAxis(sliderAxis);
    }

    // Align the handle's rotation to match the slider axis
    private void AlignHandleToAxis(Vector3 worldAxis)
    {
        if (worldAxis.sqrMagnitude < 0.001f) return;

        // Determine which axis is dominant
        Vector3 absAxis = new Vector3(Mathf.Abs(worldAxis.x), Mathf.Abs(worldAxis.y), Mathf.Abs(worldAxis.z));
        
        if (absAxis.y > absAxis.x && absAxis.y > absAxis.z)
        {
            // Y-axis slider: follow parent rotation exactly, don't apply custom rotation
            if (transform.parent != null)
            {
                transform.rotation = transform.parent.rotation;
            }
            else
            {
                transform.rotation = Quaternion.identity;
            }
        }
        else
        {
            // X and Z sliders: align plane perpendicular to axis
            transform.rotation = Quaternion.FromToRotation(Vector3.forward, worldAxis.normalized);
        }
    }

    // Validate values in inspector
    private void OnValidate()
    {
        if (maxValue < minValue)
        {
            maxValue = minValue;
        }

        currentValue = Mathf.Clamp(currentValue, minValue, maxValue);

        if (wholeNumbers)
        {
            minValue = Mathf.Round(minValue);
            maxValue = Mathf.Round(maxValue);
            currentValue = Mathf.Round(currentValue);
        }

        // Update position in editor when values change
        if (!Application.isPlaying && startPoint != null && endPoint != null)
        {
            SetPositionFromValue(currentValue);
        }
    }
}