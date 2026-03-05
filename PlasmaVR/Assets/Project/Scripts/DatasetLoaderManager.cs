using UnityEngine;

/// <summary>
/// Manager script showing how to integrate dataset selection into your VR app.
/// Add this to a GameObject in your main scene to enable dataset loading.
/// </summary>
public class DatasetLoaderManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The DatasetSelectionUI component - handles dataset picker UI")]
    public DatasetSelectionUI datasetSelector;

    [Tooltip("The DatasetServerClient component - handles HTTP requests")]
    public DatasetServerClient serverClient;

    [Tooltip("The SimulationController - manages simulation playback")]
    public SimulationController simulationController;

    [Header("Settings")]
    [Tooltip("Show dataset selector automatically on app start")]
    public bool showOnStart = true;

    [Tooltip("Server URL - get this from PlasmaVRServer.py console output")]
    public string serverUrl = "http://192.168.1.100:8080";

    void Start()
    {
        // Verify all references are assigned
        if (datasetSelector == null)
        {
            Debug.LogError("[DatasetManager] DatasetSelectionUI not assigned!");
            return;
        }

        if (serverClient == null)
        {
            Debug.LogError("[DatasetManager] DatasetServerClient not assigned!");
            return;
        }

        if (simulationController == null)
        {
            Debug.LogError("[DatasetManager] SimulationController not assigned!");
            return;
        }

        // Set server URL (you can also do this in the Inspector)
        serverClient.ServerUrl = serverUrl;

        // Show dataset selector if enabled
        if (showOnStart)
        {
            ShowDatasetSelector();
        }
    }

    public void ShowDatasetSelector()
    {
        datasetSelector.ShowPanel();
    }

    public void RefreshAndShowDatasetSelector()
    {
        datasetSelector.RefreshAndShowPanel();
    }
    public async void LoadDatasetByName(string datasetName)
    {
        try
        {
            // Get dataset info first
            string infoContent = await serverClient.GetDatasetInfoAsync(datasetName);
            if (string.IsNullOrEmpty(infoContent))
            {
                Debug.LogError($"[DatasetManager] Failed to get info for dataset {datasetName}");
                return;
            }

            await simulationController.LoadCachedDatasetAsync(datasetName, infoContent);
            Debug.Log($"[DatasetManager] Successfully loaded dataset: {datasetName}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[DatasetManager] Failed to load dataset {datasetName}: {e.Message}");
        }
    }

    public async void TestServerConnection()
    {
        bool connected = await serverClient.TestConnectionAsync();

        if (connected)
        {
            Debug.Log($"[DatasetManager] OK - Successfully connected to server: {serverUrl}");
        }
        else
        {
            Debug.LogError($"[DatasetManager] FAIL - Failed to connect to server: {serverUrl}. Check: server is running (python PlasmaVRServer.py), Quest and PC are on the same WiFi, firewall is not blocking port 8080.");
        }
    }
}
