using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI panel for selecting and managing datasets.
/// Shows a single unified list: cached datasets appear first (Name + Load + Trash),
/// followed by server datasets (Name + Download). Both can coexist for the same dataset —
/// trashing a cached row does not remove the server row, so re-downloading is always possible
/// without a full refresh.
///
/// IMPORTANT — prefab child naming requirements:
///   cachedRowPrefab  must have children named exactly: "DatasetName", "LoadButton", "TrashButton"
///   serverRowPrefab  must have children named exactly: "DatasetName", "DownloadButton"
/// The script does NOT fall back to index-based lookup, so names must match exactly.
/// </summary>
public class DatasetSelectionUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject selectionPanel;
    [SerializeField] private Transform datasetListContainer;
    [SerializeField] private GameObject cachedRowPrefab;  // Requires children: DatasetName, LoadButton, TrashButton
    [SerializeField] private GameObject serverRowPrefab;  // Requires children: DatasetName, DownloadButton
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button refreshButton;
    [SerializeField] private Button closeButton;

    [Header("Server Configuration UI")]
    [SerializeField] private TMP_InputField serverUrlInput;
    [SerializeField] private string defaultServerUrl = "http://192.168.1.100:8080";

    [Header("References")]
    [SerializeField] private DatasetServerClient serverClient;
    [SerializeField] private SimulationController simulationController;
    [SerializeField] private DatasetCacheManager cacheManager;
    [SerializeField] private DatasetMenuToggle datasetMenuToggle;
    [SerializeField] private Unity.VRTemplate.SimulationTimeScrubControl timeScrubControl;
    [Tooltip("Optional — scene-level haptics manager. Row button presses play haptics when assigned.")]
    [SerializeField] private DualControllerHaptics haptics;

    [Header("Settings")]
    [SerializeField] private bool showOnStartup = true;

    [Header("UI Colors")]
    [SerializeField] private Color activeLoadButtonColor = Color.green;
    [SerializeField] private Color defaultLoadButtonColor = Color.white;
    [SerializeField] private Sprite activeLoadSprite;
    [SerializeField] private Sprite defaultLoadSprite;

    private const string SERVER_URL_PREF_KEY = "PlasmaVR_ServerURL";

    // Plays a short haptic pulse on both controllers. Safe to call when haptics is null.
    private void PlayRowHaptic() => haptics?.PlayBothHaptics();

    // All spawned row GameObjects — used for bulk cleanup on refresh
    private List<GameObject> datasetRows = new List<GameObject>();

    // Tracks cached rows by dataset name so downloads can update them without duplicating
    private Dictionary<string, GameObject> cachedRowMap = new Dictionary<string, GameObject>();
    // Tracks server rows separately so refresh can replace only them without touching cached rows
    private List<GameObject> serverRows = new List<GameObject>();
    // Busy-state guards — prevent overlapping async operations
    private bool isRefreshing = false;
    private bool isLoadingDataset = false;
    // Tracks which dataset names are currently being downloaded
    private HashSet<string> activeDownloads = new HashSet<string>();
    // Track simulation state to refresh icons when play/pause changes
    private bool lastPlayingState = false;
    private string lastCurrentDataset = null;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        LoadSavedServerUrl();

        if (selectionPanel != null)
            selectionPanel.SetActive(false);

        if (refreshButton != null)
            refreshButton.onClick.AddListener(OnRefreshClicked);

        if (closeButton != null)
            closeButton.onClick.AddListener(HidePanel);


        if (showOnStartup)
            ShowPanel();
    }

    // -------------------------------------------------------------------------
    // Server URL helpers
    // -------------------------------------------------------------------------

    private void LoadSavedServerUrl()
    {
        // Priority: 1) PlayerPrefs saved URL (cached by user), 2) Inspector-assigned serverClient.ServerUrl,
        // 3) defaultServerUrl fallback.
        string urlToUse = defaultServerUrl;

        if (PlayerPrefs.HasKey(SERVER_URL_PREF_KEY))
        {
            urlToUse = PlayerPrefs.GetString(SERVER_URL_PREF_KEY);
            Debug.Log($"[DatasetUI] Loaded saved server URL from PlayerPrefs (preferred): {urlToUse}");
        }
        else if (serverClient != null && !string.IsNullOrEmpty(serverClient.ServerUrl))
        {
            urlToUse = serverClient.ServerUrl;
            Debug.Log($"[DatasetUI] Using Inspector server URL: {urlToUse}");
        }

        if (serverUrlInput != null)
            serverUrlInput.text = urlToUse;

        if (serverClient != null)
            serverClient.ServerUrl = urlToUse;
    }


    public async void OnRefreshClicked()
    {
        if (isRefreshing)
        {
            SetStatus("Already connecting to server, please wait...");
            return;
        }

        if (serverUrlInput == null || serverClient == null)
        {
            Debug.LogError("[DatasetUI] ServerUrlInput or DatasetServerClient not assigned!");
            return;
        }

        string url = serverUrlInput.text.Trim();

        if (string.IsNullOrEmpty(url))
        {
            SetStatus("Error: Server URL cannot be empty");
            return;
        }

        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
        {
            SetStatus("Error: URL must start with http:// or https://");
            return;
        }

        PlayerPrefs.SetString(SERVER_URL_PREF_KEY, url);
        PlayerPrefs.Save();

        serverClient.ServerUrl = url;

        Debug.Log($"[DatasetUI] Server URL updated to: {url}");
        SetStatus("Server URL saved. Refreshing datasets...");

        await RefreshDatasetListAsync();
    }

    /// <summary>
    /// Apply a server URL programmatically (e.g., from discovery) and trigger the existing
    /// refresh/connection flow. This reuses the same validation and saving as the manual flow.
    /// </summary>
    public async void ConnectUsingUrl(string url, string discoveryToken = null)
    {
        if (string.IsNullOrEmpty(url))
        {
            SetStatus("Error: Empty URL provided");
            return;
        }

        if (isRefreshing)
        {
            SetStatus("Already connecting to server, please wait...");
            return;
        }

        if (serverUrlInput != null)
            serverUrlInput.text = url;

        PlayerPrefs.SetString(SERVER_URL_PREF_KEY, url);
        PlayerPrefs.Save();

        if (serverClient != null)
            serverClient.ServerUrl = url;

        Debug.Log($"[DatasetUI] Connected via discovery. Server URL set to: {url}");
        SetStatus("Server URL saved. Refreshing datasets...");

        // If a discovery token is provided, attempt pairing first (no typing required)
        if (!string.IsNullOrEmpty(discoveryToken) && serverClient != null)
        {
            SetStatus("Pairing with server...");
            bool paired = await serverClient.PairWithTokenAsync(discoveryToken);
            if (!paired)
            {
                SetStatus("Pairing failed. Use manual URL or retry discovery.");
                return;
            }
            SetStatus("Paired successfully. Refreshing datasets...");
        }

        // Reuse the existing refresh flow
        await RefreshDatasetListAsync();
    }

    // -------------------------------------------------------------------------
    // Panel show / hide
    // -------------------------------------------------------------------------

    public async void ShowPanel()
    {
        if (selectionPanel != null)
            selectionPanel.SetActive(true);

        if (datasetMenuToggle != null)
            datasetMenuToggle.UpdatePanelStateVisual(true);

        // If a refresh is already running, don't wipe rows — just ensure the panel is visible
        if (isRefreshing)
        {
            SetStatus("Connecting to server, please wait...");
            return;
        }

        // Full reset — rebuild cached rows first, then attempt server
        SetStatus("Loading cached datasets...");
        ClearDatasetRows();
        List<string> cachedNames = new List<string>();
        if (cacheManager != null)
            cachedNames = cacheManager.GetCachedDatasetNames();
        foreach (string cachedName in cachedNames)
        {
            GameObject row = CreateCachedRow(cachedName);
            if (row != null)
                cachedRowMap[cachedName] = row;
        }
        SetStatus(cachedNames.Count == 0
            ? "No cached datasets found. Connecting to server..."
            : $"Found {cachedNames.Count} cached dataset(s). Connecting to server...");

        await RefreshDatasetListAsync();
    }

    public void HidePanel()
    {
        if (selectionPanel != null)
            selectionPanel.SetActive(false);

        if (datasetMenuToggle != null)
            datasetMenuToggle.UpdatePanelStateVisual(false);
    }

    // -------------------------------------------------------------------------
    // Refresh
    // -------------------------------------------------------------------------

    public async void RefreshDatasetList()
    {
        await RefreshDatasetListAsync();
    }

    private async System.Threading.Tasks.Task RefreshDatasetListAsync()
    {
        if (isRefreshing) return;
        isRefreshing = true;
        if (refreshButton != null) refreshButton.interactable = false;

        try
        {
            SetStatus("Connecting to server...");

            bool connected = await serverClient.TestConnectionAsync();
            if (!connected)
            {
                int cached = cachedRowMap.Count;
                SetStatus(cached == 0
                    ? "ERROR: Cannot connect to server and no cached datasets found."
                    : $"Offline mode. {cached} cached dataset(s) available.");
                return;
            }

            // Only replace server rows — cached rows stay untouched
            SetStatus("Loading server datasets...");
            ClearServerRows();

            DatasetListResponse response = await serverClient.GetAvailableDatasetsAsync();
            List<DatasetInfo> serverDatasets = (response != null && response.datasets != null)
                ? response.datasets
                : new List<DatasetInfo>();

            foreach (var ds in serverDatasets)
                CreateServerRow(ds);

            int cachedCount = cachedRowMap.Count;
            if (cachedCount == 0 && serverDatasets.Count == 0)
                SetStatus("No datasets found in cache or on server.");
            else
                SetStatus($"Found {cachedCount} cached, {serverDatasets.Count} on server.");
        }
        finally
        {
            isRefreshing = false;
            if (refreshButton != null) refreshButton.interactable = true;
        }
    }

    // -------------------------------------------------------------------------
    // Row creation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spawns a cached row (Name + Load + Trash).
    /// Buttons are looked up strictly by child name — no index fallback.
    /// </summary>
    private GameObject CreateCachedRow(string datasetName)
    {
        if (cachedRowPrefab == null || datasetListContainer == null)
        {
            Debug.LogError("[DatasetUI] cachedRowPrefab or datasetListContainer not assigned!");
            return null;
        }

        GameObject rowObj = Instantiate(cachedRowPrefab, datasetListContainer);
        // Ensure the instantiated row has zero local Z to avoid prefab offsets
        var rtTransform = rowObj.transform;
        Vector3 lp = rtTransform.localPosition;
        lp.z = 0f;
        rtTransform.localPosition = lp;
        var rect = rowObj.GetComponent<RectTransform>();
        if (rect != null)
        {
            var ap3 = rect.anchoredPosition3D;
            ap3.z = 0f;
            rect.anchoredPosition3D = ap3;
        }
        datasetRows.Add(rowObj);

        // Name label
        TextMeshProUGUI nameText = FindComponentByName<TextMeshProUGUI>(rowObj, "DatasetName");
        if (nameText != null)
            nameText.text = datasetName;
        else
            Debug.LogWarning($"[DatasetUI] cachedRowPrefab missing child 'DatasetName' (TMP) for {datasetName}");


        // Load button — strict name lookup only
        Button loadButton = FindComponentByName<Button>(rowObj, "LoadButton");
        if (loadButton != null)
        {
            loadButton.onClick.AddListener(() => { PlayRowHaptic(); OnLoadCachedDataset(datasetName); });

            // Set color based on whether this dataset is the currently loaded one
            bool isCurrent = (simulationController != null && simulationController.CurrentDatasetName == datasetName);
            var colors = loadButton.colors;
            colors.normalColor = isCurrent ? activeLoadButtonColor : defaultLoadButtonColor;
            colors.selectedColor = isCurrent ? activeLoadButtonColor : defaultLoadButtonColor;
            loadButton.colors = colors;

            // Toggle play/pause icon children (searches deeply). If none found, fall back to swapping sprite on the button Image.
            var playTransform = FindComponentByName<Transform>(rowObj, "playIcon");
            var pauseTransform = FindComponentByName<Transform>(rowObj, "pauseIcon");
            if (playTransform != null || pauseTransform != null)
            {
                if (isCurrent)
                {
                    bool isPlaying = (timeScrubControl != null) ? timeScrubControl.IsPlaying() : (simulationController != null && simulationController.playing);
                    if (playTransform != null) playTransform.gameObject.SetActive(!isPlaying);
                    if (pauseTransform != null) pauseTransform.gameObject.SetActive(isPlaying);
                }
                else
                {
                    if (playTransform != null) playTransform.gameObject.SetActive(true);
                    if (pauseTransform != null) pauseTransform.gameObject.SetActive(false);
                }
            }
            else
            {
                var loadImage = loadButton.GetComponent<Image>();
                if (loadImage != null)
                {
                            bool isPlayingImg = (timeScrubControl != null) ? timeScrubControl.IsPlaying() : (simulationController != null && simulationController.playing);
                            if (isCurrent && isPlayingImg && activeLoadSprite != null)
                                loadImage.sprite = activeLoadSprite;
                    else if (defaultLoadSprite != null)
                        loadImage.sprite = defaultLoadSprite;
                }
            }
        }
        else
        {
            Debug.LogWarning($"[DatasetUI] cachedRowPrefab missing child 'LoadButton' for {datasetName}");
        }

        // Trash button — strict name lookup only
        Button trashButton = FindComponentByName<Button>(rowObj, "TrashButton");
        if (trashButton != null)
            trashButton.onClick.AddListener(() => { PlayRowHaptic(); OnDeleteFromCache(datasetName, rowObj); });
        else
            Debug.LogWarning($"[DatasetUI] cachedRowPrefab missing child 'TrashButton' for {datasetName}");

        Debug.Log($"[DatasetUI] Created cached row for: {datasetName}");
        return rowObj;
    }

    /// <summary>
    /// Spawns a server row (Name + Download).
    /// Button is looked up strictly by child name — no index fallback.
    /// </summary>
    private GameObject CreateServerRow(DatasetInfo dataset)
    {
        if (serverRowPrefab == null || datasetListContainer == null)
        {
            Debug.LogError("[DatasetUI] serverRowPrefab or datasetListContainer not assigned!");
            return null;
        }

        GameObject rowObj = Instantiate(serverRowPrefab, datasetListContainer);
        // Ensure the instantiated row has zero local Z to avoid prefab offsets
        var rtTransform = rowObj.transform;
        Vector3 lp = rtTransform.localPosition;
        lp.z = 0f;
        rtTransform.localPosition = lp;
        var rect = rowObj.GetComponent<RectTransform>();
        if (rect != null)
        {
            var ap3 = rect.anchoredPosition3D;
            ap3.z = 0f;
            rect.anchoredPosition3D = ap3;
        }
        datasetRows.Add(rowObj);

        // Name label
        TextMeshProUGUI nameText = FindComponentByName<TextMeshProUGUI>(rowObj, "DatasetName");
        if (nameText != null)
            nameText.text = dataset.name;
        else
            Debug.LogWarning($"[DatasetUI] serverRowPrefab missing child 'DatasetName' (TMP) for {dataset.name}");

        // Download button — strict name lookup only
        Button downloadButton = FindComponentByName<Button>(rowObj, "DownloadButton");
        if (downloadButton != null)
            downloadButton.onClick.AddListener(() => { PlayRowHaptic(); OnDownloadDataset(dataset); });
        else
            Debug.LogWarning($"[DatasetUI] serverRowPrefab missing child 'DownloadButton' for {dataset.name}");

        serverRows.Add(rowObj);
        Debug.Log($"[DatasetUI] Created server row for: {dataset.name}");
        return rowObj;
    }

    // -------------------------------------------------------------------------
    // Button callbacks
    // -------------------------------------------------------------------------

    private void OnLoadCachedDataset(string datasetName)
    {
        if (isLoadingDataset)
        {
            SetStatus("Already loading a dataset, please wait...");
            return;
        }

        // If this dataset is already loaded, toggle play/pause (via time scrub control if available)
        if (simulationController != null && simulationController.CurrentDatasetName == datasetName)
        {
            if (timeScrubControl != null)
            {
                timeScrubControl.PlayOrPauseSimulation();
            }
            else
            {
                // fallback
                bool newState = !(simulationController != null && simulationController.playing);
                simulationController.toggleAnimation(newState);
            }
            bool playingNow = timeScrubControl != null ? timeScrubControl.IsPlaying() : (simulationController != null && simulationController.playing);
            SetStatus(playingNow ? "Playback resumed" : "Playback paused");
            UpdateLoadButtonColors();
            return;
        }

        SetStatus($"Loading {datasetName} from cache...");
        _ = LoadCachedDatasetAsync(datasetName);
    }

    private async System.Threading.Tasks.Task LoadCachedDatasetAsync(string datasetName)
    {
        isLoadingDataset = true;
        try
        {
            string datasetCachePath = cacheManager.GetDatasetCachePath(datasetName);
            string infoPath = System.IO.Path.Combine(datasetCachePath, "info.txt");

            if (!System.IO.File.Exists(infoPath))
            {
                SetStatus($"ERROR: Dataset info not found for {datasetName}");
                Debug.LogError($"[DatasetUI] info.txt not found at {infoPath}");
                return;
            }

            string infoContent = System.IO.File.ReadAllText(infoPath);
            bool success = await simulationController.LoadCachedDatasetAsync(datasetName, infoContent, 0);

            if (success)
            {
                SetStatus($"Successfully loaded {datasetName}!");
                UpdateLoadButtonColors();
            }
            else
                SetStatus($"ERROR: Failed to load {datasetName}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[DatasetUI] Load error: {ex.Message}");
            SetStatus($"ERROR: {ex.Message}");
        }
        finally
        {
            isLoadingDataset = false;
        }
    }
    /// <summary>
    /// Updates the color of all LoadButtons to reflect the currently loaded dataset.
    /// </summary>
    private void UpdateLoadButtonColors()
    {
        foreach (var kvp in cachedRowMap)
        {
            string datasetName = kvp.Key;
            GameObject rowObj = kvp.Value;
            Button loadButton = FindComponentByName<Button>(rowObj, "LoadButton");
            if (loadButton != null)
            {
                bool isCurrent = (simulationController != null && simulationController.CurrentDatasetName == datasetName);
                var colors = loadButton.colors;
                colors.normalColor = isCurrent ? activeLoadButtonColor : defaultLoadButtonColor;
                colors.selectedColor = isCurrent ? activeLoadButtonColor : defaultLoadButtonColor;
                loadButton.colors = colors;

                // Toggle play/pause icon children (searches deeply). If none found, fall back to swapping sprite on the button Image.
                var playTransform = FindComponentByName<Transform>(rowObj, "playIcon");
                var pauseTransform = FindComponentByName<Transform>(rowObj, "pauseIcon");
                if (playTransform != null || pauseTransform != null)
                {
                        if (isCurrent)
                        {
                            bool isPlaying = (timeScrubControl != null) ? timeScrubControl.IsPlaying() : (simulationController != null && simulationController.playing);
                            if (playTransform != null) playTransform.gameObject.SetActive(!isPlaying);
                            if (pauseTransform != null) pauseTransform.gameObject.SetActive(isPlaying);
                        }
                    else
                    {
                        if (playTransform != null) playTransform.gameObject.SetActive(true);
                        if (pauseTransform != null) pauseTransform.gameObject.SetActive(false);
                    }
                }
                else
                {
                    var loadImage = loadButton.GetComponent<Image>();
                    if (loadImage != null)
                    {
                        if (isCurrent && simulationController != null && simulationController.playing && activeLoadSprite != null)
                            loadImage.sprite = activeLoadSprite;
                        else if (defaultLoadSprite != null)
                            loadImage.sprite = defaultLoadSprite;
                    }
                }
            }
        }
    }

    private void Update()
    {
        if (simulationController == null && timeScrubControl == null) return;

        bool playingNow = (timeScrubControl != null) ? timeScrubControl.IsPlaying() : simulationController.playing;
        string currentNow = simulationController != null ? simulationController.CurrentDatasetName : null;

        if (playingNow != lastPlayingState || currentNow != lastCurrentDataset)
        {
            lastPlayingState = playingNow;
            lastCurrentDataset = currentNow;
            UpdateLoadButtonColors();
        }
    }

    private void OnDeleteFromCache(string datasetName, GameObject cachedRowObj)
    {
        if (cacheManager == null) return;

        if (isLoadingDataset)
        {
            SetStatus("Cannot delete while a dataset is loading, please wait...");
            return;
        }

        if (activeDownloads.Contains(datasetName))
        {
            SetStatus($"Cannot delete {datasetName} while it is being downloaded.");
            return;
        }

        cacheManager.RemoveDatasetFromCache(datasetName);
        Debug.Log($"[DatasetUI] Deleted {datasetName} from cache");
        SetStatus($"Deleted {datasetName} from cache.");

        // Remove only the cached row — the server row stays untouched
        cachedRowMap.Remove(datasetName);
        datasetRows.Remove(cachedRowObj);
        Destroy(cachedRowObj);
    }

    private void OnDownloadDataset(DatasetInfo dataset)
    {
        if (activeDownloads.Contains(dataset.name))
        {
            SetStatus($"{dataset.name} is already downloading, please wait...");
            return;
        }
        SetStatus($"Downloading {dataset.name}...");
        _ = DownloadAndCacheDatasetAsync(dataset);
    }

    private async System.Threading.Tasks.Task DownloadAndCacheDatasetAsync(DatasetInfo dataset)
    {
        activeDownloads.Add(dataset.name);
        // Disable the download button for this row immediately
        GameObject serverRow = serverRows.Find(r => r != null &&
            FindComponentByName<TextMeshProUGUI>(r, "DatasetName")?.text == dataset.name);
        Button downloadBtn = serverRow != null ? FindComponentByName<Button>(serverRow, "DownloadButton") : null;
        if (downloadBtn != null) downloadBtn.interactable = false;

        try
        {
            // Download and cache the full dataset via cacheManager
            bool success = await cacheManager.DownloadAndCacheDataset(dataset.name, serverClient);

            if (!success)
            {
                SetStatus($"ERROR: Failed to download {dataset.name}");
                return;
            }

            SetStatus($"Downloaded and cached {dataset.name}!");

            // If a cached row already exists for this name the files on disk have just been
            // refreshed — no UI change needed. If none exists yet, create one and slot it
            // above all server rows (i.e. after the last existing cached row).
            if (!cachedRowMap.ContainsKey(dataset.name))
            {
                GameObject newCachedRow = CreateCachedRow(dataset.name);
                if (newCachedRow != null)
                {
                    cachedRowMap[dataset.name] = newCachedRow;

                    // cachedRowMap now contains the new entry, so its count equals
                    // (previous cached count + 1). Place the row at index (count - 1)
                    // so it lands immediately after all previously existing cached rows.
                    newCachedRow.transform.SetSiblingIndex(cachedRowMap.Count - 1);
                }
            }
            // Server row is intentionally left in place for potential future re-downloads
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[DatasetUI] Download error: {ex.Message}");
            SetStatus($"ERROR: {ex.Message}");
        }
        finally
        {
            activeDownloads.Remove(dataset.name);
            if (downloadBtn != null) downloadBtn.interactable = true;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Finds a component on a direct or nested child with the exact given name.
    /// Does NOT fall back to index-based or type-only search, preventing
    /// mis-wiring when a prefab contains multiple buttons of the same type.
    /// </summary>
    private T FindComponentByName<T>(GameObject rowObj, string childName) where T : Component
{
    // GetComponentsInChildren(true) finds nested objects even if inactive
    T[] components = rowObj.GetComponentsInChildren<T>(true);
    foreach (var comp in components)
    {
        if (comp.gameObject.name == childName)
        {
            return comp;
        }
    }
    return null;
}

    private void ClearServerRows()
    {
        foreach (var row in serverRows)
        {
            if (row != null)
            {
                datasetRows.Remove(row);
                Destroy(row);
            }
        }
        serverRows.Clear();
    }

    private void ClearDatasetRows()
    {
        foreach (var row in datasetRows)
        {
            if (row != null)
                Destroy(row);
        }
        datasetRows.Clear();
        cachedRowMap.Clear();
        serverRows.Clear();
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;

        Debug.Log($"[DatasetUI] {message}");
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public void ReopenDatasetSelector() => ShowPanel();
    public void RefreshAndShowPanel() => ShowPanel();

    /// <summary>Returns true if the selection panel is currently visible.</summary>
    public bool IsPanelOpen => selectionPanel != null && selectionPanel.activeSelf;
}