using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Client for the dataset server. Handles dataset discovery, info retrieval,
/// file downloads for caching, and optional screenshot uploads.
/// </summary>
public class DatasetServerClient : MonoBehaviour
{
    [Header("Network Settings")]
    [SerializeField] private string serverUrl = "http://192.168.1.100:8080";
    [SerializeField] private float connectionTimeout = 120f;
    [SerializeField] [Range(1, 10)] private int maxConcurrentRequests = 2;

    private HttpClient httpClient;
    private string sessionToken = null;
    private string currentDataset;
    private int activeRequests = 0;
    private Queue<Action> requestQueue = new Queue<Action>();

    public string ServerUrl
    {
        get => serverUrl;
        set => serverUrl = value?.TrimEnd('/');
    }

    public string CurrentDataset => currentDataset;

    private void Awake()
    {
        httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(connectionTimeout);
    }

    public void SetSessionToken(string token)
    {
        sessionToken = token;
        if (!string.IsNullOrEmpty(sessionToken))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
        }
        else
        {
            httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }

    public async Task<bool> PairWithTokenAsync(string token)
    {
        try
        {
            string url = $"{serverUrl}/pair";
            Debug.Log($"[DatasetServer] Pairing with server at {url} using token {token}");

            var payload = JsonUtility.ToJson(new PairRequest { token = token });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(url, content);
            var text = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Debug.LogError($"[DatasetServer] Pairing failed: {response.StatusCode} - {text}");
                return false;
            }

            try
            {
                var jo = JsonUtility.FromJson<PairResponse>(text);
                if (!string.IsNullOrEmpty(jo.session_token))
                {
                    SetSessionToken(jo.session_token);
                    Debug.Log("[DatasetServer] Pairing successful, session token received.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DatasetServer] Failed to parse pair response: {ex.Message}");
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatasetServer] PairWithTokenAsync error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get list of available datasets from server.
    /// </summary>
    public async Task<DatasetListResponse> GetAvailableDatasetsAsync()
    {
        try
        {
            string url = $"{serverUrl}/api/datasets";
            Debug.Log($"[DatasetServer] Fetching datasets from: {url}");

            var response = await httpClient.GetStringAsync(url);
            var data = JsonUtility.FromJson<DatasetListResponse>(response);

            Debug.Log($"[DatasetServer] Found {data.datasets.Count} datasets");
            return data;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatasetServer] Failed to get datasets: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get info.txt content for a specific dataset.
    /// </summary>
    public async Task<string> GetDatasetInfoAsync(string datasetName)
    {
        try
        {
            string url = $"{serverUrl}/api/dataset/{datasetName}/info";
            Debug.Log($"[DatasetServer] Fetching info for dataset: {datasetName} from {url}");

            var response = await httpClient.GetStringAsync(url);
            var data = JsonUtility.FromJson<InfoResponse>(response);

            if (data == null || string.IsNullOrEmpty(data.info))
            {
                Debug.LogError($"[DatasetServer] Info response is empty for dataset: {datasetName}");
                Debug.LogError($"[DatasetServer] Raw response: {response}");
                return null;
            }

            return data.info;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatasetServer] Failed to get dataset info: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Download a specific file from a dataset with request throttling.
    /// Example paths: "Particles/0.raw", "Scalars/5.raw", "Vectors/10.raw".
    /// </summary>
    public async Task<byte[]> LoadFileAsync(string datasetName, string relativePath)
    {
        while (activeRequests >= maxConcurrentRequests)
        {
            await Task.Delay(10);
        }

        activeRequests++;
        try
        {
            string url = $"{serverUrl}/dataset/{datasetName}/{relativePath}";

            var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Debug.LogWarning($"[DatasetServer] File not found (404): {relativePath}");
                    return null;
                }

                Debug.LogError($"[DatasetServer] Failed to load file: {response.StatusCode}");
                return null;
            }

            byte[] data = await response.Content.ReadAsByteArrayAsync();
            return data;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatasetServer] Network error loading {relativePath}: {ex.Message}");
            return null;
        }
        finally
        {
            activeRequests--;
        }
    }

    /// <summary>
    /// Upload a screenshot to the server.
    /// </summary>
    public async Task<bool> UploadScreenshotAsync(byte[] imageData, string filename, string contentType)
    {
        try
        {
            string url = $"{serverUrl}/api/screenshot/upload";
            Debug.Log($"[DatasetServer] Uploading screenshot: {filename} ({imageData?.Length ?? 0} bytes)");

            using (var content = new MultipartFormDataContent())
            {
                var byteContent = new ByteArrayContent(imageData);
                byteContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                content.Add(byteContent, "file", filename);

                var response = await httpClient.PostAsync(url, content);
                string responseText = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogError($"[DatasetServer] Screenshot upload failed: {response.StatusCode}");
                    return false;
                }
            }

            Debug.Log("[DatasetServer] Screenshot upload complete");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatasetServer] Screenshot upload error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get file list for a dataset (for bulk download).
    /// </summary>
    public async Task<string> GetDatasetFileListAsync(string datasetName)
    {
        try
        {
            string url = $"{serverUrl}/api/dataset/{datasetName}/download";
            Debug.Log($"[DatasetServer] Fetching file list for: {datasetName}");

            var response = await httpClient.GetStringAsync(url);
            return response;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatasetServer] Failed to get file list: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Test if server is reachable. Uses a short timeout so the app does not
    /// block for a long time when the server is simply unavailable.
    /// </summary>
    public async Task<bool> TestConnectionAsync(float timeoutSeconds = 5f)
    {
        try
        {
            Debug.Log($"[DatasetServer] Testing connection to: {serverUrl}");
            using var cts = new System.Threading.CancellationTokenSource(
                TimeSpan.FromSeconds(timeoutSeconds));
            var response = await httpClient.GetAsync($"{serverUrl}/health", cts.Token);
            bool success = response.IsSuccessStatusCode;
            Debug.Log($"[DatasetServer] Connection test: {(success ? "SUCCESS" : "FAILED")}");
            return success;
        }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
            Debug.LogWarning($"[DatasetServer] Connection test timed out after {timeoutSeconds}s — server unreachable.");
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DatasetServer] Connection test failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Set the currently active dataset.
    /// </summary>
    public void SetCurrentDataset(string datasetName)
    {
        currentDataset = datasetName;
        Debug.Log($"[DatasetServer] Current dataset set to: {datasetName}");
    }

    private void OnDestroy()
    {
        httpClient?.Dispose();
    }
}

[Serializable]
public class PairResponse
{
    public string session_token;
    public long expires;
}

[Serializable]
public class PairRequest
{
    public string token;
}

/// <summary>
/// Response structure for dataset list API call.
/// </summary>
[Serializable]
public class DatasetListResponse
{
    public List<DatasetInfo> datasets;
}

/// <summary>
/// Information about a single dataset.
/// </summary>
[Serializable]
public class DatasetInfo
{
    public string name;
    public int frames;
    public string size;
    public long size_bytes;
}

/// <summary>
/// Response structure for dataset info API call.
/// </summary>
[Serializable]
public class InfoResponse
{
    public string info;
}
