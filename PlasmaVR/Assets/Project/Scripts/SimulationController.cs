using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Controls all simulations, manages frame playback, and coordinates loading of simulation data.
/// Handles play/pause, frame navigation, and activating/deactivating individual simulation renderers.
/// Uses cached datasets with optional lazy frame loading.
/// </summary>
public class SimulationController : MonoBehaviour
{
    /// <summary>
    /// The name of the currently loaded dataset (null if none loaded).
    /// </summary>
    public string CurrentDatasetName { get; private set; } = null;
    public List<Simulation> sims;

    public Simulation particleSim = null;
    public Simulation isosurfaceSim = null;
    public Simulation streamlineSim = null;

    public GridCreator grid = null;
    
    [Header("Server Settings")]
    [SerializeField] private DatasetServerClient serverClient;
    [SerializeField] private DatasetCacheManager cacheManager;
    [SerializeField] private SimulationToggleController toggleController;

    // public SliderSizer sliderSetup;

    public int maxFrame = 0;
    public int currFrame = 0;
    private int requestedFrame = -1;

    public bool playing = true;
    private bool toUpdate = false;
    private bool waitingForFrame = false;

    [Header("UI Settings")]
    [Tooltip("Optional UI Dropdown to select target FPS (5..30 in steps of 5). Assign in Inspector.")]
    public Dropdown fpsDropdown;

    [Tooltip("Optional TextMeshPro Dropdown to select target FPS (5..30 in steps of 5). Assign in Inspector if using TMP.")]
    public TMP_Dropdown fpsTMPDropdown;

    // cached listener so we can remove it on destroy (used for both Dropdown and TMP_Dropdown)
    private UnityEngine.Events.UnityAction<int> cbFpsDropdown;

    [Header("Playback Settings")]
    [Tooltip("Target simulation FPS (frames per second). Set to 0 for unlimited.")]
    [Range(0,240)]
    public float targetFPS = 30f;
    [SerializeField] private float baseFrameDelay = 0.07f;
    private float playbackSpeed = 1f;
    private float playbackAccumulator = 0f;

    // Track simulation loop count
    private int simulationLap = 0;

    protected IEnumerator coroutine;

    //Sim Settings
    private string basePath = "";
    private string title = "";
    private string description = "";

    private List<string> axisNames = new List<string>();
    private Vector3 axisMin = new Vector3();
    private Vector3 axisMax = new Vector3();
    private Vector3Int simRes = new Vector3Int();


    // Start is called before the first frame update
    void Start()
    {
        // Wait for dataset selection from the server/cache flow
        // DatasetSelectionUI will call LoadCachedDatasetAsync when user picks
        Debug.Log("[Simulation] Waiting for dataset selection.");
        DisableAllRenderers();
        return;
    }

    // Awake runs before Start; use it to initialize UI wiring even while Start returns early
    private void Awake()
    {
        // Prefer TMP dropdown if available (Unity VR template often uses TMP); fall back to UI Dropdown
        if (fpsTMPDropdown != null)
        {
            fpsTMPDropdown.ClearOptions();
            var stringOptions = new List<string>();
            for (int f = 5; f <= 30; f += 5) stringOptions.Add(f.ToString());
            fpsTMPDropdown.AddOptions(stringOptions);

            // Choose nearest matching index for current targetFPS
            int bestIdx = 0; float bestDiff = float.MaxValue;
            for (int i = 0; i < stringOptions.Count; ++i)
            {
                float val = (i + 1) * 5;
                float d = Mathf.Abs(val - targetFPS);
                if (d < bestDiff) { bestDiff = d; bestIdx = i; }
            }
            fpsTMPDropdown.value = bestIdx;

            cbFpsDropdown = (int idx) => {
                int fps = (idx + 1) * 5;
                targetFPS = fps;
                Debug.Log($"[Simulation] Target FPS set to {targetFPS}");
            };
            fpsTMPDropdown.onValueChanged.AddListener(cbFpsDropdown);
        }
        else if (fpsDropdown != null)
        {
            // Populate options: 5,10,...,30 (numeric-only labels)
            fpsDropdown.ClearOptions();
            var options = new List<Dropdown.OptionData>();
            for (int f = 5; f <= 30; f += 5) options.Add(new Dropdown.OptionData(f.ToString()));
            fpsDropdown.AddOptions(options);

            // Choose nearest matching index for current targetFPS (fallback to first)
            int bestIdx = 0;
            float bestDiff = float.MaxValue;
            for (int i = 0; i < options.Count; ++i)
            {
                float val = (i + 1) * 5;
                float d = Mathf.Abs(val - targetFPS);
                if (d < bestDiff)
                {
                    bestDiff = d;
                    bestIdx = i;
                }
            }
            fpsDropdown.value = bestIdx;

            cbFpsDropdown = (int idx) => {
                int fps = (idx + 1) * 5;
                targetFPS = fps;
                Debug.Log($"[Simulation] Target FPS set to {targetFPS}");
            };
            fpsDropdown.onValueChanged.AddListener(cbFpsDropdown);
        }
    }
    
    /// <summary>
    /// Disable all simulation renderers (particle, isosurface, streamline).
    /// Call this on startup when waiting for dataset selection.
    /// </summary>
    private void DisableAllRenderers()
    {
        if (particleSim != null && particleSim.gameObject != null)
            particleSim.gameObject.SetActive(false);
        if (isosurfaceSim != null && isosurfaceSim.gameObject != null)
            isosurfaceSim.gameObject.SetActive(false);
        if (streamlineSim != null && streamlineSim.gameObject != null)
            streamlineSim.gameObject.SetActive(false);
        Debug.Log("[Simulation] All renderers disabled");
    }
    
    /// <summary>
    /// Enable all simulation renderers.
    /// Call this after successfully loading a dataset.
    /// </summary>
    private void EnableAllRenderers()
    {
        if (particleSim != null && particleSim.gameObject != null)
            particleSim.gameObject.SetActive(true);
        if (isosurfaceSim != null && isosurfaceSim.gameObject != null)
            isosurfaceSim.gameObject.SetActive(true);
        if (streamlineSim != null && streamlineSim.gameObject != null)
            streamlineSim.gameObject.SetActive(true);
        Debug.Log("[Simulation] All renderers enabled");
    }

    /// <summary>
    /// Re-applies the current UI toggle states to the renderers so a newly loaded
    /// simulation respects whatever the user had toggled off before loading.
    /// </summary>
    private void ApplyToggleStates()
    {
        if (toggleController == null)
            toggleController = FindFirstObjectByType<SimulationToggleController>();
        toggleController?.ApplyCurrentToggleStates();
    }
    
    /// <summary>
    /// Load dataset from cached storage or download and cache it first.
    /// Called by DatasetSelectionUI when user selects a dataset.
    /// </summary>
    public async Task<bool> LoadCachedDatasetAsync(string datasetName, string infoContent, long datasetSizeBytes = 0)
    {
        Debug.Log($"[Simulation] Loading dataset: {datasetName} (Size: {FormatBytes(datasetSizeBytes)})");
        
        try
        {
            // Ensure server client is assigned
            if (serverClient == null)
            {
                serverClient = FindFirstObjectByType<DatasetServerClient>();
                if (serverClient == null)
                {
                    Debug.LogError("[Simulation] DatasetServerClient is not assigned and could not be found in scene.");
                    return false;
                }
            }
            
            // Ensure cache manager is assigned
            if (cacheManager == null)
            {
                cacheManager = FindFirstObjectByType<DatasetCacheManager>();
                if (cacheManager == null)
                {
                    Debug.LogError("[Simulation] CacheManager not found. Cannot load datasets without cache.");
                    return false;
                }
            }

            // Parse info.txt content
            ParseInfoContent(infoContent);

            // Reset current frame to 0 for new dataset
            currFrame = 0;

            // Update scrub slider range for this dataset
            var scrubControl = GetComponent<Unity.VRTemplate.SimulationTimeScrubControl>();
            if (scrubControl != null)
            {
                scrubControl.RefreshSliderRange(maxFrame, 0);
            }
            
            // Always use cache: download if missing, then lazy-load from local cache
            bool isCached = cacheManager.IsDatasetCached(datasetName);
            if (!isCached)
            {
                Debug.Log($"[Simulation] Dataset not cached, downloading: {datasetName} ({FormatBytes(datasetSizeBytes)})");
                bool downloadSuccess = await cacheManager.DownloadAndCacheDataset(datasetName, serverClient);

                if (!downloadSuccess)
                {
                    Debug.LogError($"[Simulation] Failed to download dataset {datasetName}");
                    return false;
                }
            }
            else
            {
                Debug.Log($"[Simulation] Dataset already cached, loading: {datasetName}");
                cacheManager.UpdateAccessTime(datasetName);
            }

            string localDatasetPath = cacheManager.GetDatasetCachePath(datasetName);
            basePath = System.IO.Path.GetFullPath(localDatasetPath);
            Debug.Log($"[Simulation] Using cached dataset path: {basePath}");
            
            Vector3 dims = new Vector3((axisMax.x - axisMin.x) / 20f, (axisMax.y - axisMin.y) / 20f, (axisMax.z - axisMin.z) / 20f);
            
            sims = new List<Simulation>();
            sims.Add(particleSim);
            sims.Add(isosurfaceSim);
            sims.Add(streamlineSim);
            
            List<Simulation> toRemove = new List<Simulation>();
            
            // Initialize each simulation
            foreach (Simulation sim in sims)
            {
                // Activate before startSim — coroutines cannot start on inactive GameObjects.
                if (sim != null && sim.gameObject != null)
                    sim.gameObject.SetActive(true);

                // Use local cached file loading with lazy loading
                sim.SetLazyLocalLoading(true);
                Debug.Log($"[Simulation] Starting cached simulation: {sim.name} with path: {basePath}");
                
                if (!sim.startSim(maxFrame, dims, basePath))
                {
                    Debug.LogError($"[Simulation] Failed to start simulation: {sim.name}");
                    toRemove.Add(sim);
                }
                else
                {
                    Debug.Log($"[Simulation] Successfully started simulation: {sim.name}");
                }
            }
            
            foreach (Simulation sim in toRemove)
            {
                sims.Remove(sim);
            }
            toRemove.Clear();
            
            // Setup grid
            if (grid != null)
            {
                grid.upperBound = dims.x;
                grid.lowerBound = -dims.x;
                grid.dataLowerBound = axisMin.x;
                grid.dataUpperBound = axisMax.x;
                grid.createMesh();
                grid.createAxisText();
            }
            
            // Enable renderers now that dataset is successfully loaded, then re-apply
            // toggle states so anything the user had disabled stays hidden.
            EnableAllRenderers();
            ApplyToggleStates();
            
            // Mark the dataset as successfully loaded only here, after everything succeeded
            CurrentDatasetName = datasetName;
            cacheManager.SetActiveDataset(datasetName);
            
            // Refresh clipping box VFX list after renderers are enabled
            // (VFX instances were inactive during initial ClippingBox.Start())
            var clippingBox = FindFirstObjectByType<ClippingBox>();
            if (clippingBox != null)
            {
                // Removed: clippingBox.RefreshVfxList();
                Debug.Log("[Simulation] Refreshed ClippingBox VFX list after enabling renderers");
            }

            // Display frame 0 immediately so user sees something (currFrame already set to 0 earlier)
            toUpdate = true; // Trigger Update() to call displayFrame on all renderers
            if (sims != null && sims.Count > 0)
            {
                foreach (Simulation sim in sims)
                {
                    sim.displayFrame(currFrame);
                }
            }

            // Reset playing state: start paused so the dataset does not auto-play on load
            playing = false;
            
            Debug.Log($"[Simulation] Successfully loaded dataset: {datasetName} (Mode: CACHED)");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Simulation] Failed to load dataset: {ex.Message}");
            CurrentDatasetName = null;
            cacheManager?.SetActiveDataset(null);
            DisableAllRenderers();
            return false;
        }
    }
    
    /// <summary>
    /// Load a single frame from a cached dataset.
    /// This allows users to view individual frames without loading the entire dataset.
    /// Called by DatasetSelectionUI when user selects a specific frame.
    /// </summary>
    public Task<bool> LoadSingleFrameAsync(string datasetName, int frameNumber, string datasetCachePath)
    {
        Debug.Log($"[Simulation] Loading single frame {frameNumber} from dataset: {datasetName}");
        
        try
        {
            // Ensure cache manager is assigned
            if (cacheManager == null)
            {
                cacheManager = FindFirstObjectByType<DatasetCacheManager>();
                if (cacheManager == null)
                {
                    Debug.LogError("[Simulation] CacheManager not found for frame loading.");
                    return Task.FromResult(false);
                }
            }
            
            // Read the dataset info file to get metadata (axis names, bounds, etc.)
            string infoPath = System.IO.Path.Combine(datasetCachePath, "info.txt");
            if (!System.IO.File.Exists(infoPath))
            {
                Debug.LogError($"[Simulation] info.txt not found at {infoPath}");
                return Task.FromResult(false);
            }
            
            string infoContent = System.IO.File.ReadAllText(infoPath);
            ParseInfoContent(infoContent);
            
            // For single frame, maxFrame is set to 1 (only one frame to display)
            // Update scrub slider range
            var scrubControl = GetComponent<Unity.VRTemplate.SimulationTimeScrubControl>();
            if (scrubControl != null)
            {
                scrubControl.RefreshSliderRange(1, 0);
            }
            
            // Set frame path to the single frame directory
            // Frame structure: DatasetCache/{dataset}/frames/{frameNumber}/{Particles,Scalars,Vectors}/0.raw
            string framePath = System.IO.Path.Combine(datasetCachePath, "frames", frameNumber.ToString());
            basePath = System.IO.Path.GetFullPath(framePath);
            
            Debug.Log($"[Simulation] Loading from frame path: {basePath}");
            
            Vector3 dims = new Vector3(
                (axisMax.x - axisMin.x) / 20f,
                (axisMax.y - axisMin.y) / 20f,
                (axisMax.z - axisMin.z) / 20f
            );
            
            // Reset frame counter and list of sims
            currFrame = 0;
            sims = new List<Simulation>();
            sims.Add(particleSim);
            sims.Add(isosurfaceSim);
            sims.Add(streamlineSim);
            
            List<Simulation> toRemove = new List<Simulation>();
            
            // Initialize each simulation with frame-specific path
            foreach (Simulation sim in sims)
            {
                // Activate before startSim — coroutines cannot start on inactive GameObjects.
                if (sim != null && sim.gameObject != null)
                    sim.gameObject.SetActive(true);

                sim.SetLazyLocalLoading(true);
                Debug.Log($"[Simulation] Starting single frame simulation: {sim.name} with path: {basePath}");
                
                // Use maxFrame=1 because we only have one frame loaded
                if (!sim.startSim(1, dims, basePath))
                {
                    Debug.LogError($"[Simulation] Failed to start simulation: {sim.name}");
                    toRemove.Add(sim);
                }
                else
                {
                    Debug.Log($"[Simulation] Successfully started single frame simulation: {sim.name}");
                }
            }
            
            foreach (Simulation sim in toRemove)
            {
                sims.Remove(sim);
            }
            toRemove.Clear();
            
            // Setup grid
            if (grid != null)
            {
                grid.upperBound = dims.x;
                grid.lowerBound = -dims.x;
                grid.dataLowerBound = axisMin.x;
                grid.dataUpperBound = axisMax.x;
                grid.createMesh();
                grid.createAxisText();
            }
            
            // Enable renderers now that frame is successfully loaded
            EnableAllRenderers();
            // Re-apply UI toggle states so disabled renderers stay hidden on the new frame.
            ApplyToggleStates();
            
            // Display the single frame immediately
            toUpdate = true;
            if (sims != null && sims.Count > 0)
            {
                foreach (Simulation sim in sims)
                {
                    sim.displayFrame(currFrame);
                }
            }
            
            // Reset playing state
            playing = false; // Don't play/animate single frame, just show it
            
            Debug.Log($"[Simulation] Successfully loaded single frame {frameNumber} from {datasetName}");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Simulation] Failed to load single frame: {ex.Message}");
            DisableAllRenderers();
            return Task.FromResult(false);
        }
    }
    
    private string FormatBytes(long bytes)
    {
        if (bytes == 0) return "Unknown size";
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
    
    /// <summary>
    /// Parse info.txt content string.
    /// </summary>
    private void ParseInfoContent(string content)
    {
        string[] lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        if (lines.Length == 0)
            throw new FormatException("info.txt is empty.");

        // Read first line: maxFrame, title, description
        string[] startInfo = lines[0].Split(';');
        if (startInfo.Length < 3)
            throw new FormatException($"info.txt header line malformed (expected 3 fields, got {startInfo.Length}): '{lines[0]}'");

        maxFrame = int.Parse(startInfo[0]);
        title = startInfo[1];
        description = startInfo[2];
        
        Debug.Log($"[Simulation] Parsed header — MaxFrame: {maxFrame}, Title: '{title}'");
        
        List<string> names = new List<string>();
        List<float> mins = new List<float>();
        List<float> maxs = new List<float>();
        List<int> res = new List<int>();
        List<string> units = new List<string>();
        
        // Read axis info (lines 1-3)
        if (lines.Length >= 4)
        {
            for (int i = 1; i < 4; i++)
            {
                string[] axisInfo = lines[i].Split(';');
                if (axisInfo.Length < 5)
                    throw new FormatException($"info.txt axis line {i} malformed (expected 5 fields, got {axisInfo.Length}): '{lines[i]}'");
                names.Add(axisInfo[0]);
                res.Add(int.Parse(axisInfo[1]));
                mins.Add(float.Parse(axisInfo[2], CultureInfo.InvariantCulture));
                maxs.Add(float.Parse(axisInfo[3], CultureInfo.InvariantCulture));
                units.Add(axisInfo[4]);
            }
        }
        
        axisNames = names;
        simRes = new Vector3Int(res[0], res[1], res[2]);
        axisMin = new Vector3(mins[0], mins[1], mins[2]);
        axisMax = new Vector3(maxs[0], maxs[1], maxs[2]);
        
        Debug.Log($"[Simulation] Parsed axes — Resolution: {simRes}, Range: {axisMin}→{axisMax}, Names: [{string.Join(", ", axisNames)}]");
    }

    private void OnEnable()
    {
        this.coroutine = AnimateSimulation();
        StartCoroutine(coroutine);
    }

    private void OnDestroy()
    {
        if (fpsDropdown != null && cbFpsDropdown != null)
            fpsDropdown.onValueChanged.RemoveListener(cbFpsDropdown);
        if (fpsTMPDropdown != null && cbFpsDropdown != null)
            fpsTMPDropdown.onValueChanged.RemoveListener(cbFpsDropdown);
    }

    private int GetTargetFrame()
    {
        return requestedFrame >= 0 ? requestedFrame : currFrame;
    }

    // Update is called once per frame
    void Update()
    {
        if (toUpdate && sims != null && sims.Count > 0)
        {
            int targetFrame = GetTargetFrame();
            // For lazy loading: wait until all active sims have data for this frame
            if (!AreAllActiveFramesReady(targetFrame))
            {
                foreach (Simulation sim in sims)
                {
                    sim.displayFrame(targetFrame); // triggers async load if needed
                }

                toUpdate = false;

                if (!waitingForFrame)
                {
                    waitingForFrame = true;
                    StartCoroutine(WaitForFrameReady(0.05f));
                }
                return;
            }

            //draw.changeFrame(currFrame);
            //info.updateInfo(currFrame);
            foreach (Simulation sim in sims)
            {
                sim.displayFrame(targetFrame);
            }
            currFrame = targetFrame;
            requestedFrame = -1;
            toUpdate = false;
        }
    }

    private bool AreAllActiveFramesReady(int frame)
    {
        if (sims == null || sims.Count == 0) return false;
        foreach (Simulation sim in sims)
        {
            if (!sim.IsFrameReady(frame))
            {
                return false;
            }
        }
        return true;
    }

    private IEnumerator WaitForFrameReady(float delay)
    {
        // Wait until frame is actually ready or timeout
        float timeout = 5f; // 5 second timeout per frame
        float elapsed = 0f;
        
        while (elapsed < timeout && !AreAllActiveFramesReady(GetTargetFrame()))
        {
            yield return new WaitForSeconds(delay);
            elapsed += delay;
        }
        
        if (elapsed >= timeout)
        {
            Debug.LogWarning($"[Simulation] Frame {currFrame} load timeout after {timeout}s. Continuing anyway.");
        }
        
        waitingForFrame = false;
        toUpdate = true;
    }

    private void loadInfo()
    {
        //Get info lines
        string path = System.IO.Path.Combine(basePath, "info.txt");
        string[] lines = null;

        //Get all lines from info.txt
#if UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log("[Simulation] Loading info.txt via UnityWebRequest: " + path);
        
        var www = UnityEngine.Networking.UnityWebRequest.Get(path);
        www.SendWebRequest();
        while (!www.isDone) { }
        if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            string text = www.downloadHandler.text;
            lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        }
        else
        {
            Debug.LogError("[Simulation] Failed to load info.txt: " + www.error);
            return;
        }
#else
        lines = System.IO.File.ReadAllLines(path);
#endif

        if (lines == null)
        {
            Debug.LogError("[Simulation] info.txt lines is null — file loading failed at: " + path);
            return;
        }
        if (lines.Length == 0)
        {
            Debug.LogError("[Simulation] info.txt is empty — cannot parse header at: " + path);
            return;
        }
        string[] startInfo = lines[0].Split(';');
        maxFrame = Int32.Parse(startInfo[0]);
        title = startInfo[1];
        description = startInfo[2];
        
        Debug.Log($"[Simulation] Parsed header — MaxFrame: {maxFrame}, Title: '{title}'");

        List<string> names = new List<string>();
        List<float> mins = new List<float>();
        List<float> maxs = new List<float>();
        List<int> res = new List<int>();
        List<string> units = new List<string>();

        //Read axis info
        if (lines.Length < 4)
        {
            Debug.LogWarning("[Simulation] info.txt has fewer lines than expected — axis data may be incomplete");
        }
        else
        {
            for (int i = 1; i < 4; i++)
            {
                string[] axisInfo = lines[i].Split(';');
                names.Add(axisInfo[0]);
                res.Add(Int32.Parse(axisInfo[1]));
                mins.Add(float.Parse(axisInfo[2], CultureInfo.InvariantCulture));
                maxs.Add(float.Parse(axisInfo[3], CultureInfo.InvariantCulture));
                units.Add(axisInfo[4]);
            }
        }
        axisNames = names;
        simRes = new Vector3Int(res[0], res[1], res[2]);
        axisMin = new Vector3(mins[0], mins[1], mins[2]);
        axisMax = new Vector3(maxs[0], maxs[1], maxs[2]);
        
        Debug.Log($"[Simulation] Parsed axes — Resolution: {simRes}, Range: {axisMin}→{axisMax}, Names: [{string.Join(", ", axisNames)}]");

    }

    public void toggleAnimation(bool activation)
    {
        playing = activation;
        if (!playing)
        {
            playbackAccumulator = 0f;
        }
    }

    public void SetPlaybackSpeed(float speed)
    {
        playbackSpeed = Mathf.Clamp(speed, 0.1f, 5.0f);
    }

    /// <summary>
    /// Forces all simulations to redraw the current frame without advancing time.
    /// Useful when visual parameters (e.g., clipping) change while playback is paused.
    /// </summary>
    public void RequestRefreshCurrentFrame()
    {
        toUpdate = true;
    }

    public void changeFrame(float num)
    {
        int targetFrame = (int)num;
        targetFrame = Mathf.Clamp(targetFrame, 0, maxFrame - 1);
        requestedFrame = targetFrame;
        toUpdate = true;
    }

    public void activatePart(bool activation)
    {
        if (activation)
        {
            if (!sims.Contains(particleSim))
            {
                sims.Add(particleSim);
                particleSim.gameObject.SetActive(true);
                // partInfo.SetActive(true);
            }
        }
        else
        {
            if (sims.Contains(particleSim))
            {
                sims.Remove(particleSim);
                particleSim.gameObject.SetActive(false);
                // partInfo.SetActive(false);
            }
        }
    }

    public void activateIso(bool activation)
    {
        if (activation)
        {
            if (!sims.Contains(isosurfaceSim))
            {
                sims.Add(isosurfaceSim);
                isosurfaceSim.gameObject.SetActive(true);
                // isoInfo.SetActive(true);
            }
        }
        else
        {
            if (sims.Contains(isosurfaceSim))
            {
                sims.Remove(isosurfaceSim);
                isosurfaceSim.gameObject.SetActive(false);
                // isoInfo.SetActive(false);
            }
        }
    }

    public void activateStream(bool activation)
    {
        if (activation)
        {
            if (!sims.Contains(streamlineSim))
            {
                sims.Add(streamlineSim);
                streamlineSim.gameObject.SetActive(true);
                // streamInfo.SetActive(true);
            }
        }
        else
        {
            if (sims.Contains(streamlineSim))
            {
                sims.Remove(streamlineSim);
                streamlineSim.gameObject.SetActive(false);
                // streamInfo.SetActive(false);
            }
        }
    }

    public void nextFrame()
    {
        int next = currFrame + 1;
        if (next >= maxFrame)
        {
            next = 0;
            simulationLap++;
        }
        requestedFrame = next;
        toUpdate = true;
    }

    public void prevFrame()
    {
        int prev = currFrame - 1;
        if (prev < 0)
        {
            prev = maxFrame - 1;
        }
        requestedFrame = prev;
        toUpdate = true;
    }


    protected IEnumerator AnimateSimulation()
    {
        while (true)
        {
            yield return null;

            if (!playing)
            {
                continue;
            }


            float frameDelay = (targetFPS > 0f) ? (1f / targetFPS) : (baseFrameDelay / Mathf.Max(playbackSpeed, 0.01f));
            playbackAccumulator += Time.deltaTime;

            if (playbackAccumulator < frameDelay)
            {
                continue;
            }

            // Only advance if no pending target and current frame is fully loaded
            if (requestedFrame < 0 && AreAllActiveFramesReady(currFrame))
            {
                playbackAccumulator -= frameDelay;
                this.nextFrame();
            }
            else
            {
                // Prevent runaway accumulation while waiting for slow frames
                playbackAccumulator = Mathf.Min(playbackAccumulator, frameDelay);
            }
        }
    }
}
