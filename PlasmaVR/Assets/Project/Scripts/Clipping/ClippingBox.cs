using UnityEngine;
using UnityEngine.VFX;
using System.Collections.Generic;

/// <summary>
/// Manages two independent clipping boxes that control particle/streamline visibility in shaders.
/// Updates shader globals and VFX parameters when clip boxes move or are enabled/disabled.
/// Triggers frame refresh when clipping changes during pause so visuals update immediately.
/// </summary>
public class ClippingBox : MonoBehaviour
{
    /// <summary>Support two clipping boxes referenced from this manager (assign Transforms in Inspector)</summary>
    [Header("Clipping Boxes (assign child box Transforms here)")]
    [Tooltip("First clipping box Transform (can be null).")]
    public Transform box1;
    [Tooltip("Second clipping box Transform (can be null).")]
    public Transform box2;

    [Header("Simulation Refresh")]
    [Tooltip("Simulation controller used to refresh visuals when clipping changes while paused.")]
    [SerializeField] private SimulationController simulationController;

    [Header("Box sizing")]
    [Tooltip("Use Renderer/Collider bounds size (world) instead of Transform.lossyScale when available.")]
    [SerializeField] private bool useBoundsSize = true;

    private static readonly int ClipBoxCenter1ID = Shader.PropertyToID("_ClipBoxCenter1");
    private static readonly int ClipBoxSize1ID = Shader.PropertyToID("_ClipBoxSize1");
    private static readonly int ClipBoxInvRotRow0_1ID = Shader.PropertyToID("_ClipBoxInvRotRow0_1");
    private static readonly int ClipBoxInvRotRow1_1ID = Shader.PropertyToID("_ClipBoxInvRotRow1_1");
    private static readonly int ClipBoxInvRotRow2_1ID = Shader.PropertyToID("_ClipBoxInvRotRow2_1");
    private static readonly int ClipBoxCenter2ID = Shader.PropertyToID("_ClipBoxCenter2");
    private static readonly int ClipBoxSize2ID = Shader.PropertyToID("_ClipBoxSize2");
    private static readonly int ClipBoxInvRotRow0_2ID = Shader.PropertyToID("_ClipBoxInvRotRow0_2");
    private static readonly int ClipBoxInvRotRow1_2ID = Shader.PropertyToID("_ClipBoxInvRotRow1_2");
    private static readonly int ClipBoxInvRotRow2_2ID = Shader.PropertyToID("_ClipBoxInvRotRow2_2");
    private static readonly int ClipBoxesEnabledID = Shader.PropertyToID("_ClipBoxesEnabled");

    // Whether clipping is active. Controlled externally by the ToggleSwitcher.
    private bool clippingEnabled = true;

    private List<VisualEffect> vfxs;
    
    // Cache previous values to avoid redundant updates
    private Vector3 lastCenter1;
    private Vector3 lastSize1;
    private Quaternion lastRot1;
    private Vector3 lastCenter2;
    private Vector3 lastSize2;
    private Quaternion lastRot2;
    private float lastEnabledValue = -1f;
    
    // Cached flags for which VFX properties exist
    private struct VFXPropertyCache
    {
        public bool hasClipBoxCenter1;
        public bool hasClipBoxSize1;
        public bool hasClipBoxInvRotRow0_1;
        public bool hasClipBoxInvRotRow1_1;
        public bool hasClipBoxInvRotRow2_1;
        public bool hasClipBoxCenter2;
        public bool hasClipBoxSize2;
        public bool hasClipBoxInvRotRow0_2;
        public bool hasClipBoxInvRotRow1_2;
        public bool hasClipBoxInvRotRow2_2;
        public bool hasClipBoxesEnabled;
    }
    private Dictionary<VisualEffect, VFXPropertyCache> vfxPropertyCache;

    // Convert quaternion to inverse rotation matrix rows (transpose = inverse for rotation)
    private static void QuaternionToInverseRotationMatrix(Quaternion q, out Vector3 row0, out Vector3 row1, out Vector3 row2)
    {
        // Build rotation matrix from quaternion
        float xx = q.x * q.x;
        float yy = q.y * q.y;
        float zz = q.z * q.z;
        float xy = q.x * q.y;
        float xz = q.x * q.z;
        float yz = q.y * q.z;
        float wx = q.w * q.x;
        float wy = q.w * q.y;
        float wz = q.w * q.z;

        // Matrix rows (transpose gives inverse)
        row0 = new Vector3(
            1 - 2 * (yy + zz),
            2 * (xy + wz),
            2 * (xz - wy)
        );
        row1 = new Vector3(
            2 * (xy - wz),
            1 - 2 * (xx + zz),
            2 * (yz + wx)
        );
        row2 = new Vector3(
            2 * (xz + wy),
            2 * (yz - wx),
            1 - 2 * (xx + yy)
        );
    }

    void Start()
    {
        if (simulationController == null)
        {
            simulationController = Object.FindObjectOfType<SimulationController>();
        }

        // Cache VisualEffect components to avoid allocations in Update
        vfxs = new List<VisualEffect>(Object.FindObjectsByType<VisualEffect>(FindObjectsSortMode.None));
        
        // Build property cache
        vfxPropertyCache = new Dictionary<VisualEffect, VFXPropertyCache>();
        foreach (var vfx in vfxs)
        {
            if (vfx == null) continue;
            vfxPropertyCache[vfx] = new VFXPropertyCache
            {
                hasClipBoxCenter1 = vfx.HasVector3("ClipBoxCenter1"),
                hasClipBoxSize1 = vfx.HasVector3("ClipBoxSize1"),
                hasClipBoxInvRotRow0_1 = vfx.HasVector3("ClipBoxInvRotRow0_1"),
                hasClipBoxInvRotRow1_1 = vfx.HasVector3("ClipBoxInvRotRow1_1"),
                hasClipBoxInvRotRow2_1 = vfx.HasVector3("ClipBoxInvRotRow2_1"),
                hasClipBoxCenter2 = vfx.HasVector3("ClipBoxCenter2"),
                hasClipBoxSize2 = vfx.HasVector3("ClipBoxSize2"),
                hasClipBoxInvRotRow0_2 = vfx.HasVector3("ClipBoxInvRotRow0_2"),
                hasClipBoxInvRotRow1_2 = vfx.HasVector3("ClipBoxInvRotRow1_2"),
                hasClipBoxInvRotRow2_2 = vfx.HasVector3("ClipBoxInvRotRow2_2"),
                hasClipBoxesEnabled = vfx.HasFloat("ClipBoxesEnabled")
            };
        }
        
        // ensure globals are initialized
        Shader.SetGlobalFloat(ClipBoxesEnabledID, clippingEnabled ? 1f : 0f);
    }

    void Update()
    {
        bool anyBoxSet = false;
        bool needsUpdate = false;

        // Check box1 for changes
        if (box1 != null)
        {
            Vector3 center1 = box1.position;
            Vector3 size1 = GetBoxSize(box1);
            Quaternion rot1 = box1.rotation;
            
            // Only update if values changed
            if (center1 != lastCenter1 || size1 != lastSize1 || rot1 != lastRot1)
            {
                QuaternionToInverseRotationMatrix(rot1, out Vector3 invRow0, out Vector3 invRow1, out Vector3 invRow2);
                
                Shader.SetGlobalVector(ClipBoxCenter1ID, center1);
                Shader.SetGlobalVector(ClipBoxSize1ID, size1);
                Shader.SetGlobalVector(ClipBoxInvRotRow0_1ID, invRow0);
                Shader.SetGlobalVector(ClipBoxInvRotRow1_1ID, invRow1);
                Shader.SetGlobalVector(ClipBoxInvRotRow2_1ID, invRow2);
                lastCenter1 = center1;
                lastSize1 = size1;
                lastRot1 = rot1;
                needsUpdate = true;
                
                // Update VFX only when changed
                UpdateVFXBox1(center1, size1, invRow0, invRow1, invRow2);
            }
            anyBoxSet = true;
        }
        else if (lastSize1 != Vector3.zero)
        {
            // Box was removed, clear it
            Shader.SetGlobalVector(ClipBoxSize1ID, Vector3.zero);
            lastSize1 = Vector3.zero;
            needsUpdate = true;
        }

        // Check box2 for changes
        if (box2 != null)
        {
            Vector3 center2 = box2.position;
            Vector3 size2 = GetBoxSize(box2);
            Quaternion rot2 = box2.rotation;
            
            // Only update if values changed
            if (center2 != lastCenter2 || size2 != lastSize2 || rot2 != lastRot2)
            {
                QuaternionToInverseRotationMatrix(rot2, out Vector3 invRow0, out Vector3 invRow1, out Vector3 invRow2);
                
                Shader.SetGlobalVector(ClipBoxCenter2ID, center2);
                Shader.SetGlobalVector(ClipBoxSize2ID, size2);
                Shader.SetGlobalVector(ClipBoxInvRotRow0_2ID, invRow0);
                Shader.SetGlobalVector(ClipBoxInvRotRow1_2ID, invRow1);
                Shader.SetGlobalVector(ClipBoxInvRotRow2_2ID, invRow2);
                lastCenter2 = center2;
                lastSize2 = size2;
                lastRot2 = rot2;
                needsUpdate = true;
                
                // Update VFX only when changed
                UpdateVFXBox2(center2, size2, invRow0, invRow1, invRow2);
            }
            anyBoxSet = true;
        }
        else if (lastSize2 != Vector3.zero)
        {
            // Box was removed, clear it
            Shader.SetGlobalVector(ClipBoxSize2ID, Vector3.zero);
            lastSize2 = Vector3.zero;
            needsUpdate = true;
        }

        // Update the global enabled flag only if it changed
        float enabled = (clippingEnabled && anyBoxSet) ? 1f : 0f;
        if (enabled != lastEnabledValue)
        {
            Shader.SetGlobalFloat(ClipBoxesEnabledID, enabled);
            lastEnabledValue = enabled;
            needsUpdate = true;
            
            // Update VFX enabled state
            UpdateVFXEnabled(enabled);
        }

        // When paused, force the current frame to redraw so particles recover after being clipped away.
        if (needsUpdate && simulationController != null && !simulationController.playing)
        {
            simulationController.RequestRefreshCurrentFrame();
            // Workaround: Force VFX Graph to re-evaluate properties by reinitializing VFXs
            foreach (var vfx in vfxs)
            {
                if (vfx != null)
                {
                    vfx.Reinit();
                }
            }
        }
    }

    private Vector3 GetBoxSize(Transform box)
    {
        if (box == null) return Vector3.zero;

        if (useBoundsSize)
        {
            if (box.TryGetComponent<Renderer>(out var r))
            {
                // Use the renderer's local bounds scaled by the transform to avoid rotation-inflated world AABBs.
                return Vector3.Scale(r.localBounds.size, box.lossyScale);
            }
            if (box.TryGetComponent<Collider>(out var c))
            {
                if (c is BoxCollider bc)
                {
                    // BoxCollider exposes local size directly; scale to world.
                    return Vector3.Scale(bc.size, box.lossyScale);
                }

                // Fallback for other collider types: prefer transform scale to avoid rotation-inflated bounds.
                return box.lossyScale;
            }
        }

        return box.lossyScale;
    }

    // Optimized VFX update methods - only update what's needed
    private void UpdateVFXBox1(Vector3 center, Vector3 size, Vector3 invRow0, Vector3 invRow1, Vector3 invRow2)
    {
        foreach (var vfx in vfxs)
        {
            if (vfx == null) continue;
            if (!vfxPropertyCache.TryGetValue(vfx, out var cache)) continue;
            
            if (cache.hasClipBoxCenter1) vfx.SetVector3("ClipBoxCenter1", center);
            if (cache.hasClipBoxSize1) vfx.SetVector3("ClipBoxSize1", size);
            if (cache.hasClipBoxInvRotRow0_1) vfx.SetVector3("ClipBoxInvRotRow0_1", invRow0);
            if (cache.hasClipBoxInvRotRow1_1) vfx.SetVector3("ClipBoxInvRotRow1_1", invRow1);
            if (cache.hasClipBoxInvRotRow2_1) vfx.SetVector3("ClipBoxInvRotRow2_1", invRow2);
        }
    }
    
    private void UpdateVFXBox2(Vector3 center, Vector3 size, Vector3 invRow0, Vector3 invRow1, Vector3 invRow2)
    {
        foreach (var vfx in vfxs)
        {
            if (vfx == null) continue;
            if (!vfxPropertyCache.TryGetValue(vfx, out var cache)) continue;
            
            if (cache.hasClipBoxCenter2) vfx.SetVector3("ClipBoxCenter2", center);
            if (cache.hasClipBoxSize2) vfx.SetVector3("ClipBoxSize2", size);
            if (cache.hasClipBoxInvRotRow0_2) vfx.SetVector3("ClipBoxInvRotRow0_2", invRow0);
            if (cache.hasClipBoxInvRotRow1_2) vfx.SetVector3("ClipBoxInvRotRow1_2", invRow1);
            if (cache.hasClipBoxInvRotRow2_2) vfx.SetVector3("ClipBoxInvRotRow2_2", invRow2);
        }
    }
    
    private void UpdateVFXEnabled(float enabled)
    {
        foreach (var vfx in vfxs)
        {
            if (vfx == null) continue;
            if (!vfxPropertyCache.TryGetValue(vfx, out var cache)) continue;
            
            if (cache.hasClipBoxesEnabled) vfx.SetFloat("ClipBoxesEnabled", enabled);
        }
    }

    /// <summary>
    /// Enable or disable clipping globally. When disabled, shaders will ignore clip boxes.
    /// </summary>
    public void SetClippingEnabled(bool enabled)
    {
        if (clippingEnabled == enabled) return; // Early exit if no change
        
        clippingEnabled = enabled;
        float enabledValue = clippingEnabled ? 1f : 0f;
        
        // Immediately update global so there's no frame delay.
        Shader.SetGlobalFloat(ClipBoxesEnabledID, enabledValue);
        lastEnabledValue = enabledValue;
        
        // Clear sizes when disabled so previous values don't accidentally persist in other codepaths.
        if (!clippingEnabled)
        {
            Shader.SetGlobalVector(ClipBoxSize1ID, Vector3.zero);
            Shader.SetGlobalVector(ClipBoxSize2ID, Vector3.zero);
            lastSize1 = Vector3.zero;
            lastSize2 = Vector3.zero;
        }
        
        UpdateVFXEnabled(enabledValue);
    }
    
    /// <summary>
    /// Register a new VFX to receive clipping updates. Call this when dynamically creating VFX.
    /// </summary>
    public void RegisterVFX(VisualEffect vfx)
    {
        if (vfx == null || vfxs.Contains(vfx)) return;
        
        vfxs.Add(vfx);
        vfxPropertyCache[vfx] = new VFXPropertyCache
        {
            hasClipBoxCenter1 = vfx.HasVector3("ClipBoxCenter1"),
            hasClipBoxSize1 = vfx.HasVector3("ClipBoxSize1"),
            hasClipBoxInvRotRow0_1 = vfx.HasVector3("ClipBoxInvRotRow0_1"),
            hasClipBoxInvRotRow1_1 = vfx.HasVector3("ClipBoxInvRotRow1_1"),
            hasClipBoxInvRotRow2_1 = vfx.HasVector3("ClipBoxInvRotRow2_1"),
            hasClipBoxCenter2 = vfx.HasVector3("ClipBoxCenter2"),
            hasClipBoxSize2 = vfx.HasVector3("ClipBoxSize2"),
            hasClipBoxInvRotRow0_2 = vfx.HasVector3("ClipBoxInvRotRow0_2"),
            hasClipBoxInvRotRow1_2 = vfx.HasVector3("ClipBoxInvRotRow1_2"),
            hasClipBoxInvRotRow2_2 = vfx.HasVector3("ClipBoxInvRotRow2_2"),
            hasClipBoxesEnabled = vfx.HasFloat("ClipBoxesEnabled")
        };
        
        // Initialize with current values
        if (box1 != null)
        {
            QuaternionToInverseRotationMatrix(lastRot1, out Vector3 invRow0_1, out Vector3 invRow1_1, out Vector3 invRow2_1);
            if (vfxPropertyCache[vfx].hasClipBoxCenter1) vfx.SetVector3("ClipBoxCenter1", lastCenter1);
            if (vfxPropertyCache[vfx].hasClipBoxSize1) vfx.SetVector3("ClipBoxSize1", lastSize1);
            if (vfxPropertyCache[vfx].hasClipBoxInvRotRow0_1) vfx.SetVector3("ClipBoxInvRotRow0_1", invRow0_1);
            if (vfxPropertyCache[vfx].hasClipBoxInvRotRow1_1) vfx.SetVector3("ClipBoxInvRotRow1_1", invRow1_1);
            if (vfxPropertyCache[vfx].hasClipBoxInvRotRow2_1) vfx.SetVector3("ClipBoxInvRotRow2_1", invRow2_1);
        }
        if (box2 != null)
        {
            QuaternionToInverseRotationMatrix(lastRot2, out Vector3 invRow0_2, out Vector3 invRow1_2, out Vector3 invRow2_2);
            if (vfxPropertyCache[vfx].hasClipBoxCenter2) vfx.SetVector3("ClipBoxCenter2", lastCenter2);
            if (vfxPropertyCache[vfx].hasClipBoxSize2) vfx.SetVector3("ClipBoxSize2", lastSize2);
            if (vfxPropertyCache[vfx].hasClipBoxInvRotRow0_2) vfx.SetVector3("ClipBoxInvRotRow0_2", invRow0_2);
            if (vfxPropertyCache[vfx].hasClipBoxInvRotRow1_2) vfx.SetVector3("ClipBoxInvRotRow1_2", invRow1_2);
            if (vfxPropertyCache[vfx].hasClipBoxInvRotRow2_2) vfx.SetVector3("ClipBoxInvRotRow2_2", invRow2_2);
        }
        if (vfxPropertyCache[vfx].hasClipBoxesEnabled) vfx.SetFloat("ClipBoxesEnabled", lastEnabledValue);
    }
    
    /// <summary>
    /// Unregister a VFX from receiving updates. Call this before destroying VFX.
    /// </summary>
    public void UnregisterVFX(VisualEffect vfx)
    {
        if (vfx == null) return;
        vfxs.Remove(vfx);
        vfxPropertyCache.Remove(vfx);
    }
}