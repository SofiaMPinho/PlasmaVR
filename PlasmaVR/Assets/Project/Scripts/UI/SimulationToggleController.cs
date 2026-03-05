using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages UI toggles for controlling individual simulation renderers (particles, isosurfaces, streamlines).
/// Automatically connects toggle events to the SimulationController and syncs visual state with simulation state.
/// </summary>
public class SimulationToggleController : MonoBehaviour
{
    [Header("Simulation Controller Reference")]
    [SerializeField, Tooltip("Reference to the main simulation controller")]
    private SimulationController simulationController;

    [Header("Toggle References")]
    [SerializeField, Tooltip("Toggle for particle simulation")]
    private Toggle particleToggle;
    
    [SerializeField, Tooltip("Toggle for isosurface simulation")]
    private Toggle isosurfaceToggle;
    
    [SerializeField, Tooltip("Toggle for streamline simulation")]
    private Toggle streamlineToggle;

    [Header("Default States")]
    [SerializeField, Tooltip("Should particles be enabled by default?")]
    private bool particlesEnabledByDefault = true;
    
    [SerializeField, Tooltip("Should isosurfaces be enabled by default?")]
    private bool isosurfacesEnabledByDefault = true;
    
    [SerializeField, Tooltip("Should streamlines be enabled by default?")]
    private bool streamlinesEnabledByDefault = true;

    void Start()
    {
        // Find SimulationController if not assigned
        if (simulationController == null)
        {
            simulationController = FindFirstObjectByType<SimulationController>();
            if (simulationController == null)
            {
                Debug.LogError("[SimToggle] No SimulationController found in scene!");
                return;
            }
        }

        // Set up toggle listeners
        SetupToggleListeners();
        
        // Initialize toggle states
        InitializeToggleStates();
    }

    void SetupToggleListeners()
    {
        if (particleToggle != null)
        {
            particleToggle.onValueChanged.AddListener(OnParticleToggleChanged);
        }

        if (isosurfaceToggle != null)
        {
            isosurfaceToggle.onValueChanged.AddListener(OnIsosurfaceToggleChanged);
        }

        if (streamlineToggle != null)
        {
            streamlineToggle.onValueChanged.AddListener(OnStreamlineToggleChanged);
        }
    }

    void InitializeToggleStates()
    {
        // Set initial toggle states and ensure visual controllers are updated
        if (particleToggle != null)
        {
            particleToggle.isOn = particlesEnabledByDefault;
            simulationController.activatePart(particlesEnabledByDefault);
        }

        if (isosurfaceToggle != null)
        {
            isosurfaceToggle.isOn = isosurfacesEnabledByDefault;
            simulationController.activateIso(isosurfacesEnabledByDefault);
        }

        if (streamlineToggle != null)
        {
            streamlineToggle.isOn = streamlinesEnabledByDefault;
            simulationController.activateStream(streamlinesEnabledByDefault);
        }
    }

    void OnDestroy()
    {
        // Clean up listeners
        if (particleToggle != null)
            particleToggle.onValueChanged.RemoveListener(OnParticleToggleChanged);
        
        if (isosurfaceToggle != null)
            isosurfaceToggle.onValueChanged.RemoveListener(OnIsosurfaceToggleChanged);
        
        if (streamlineToggle != null)
            streamlineToggle.onValueChanged.RemoveListener(OnStreamlineToggleChanged);
    }

    // Toggle event handlers
    public void OnParticleToggleChanged(bool isOn)
    {
        if (simulationController != null)
        {
            simulationController.activatePart(isOn);
            Debug.Log($"[SimToggle] Particles: {(isOn ? "enabled" : "disabled")}");
        }
    }

    public void OnIsosurfaceToggleChanged(bool isOn)
    {
        if (simulationController != null)
        {
            simulationController.activateIso(isOn);
            Debug.Log($"[SimToggle] Isosurface: {(isOn ? "enabled" : "disabled")}");
        }
    }

    public void OnStreamlineToggleChanged(bool isOn)
    {
        if (simulationController != null)
        {
            simulationController.activateStream(isOn);
            Debug.Log($"[SimToggle] Streamlines: {(isOn ? "enabled" : "disabled")}");
        }
    }

    /// <summary>
    /// Re-applies the current toggle states to the SimulationController renderers.
    /// Call this after a new dataset is loaded so previously-disabled renderers stay off.
    /// </summary>
    public void ApplyCurrentToggleStates()
    {
        if (simulationController == null) return;
        bool particles   = particleToggle   != null ? particleToggle.isOn   : particlesEnabledByDefault;
        bool isosurfaces = isosurfaceToggle != null ? isosurfaceToggle.isOn : isosurfacesEnabledByDefault;
        bool streamlines = streamlineToggle != null ? streamlineToggle.isOn : streamlinesEnabledByDefault;
        simulationController.activatePart(particles);
        simulationController.activateIso(isosurfaces);
        simulationController.activateStream(streamlines);
    }

    // Public methods for programmatic control
    public void SetParticleSimulation(bool enabled)
    {
        if (particleToggle != null)
            particleToggle.isOn = enabled;
        else if (simulationController != null)
            simulationController.activatePart(enabled);
    }

    public void SetIsosurfaceSimulation(bool enabled)
    {
        if (isosurfaceToggle != null)
            isosurfaceToggle.isOn = enabled;
        else if (simulationController != null)
            simulationController.activateIso(enabled);
    }

    public void SetStreamlineSimulation(bool enabled)
    {
        if (streamlineToggle != null)
            streamlineToggle.isOn = enabled;
        else if (simulationController != null)
            simulationController.activateStream(enabled);
    }

    // Convenience methods for UI buttons
    public void ToggleParticles()
    {
        if (particleToggle != null)
            particleToggle.isOn = !particleToggle.isOn;
    }

    public void ToggleIsosurfaces()
    {
        if (isosurfaceToggle != null)
            isosurfaceToggle.isOn = !isosurfaceToggle.isOn;
    }

    public void ToggleStreamlines()
    {
        if (streamlineToggle != null)
            streamlineToggle.isOn = !streamlineToggle.isOn;
    }

    // Enable/disable all simulations at once
    public void EnableAllSimulations()
    {
        SetParticleSimulation(true);
        SetIsosurfaceSimulation(true);
        SetStreamlineSimulation(true);
    }

    public void DisableAllSimulations()
    {
        SetParticleSimulation(false);
        SetIsosurfaceSimulation(false);
        SetStreamlineSimulation(false);
    }

    // Get current simulation states
    public bool IsParticleSimulationEnabled()
    {
        return particleToggle != null ? particleToggle.isOn : false;
    }

    public bool IsIsosurfaceSimulationEnabled()
    {
        return isosurfaceToggle != null ? isosurfaceToggle.isOn : false;
    }

    public bool IsStreamlineSimulationEnabled()
    {
        return streamlineToggle != null ? streamlineToggle.isOn : false;
    }
}