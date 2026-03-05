using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Manages persistent local caching of datasets on-device.
/// Downloads full datasets from the server and stores them under Application.persistentDataPath/DatasetCache.
/// Enforces a configurable maximum total cache size (default 50 GB) using LRU eviction,
/// skipping the dataset that is currently loaded and playing.
/// </summary>
public class DatasetCacheManager : MonoBehaviour
{
    [Header("Cache Settings")]
    [SerializeField] 
    [Tooltip("Maximum total cache size in GB")]
    private float maxCacheSizeGB = 50.0f;
    
    [SerializeField]
    [Tooltip("All datasets are cached locally on device")]
    private float cacheThresholdGB = 10000.0f;  // Very large threshold - cache everything
    
    private string cacheRootPath;
    private Dictionary<string, DatasetCacheInfo> cachedDatasets = new Dictionary<string, DatasetCacheInfo>();
    // Dataset currently loaded/playing — never evicted by LRU
    private string activeDatasetName = null;
    
    private const string CACHE_INDEX_FILE = "cache_index.json";
    
    public float MaxCacheSizeBytes => maxCacheSizeGB * 1024 * 1024 * 1024;
    public float CacheThresholdBytes => cacheThresholdGB * 1024 * 1024 * 1024;
    
    private void Awake()
    {
        // Set cache path to persistent data path
        cacheRootPath = Path.Combine(Application.persistentDataPath, "DatasetCache");
        
        // Ensure cache directory exists
        if (!Directory.Exists(cacheRootPath))
        {
            Directory.CreateDirectory(cacheRootPath);
            Debug.Log($"[CacheManager] Created cache directory: {cacheRootPath}");
        }
        
        // Load cache index
        LoadCacheIndex();
        
        Debug.Log($"[CacheManager] Initialized — path: {cacheRootPath}, max: {maxCacheSizeGB} GB, threshold: {cacheThresholdGB} GB, cached datasets: {cachedDatasets.Count}");
    }
    
    /// <summary>
    /// Notify the cache manager which dataset is currently loaded/playing so it is never evicted.
    /// Call this after a successful load and pass null when no dataset is active.
    /// </summary>
    public void SetActiveDataset(string datasetName)
    {
        activeDatasetName = datasetName;
    }

    /// <summary>
    /// Check if a dataset should be cached based on its size.
    /// </summary>
    public bool ShouldCacheDataset(long datasetSizeBytes)
    {
        return datasetSizeBytes <= CacheThresholdBytes;
    }
    
    /// <summary>
    /// Check if a dataset is already cached.
    /// </summary>
    public bool IsDatasetCached(string datasetName)
    {
        if (!cachedDatasets.ContainsKey(datasetName))
            return false;
        
        // Verify files still exist
        string datasetPath = GetDatasetCachePath(datasetName);
        bool exists = Directory.Exists(datasetPath);
        
        if (!exists)
        {
            // Cache index is stale, remove entry
            Debug.LogWarning($"[CacheManager] Dataset {datasetName} missing from cache, removing index entry");
            cachedDatasets.Remove(datasetName);
            SaveCacheIndex();
        }
        
        return exists;
    }
    
    /// <summary>
    /// Get the local file path for a cached dataset file.
    /// Returns null if dataset is not cached.
    /// </summary>
    public string GetCachedFilePath(string datasetName, string relativePath)
    {
        if (!IsDatasetCached(datasetName))
            return null;
        
        string fullPath = Path.Combine(cacheRootPath, datasetName, relativePath);
        
        if (File.Exists(fullPath))
            return fullPath;
        
        return null;
    }
    
    /// <summary>
    /// Download and cache an entire dataset.
    /// </summary>
    public async Task<bool> DownloadAndCacheDataset(string datasetName, DatasetServerClient loader)
    {
        try
        {
            Debug.Log($"[CacheManager] Starting download of dataset: {datasetName}");
            
            // Get file list from server
            var fileListJson = await loader.GetDatasetFileListAsync(datasetName);
            if (string.IsNullOrEmpty(fileListJson))
            {
                Debug.LogError($"[CacheManager] Failed to get file list for {datasetName}");
                return false;
            }
            
            var fileList = JsonUtility.FromJson<DatasetFileListResponse>(fileListJson);
            if (fileList == null || fileList.files == null)
            {
                Debug.LogError($"[CacheManager] Invalid file list response for {datasetName}");
                return false;
            }
            
            // Calculate total size
            long totalSize = 0;
            foreach (var file in fileList.files)
            {
                totalSize += file.size;
            }

            // Build download list: replace streamline raw files with consolidated frame requests
            var downloadPaths = new List<string>();
            var consolidatedFrames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int skippedMeta = 0;
            int replacedStreamlineFiles = 0;

            foreach (var file in fileList.files)
            {
                string relativePath = file.path;
                if (relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    skippedMeta++;
                    continue;
                }

                if (TryGetConsolidatedStreamlinePath(relativePath, out string consolidatedPath))
                {
                    replacedStreamlineFiles++;
                    if (consolidatedFrames.Add(consolidatedPath))
                    {
                        downloadPaths.Add(consolidatedPath);
                    }
                    continue;
                }

                downloadPaths.Add(relativePath);
            }
            
            Debug.Log($"[CacheManager] Dataset {datasetName} has {fileList.files.Count} files, total size: {FormatBytes(totalSize)}");
            Debug.Log($"[CacheManager] Streamlines: replaced {replacedStreamlineFiles} raw files with {consolidatedFrames.Count} consolidated requests; skipped {skippedMeta} .meta files");
            
            // Make space if needed
            if (!EnsureCacheSpace(totalSize))
            {
                Debug.LogError($"[CacheManager] Unable to make space for dataset {datasetName}");
                return false;
            }
            
            // Create dataset directory
            string datasetPath = GetDatasetCachePath(datasetName);
            if (Directory.Exists(datasetPath))
            {
                Debug.Log($"[CacheManager] Cleaning existing cache for {datasetName}");
                Directory.Delete(datasetPath, true);
            }
            Directory.CreateDirectory(datasetPath);
            
            // Download info.txt first
            string infoContent = await loader.GetDatasetInfoAsync(datasetName);
            if (!string.IsNullOrEmpty(infoContent))
            {
                string infoPath = Path.Combine(datasetPath, "info.txt");
                File.WriteAllText(infoPath, infoContent);
            }
            
            // Download all files
            int downloaded = 0;
            int failed = 0;
            
            foreach (var relativePath in downloadPaths)
            {
                try
                {
                    byte[] data = await loader.LoadFileAsync(datasetName, relativePath);
                    if (data != null && data.Length > 0)
                    {
                        // Normalize path for cross-platform compatibility (Quest/Android)
                        string normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
                        if (IsConsolidatedStreamlineFramePath(relativePath))
                        {
                            normalizedPath += ".sbin";
                        }
                        string filePath = Path.Combine(datasetPath, normalizedPath);
                        
                        // Ensure directory exists
                        string fileDir = Path.GetDirectoryName(filePath);
                        if (!Directory.Exists(fileDir))
                        {
                            Directory.CreateDirectory(fileDir);
                        }
                        
                        // Write file
                        File.WriteAllBytes(filePath, data);
                        downloaded++;
                        
                        if (downloaded % 10 == 0)
                        {
                            Debug.Log($"[CacheManager] Downloaded {downloaded}/{downloadPaths.Count} files...");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[CacheManager] Empty data for {relativePath}");
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CacheManager] Failed to download {relativePath}: {ex.Message}");
                    failed++;
                }
            }
            
            Debug.Log($"[CacheManager] Download complete: {downloaded} success, {failed} failed");
            
            if (failed > 0 && downloaded == 0)
            {
                // Complete failure, cleanup
                Debug.LogError($"[CacheManager] Complete download failure, cleaning up");
                Directory.Delete(datasetPath, true);
                return false;
            }
            
            // Add to cache index
            var cacheInfo = new DatasetCacheInfo
            {
                datasetName = datasetName,
                sizeBytes = totalSize,
                fileCount = downloaded,
                lastAccessTime = DateTime.Now
            };
            
            cachedDatasets[datasetName] = cacheInfo;
            SaveCacheIndex();
            
            Debug.Log($"[CacheManager] Successfully cached dataset: {datasetName}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CacheManager] Error caching dataset {datasetName}: {ex.Message}");
            return false;
        }
    }

    private static bool TryGetConsolidatedStreamlinePath(string relativePath, out string consolidatedPath)
    {
        consolidatedPath = null;
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        if (!(relativePath.StartsWith("Vectors/") || relativePath.StartsWith("Streamlines/")))
        {
            return false;
        }

        string[] parts = relativePath.Split('/');
        if (parts.Length >= 3 && parts[parts.Length - 1].EndsWith(".raw", StringComparison.OrdinalIgnoreCase))
        {
            consolidatedPath = $"{parts[0]}/{parts[1]}";
            return true;
        }

        return false;
    }

    private static bool IsConsolidatedStreamlineFramePath(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return false;
        }

        if (!(relativePath.StartsWith("Vectors/") || relativePath.StartsWith("Streamlines/")))
        {
            return false;
        }

        int lastSlash = relativePath.LastIndexOf('/');
        string fileName = lastSlash >= 0 ? relativePath.Substring(lastSlash + 1) : relativePath;
        return !fileName.Contains(".");
    }
    
    /// <summary>
    /// Get the cache path for a specific dataset.
    /// </summary>
    public string GetDatasetCachePath(string datasetName)
    {
        return Path.Combine(cacheRootPath, datasetName);
    }
    
    /// <summary>
    /// Returns the names of all currently cached datasets.
    /// </summary>
    public List<string> GetCachedDatasetNames()
    {
        // Validate each entry still exists on disk, pruning stale ones
        var stale = new List<string>();
        foreach (var kvp in cachedDatasets)
        {
            if (!Directory.Exists(GetDatasetCachePath(kvp.Key)))
                stale.Add(kvp.Key);
        }
        foreach (var name in stale)
        {
            Debug.LogWarning($"[CacheManager] Pruning stale cache entry: {name}");
            cachedDatasets.Remove(name);
        }
        if (stale.Count > 0)
            SaveCacheIndex();

        return new List<string>(cachedDatasets.Keys);
    }

    /// <summary>
    /// Update last access time for a dataset (for LRU tracking).
    /// </summary>
    public void UpdateAccessTime(string datasetName)
    {
        if (cachedDatasets.ContainsKey(datasetName))
        {
            cachedDatasets[datasetName].lastAccessTime = DateTime.Now;
            SaveCacheIndex();
        }
    }
    
    /// <summary>
    /// Get current cache usage in bytes.
    /// </summary>
    public long GetCurrentCacheSize()
    {
        long total = 0;
        foreach (var info in cachedDatasets.Values)
        {
            total += info.sizeBytes;
        }
        return total;
    }
    
    /// <summary>
    /// Ensure there's enough space in cache for new data.
    /// Uses LRU eviction strategy.
    /// </summary>
    private bool EnsureCacheSpace(long requiredBytes)
    {
        long currentSize = GetCurrentCacheSize();
        long availableSpace = (long)MaxCacheSizeBytes - currentSize;
        
        if (availableSpace >= requiredBytes)
        {
            return true;
        }
        
        Debug.Log($"[CacheManager] Need to free up space. Current: {FormatBytes(currentSize)}, Need: {FormatBytes(requiredBytes)}");
        
        // Sort datasets by last access time (oldest first)
        var sortedDatasets = cachedDatasets.OrderBy(kvp => kvp.Value.lastAccessTime).ToList();
        
        foreach (var kvp in sortedDatasets)
        {
            if (availableSpace >= requiredBytes)
                break;

            // Never evict the dataset that is currently loaded and playing
            if (!string.IsNullOrEmpty(activeDatasetName) &&
                string.Equals(kvp.Key, activeDatasetName, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[CacheManager] Skipping eviction of active dataset: {kvp.Key}");
                continue;
            }

            // Remove this dataset
            Debug.Log($"[CacheManager] Evicting dataset: {kvp.Key} (last accessed: {kvp.Value.lastAccessTime})");
            RemoveDatasetFromCache(kvp.Key);
            
            currentSize = GetCurrentCacheSize();
            availableSpace = (long)MaxCacheSizeBytes - currentSize;
        }
        
        bool hasSpace = availableSpace >= requiredBytes;
        if (!hasSpace)
        {
            Debug.LogError($"[CacheManager] Unable to free enough space. Available: {FormatBytes(availableSpace)}, Required: {FormatBytes(requiredBytes)}");
        }
        
        return hasSpace;
    }
    
    /// <summary>
    /// Remove a dataset from cache.
    /// </summary>
    public void RemoveDatasetFromCache(string datasetName)
    {
        try
        {
            string datasetPath = GetDatasetCachePath(datasetName);
            if (Directory.Exists(datasetPath))
            {
                Directory.Delete(datasetPath, true);
                Debug.Log($"[CacheManager] Removed dataset from cache: {datasetName}");
            }
            
            cachedDatasets.Remove(datasetName);
            SaveCacheIndex();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CacheManager] Error removing dataset {datasetName}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Clear entire cache.
    /// </summary>
    public void ClearCache()
    {
        try
        {
            if (Directory.Exists(cacheRootPath))
            {
                Directory.Delete(cacheRootPath, true);
                Directory.CreateDirectory(cacheRootPath);
            }
            
            cachedDatasets.Clear();
            SaveCacheIndex();
            
            Debug.Log($"[CacheManager] Cache cleared");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CacheManager] Error clearing cache: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Load cache index from disk.
    /// </summary>
    private void LoadCacheIndex()
    {
        try
        {
            string indexPath = Path.Combine(cacheRootPath, CACHE_INDEX_FILE);
            if (File.Exists(indexPath))
            {
                string json = File.ReadAllText(indexPath);
                var index = JsonUtility.FromJson<CacheIndex>(json);
                
                if (index != null && index.datasets != null)
                {
                    cachedDatasets = index.datasets.ToDictionary(d => d.datasetName, d => d);
                    Debug.Log($"[CacheManager] Loaded cache index: {cachedDatasets.Count} datasets");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CacheManager] Error loading cache index: {ex.Message}");
            cachedDatasets = new Dictionary<string, DatasetCacheInfo>();
        }
    }
    
    /// <summary>
    /// Save cache index to disk.
    /// </summary>
    private void SaveCacheIndex()
    {
        try
        {
            var index = new CacheIndex
            {
                datasets = cachedDatasets.Values.ToList()
            };
            
            string json = JsonUtility.ToJson(index, true);
            string indexPath = Path.Combine(cacheRootPath, CACHE_INDEX_FILE);
            File.WriteAllText(indexPath, json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CacheManager] Error saving cache index: {ex.Message}");
        }
    }
    
    private string FormatBytes(long bytes)
    {
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
}

/// <summary>
/// Information about a cached dataset.
/// </summary>
[Serializable]
public class DatasetCacheInfo
{
    public string datasetName;
    public long sizeBytes;
    public int fileCount;
    public string lastAccessTimeString; // ISO 8601 format for serialization
    
    [NonSerialized]
    private DateTime? _lastAccessTime;
    
    public DateTime lastAccessTime
    {
        get
        {
            if (!_lastAccessTime.HasValue && !string.IsNullOrEmpty(lastAccessTimeString))
            {
                _lastAccessTime = DateTime.Parse(lastAccessTimeString);
            }
            return _lastAccessTime ?? DateTime.Now;
        }
        set
        {
            _lastAccessTime = value;
            lastAccessTimeString = value.ToString("o"); // ISO 8601 format
        }
    }
}

/// <summary>
/// Cache index for persistence.
/// </summary>
[Serializable]
public class CacheIndex
{
    public List<DatasetCacheInfo> datasets;
}

/// <summary>
/// Response from /api/dataset/<name>/download endpoint.
/// </summary>
[Serializable]
public class DatasetFileListResponse
{
    public string dataset;
    public List<DatasetFileInfo> files;
}

/// <summary>
/// Information about a file in a dataset.
/// </summary>
[Serializable]
public class DatasetFileInfo
{
    public string path;
    public long size;
}