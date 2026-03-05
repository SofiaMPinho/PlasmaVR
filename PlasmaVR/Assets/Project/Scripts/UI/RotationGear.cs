using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.Events;

/// <summary>
/// A VR-grabable rotation gear with fixed-axis rotation and optional snap-to-corners.
/// Locked position prevents movement; only rotation around the configured axis is allowed.
/// Can optionally drive a separate affected object or snap grab attachment to corner points.
/// </summary>
[DisallowMultipleComponent]
public class RotationGear : UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable
{
    [Header("Snap Points")]
    [Tooltip("Optional corner snap points for grab attachment (e.g., gear corners).")]
    [SerializeField] private List<Transform> cornerSnapPoints = new List<Transform>();

    [Header("Rotation Axis")]
    [Tooltip("The axis around which the gear rotates (e.g., Y for vertical spin).")]
    [SerializeField] private Vector3 rotationAxis = Vector3.up;

    [Header("Rotation Snap")]
    [Tooltip("When true, quantizes rotation to discrete steps.")]
    [SerializeField] private bool enableRotationSnap = false;
    [Tooltip("Snap step in degrees (e.g., 15 means snap every 15°).")]
    [SerializeField] private float snapStepDegrees = 15f;
    [Tooltip("Sensitivity multiplier for rotation (increase for faster response).")]
    [SerializeField] private float rotationSensitivity = 1f;

    [Header("Output")]
    [Tooltip("Optional target that will be rotated by this gear (around the same axis).")]
    [SerializeField] private Transform affectedObject;
    [Tooltip("Multiplier applied when rotating the affected object.")]
    [SerializeField] private float affectedObjectMultiplier = 1f;
    [Tooltip("Event invoked with the angle delta applied each frame (degrees).")]
    public UnityEvent<float> onAngleDelta;

    private Vector3 initialLocalPosition;
    private Vector3 lockedWorldPosition;
    private Quaternion initialRotation;
    private Transform originalAttachTransform;
    private bool isGrabbed = false;
    private Vector3 lastInteractorPosition;

    protected override void Awake()
    {
        base.Awake();

        // Force settings to prevent movement tracking
        movementType = MovementType.Instantaneous;
        trackPosition = false;
        trackRotation = false;
        throwOnDetach = false;

        // Store starting local position (allows following parent), rotation, and attach transform
        initialLocalPosition = transform.localPosition;
        initialRotation = transform.rotation;
        originalAttachTransform = attachTransform;
    }

    protected override void OnSelectEntering(SelectEnterEventArgs args)
    {
        base.OnSelectEntering(args);
        isGrabbed = true;
        lockedWorldPosition = transform.position; // Lock world position when grabbed
        lastInteractorPosition = args.interactorObject.GetAttachTransform(this).position;
        
        // Snap to nearest corner point if available
        SetNearestAttachPoint(args);
    }

    protected override void OnSelectExited(SelectExitEventArgs args)
    {
        base.OnSelectExited(args);
        isGrabbed = false;
        
        // Restore original attach transform
        attachTransform = originalAttachTransform;
    }

    private void SetNearestAttachPoint(SelectEnterEventArgs args)
    {
        if (cornerSnapPoints == null || cornerSnapPoints.Count == 0)
            return;

        var interactorAttach = args.interactorObject.GetAttachTransform(this);
        if (interactorAttach == null)
            return;

        Transform closest = null;
        float bestDistSqr = float.MaxValue;
        foreach (var t in cornerSnapPoints)
        {
            if (t == null) continue;
            float d = (t.position - interactorAttach.position).sqrMagnitude;
            if (d < bestDistSqr)
            {
                bestDistSqr = d;
                closest = t;
            }
        }

        if (closest != null)
            attachTransform = closest;
    }

    private void LateUpdate()
    {
        if (isGrabbed)
        {
            // Lock world position while grabbed (stays in place)
            transform.position = lockedWorldPosition;
            UpdateRotation();
        }
        else
        {
            // Keep local position while not grabbed (follows parent rotation)
            transform.localPosition = initialLocalPosition;
        }
    }

    private void UpdateRotation()
    {
        var interactor = firstInteractorSelecting;
        if (interactor == null) return;

        Vector3 interactorPosition = interactor.GetAttachTransform(this).position;
        Vector3 handVelocity = (interactorPosition - lastInteractorPosition) / Time.deltaTime;
        
        // Vector from gear center to grab point
        Vector3 grabOffset = interactorPosition - transform.position;
        
        // Torque = grabOffset × velocity (cross product gives rotation axis and magnitude)
        Vector3 torque = Vector3.Cross(grabOffset, handVelocity);
        
        // Component of torque along the rotation axis gives us the angular velocity
        Vector3 axis = rotationAxis.normalized;
        float angularVelocity = Vector3.Dot(torque, axis);
        
        // Apply sensitivity scaling
        float rotationAmount = angularVelocity * rotationSensitivity;
        
        // Apply rotation snap if enabled
        if (enableRotationSnap)
        {
            rotationAmount = Mathf.Round(rotationAmount / snapStepDegrees) * snapStepDegrees;
        }

        // Rotate around the specified axis
        transform.Rotate(axis, rotationAmount, Space.World);

        // Propagate rotation to affected object
        if (affectedObject != null)
        {
            affectedObject.Rotate(axis, rotationAmount * affectedObjectMultiplier, Space.World);
        }

        // Invoke event for listeners
        onAngleDelta?.Invoke(rotationAmount);

        lastInteractorPosition = interactorPosition;
    }

    // Helper to get a perpendicular vector to the rotation axis
    private Vector3 GetPerpendicular1()
    {
        Vector3 normalized = rotationAxis.normalized;
        if (Mathf.Abs(normalized.y) < 0.9f)
        {
            return Vector3.Cross(normalized, Vector3.up).normalized;
        }
        else
        {
            return Vector3.Cross(normalized, Vector3.right).normalized;
        }
    }
}

