using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base class for all simulation renderers (particles, isosurfaces, streamlines).
/// Provides abstract methods for initializing and displaying simulation frames.
/// Supports local cached data loading with optional lazy frame loading.
/// </summary>
public abstract class Simulation : MonoBehaviour
{
    protected bool toUpdate = false;
    protected bool useLazyLocalLoading = false;

    public abstract void displayFrame(int frame);

    public abstract bool startSim(int numFrames, Vector3 dims, string simPath);

    protected abstract void loadSimulation();

    /// <summary>
    /// Indicates whether the specified frame is ready to render.
    /// Lazy-loading renderers should override to report per-frame readiness.
    /// </summary>
    public virtual bool IsFrameReady(int frame)
    {
        return true;
    }

    /// <summary>
    /// Enable or disable lazy local loading for cached datasets.
    /// When enabled, frames load on demand instead of preloading all frames.
    /// </summary>
    public void SetLazyLocalLoading(bool enabled)
    {
        useLazyLocalLoading = enabled;
    }
}
