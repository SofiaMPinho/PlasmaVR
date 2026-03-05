using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// High-performance isosurface renderer optimized for Quest 3.
/// Supports async loading, mesh caching, and optional LOD (Level of Detail) for distance-based quality scaling.
/// Loads isosurface meshes from .raw files and applies shader-based clipping and threshold effects.
/// </summary>
public class IsosurfaceRenderer : Simulation
{
    [Header("Performance Settings")]
    [SerializeField] private int maxVerticesPerMesh = 65000; // Quest 3 optimisation
    [SerializeField] private bool enableLOD = true; // Disabled by default for compatibility
    [SerializeField] private float[] lodDistances = { 10f, 25f, 50f };

    [Header("Memory Management")]
    [SerializeField] private int maxCachedMeshes = 10;
    [SerializeField] private bool useAsyncLoading = true; // Disabled by default for compatibility

    [Header("Local Cache Loading")]
    [SerializeField] private bool enableLazyLocalLoading = true;
    [SerializeField] private int preloadFrames = 2;
    [SerializeField] private int maxLocalMeshCache = 8;

    private string basePath = "";
    private int maxFrame = 0;
    private List<Mesh> meshes;
    private Dictionary<int, Mesh> meshCache;
    private MeshFilter filter = null;
    private LODGroup lodGroup;
    private List<bool> frameLoaded;

    // For async loading
    private Queue<int> loadingQueue = new Queue<int>();
    private readonly HashSet<int> loadingFrames = new HashSet<int>();
    private readonly Queue<int> loadedFrameQueue = new Queue<int>();
    private int lastDisplayedFrame = -1;

    public override bool startSim(int numFrames, Vector3 dims, string simPath)
    {
        try
        {
            meshes = new List<Mesh>();
            meshCache = new Dictionary<int, Mesh>();
            filter = this.GetComponent<MeshFilter>();

            // Setup LOD if enabled
            if (enableLOD)
            {
                SetupLODGroup();
            }

            this.transform.localPosition = new Vector3(-dims.x, -dims.y, -dims.z);
            this.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            maxFrame = numFrames;
            frameLoaded = new List<bool>(maxFrame);
            for (int i = 0; i < maxFrame; i++)
            {
                frameLoaded.Add(false);
            }
            basePath = System.IO.Path.Combine(simPath, "Scalars");
            if (!Directory.Exists(basePath))
            {
                string fallbackPath = System.IO.Path.Combine(simPath, "Isosurfaces");
                if (Directory.Exists(fallbackPath))
                {
                    basePath = fallbackPath;
                }
            }

            if (!Directory.Exists(basePath))
            {
                Debug.LogWarning($"[Isosurface] Base path missing: {basePath}. Creating empty isosurface frames.");
                meshes = new List<Mesh>();
                for (int i = 0; i < maxFrame; i++)
                {
                    meshes.Add(CreateEmptyMesh());
                }
                return true;
            }

            if (useLazyLocalLoading && enableLazyLocalLoading)
            {
                InitializeFramePlaceholders();
                StartCoroutine(PreloadFramesCoroutine());
            }
            else if (useAsyncLoading)
            {
                StartCoroutine(LoadSimulationAsync());
            }
            else
            {
                loadSimulation();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Isosurface] Failed to start simulation: {e.Message}");
            return false;
        }
        return true;
    }

    void SetupLODGroup()
    {
        lodGroup = GetComponent<LODGroup>();
        if (lodGroup == null)
        {
            lodGroup = gameObject.AddComponent<LODGroup>();
        }

        // Create LOD levels
        LOD[] lods = new LOD[lodDistances.Length + 1];
        for (int i = 0; i < lodDistances.Length; i++)
        {
            lods[i] = new LOD(1.0f / lodDistances[i], new Renderer[] { GetComponent<MeshRenderer>() });
        }
        lods[lodDistances.Length] = new LOD(0.01f, new Renderer[0]); // Cull distance

        lodGroup.SetLODs(lods);
        lodGroup.RecalculateBounds();
    }

    public override void displayFrame(int frame)
    {
        // Safety check: if filter is not initialized, skip
        if (filter == null || meshes == null || meshes.Count == 0)
        {
            return;
        }

        if (frame < 0 || frame >= meshes.Count) return;

        if (useLazyLocalLoading && enableLazyLocalLoading)
        {
            PreloadFutureFrames(frame);
            if (!IsFrameReady(frame))
            {
                // Uncomment for debugging frame-ready timing:
                // Not ready yet — lazy loading will trigger the load below
                return;
            }
        }

        // Use cached mesh if available
        if (meshCache.ContainsKey(frame))
        {
            filter.mesh = meshCache[frame];
        }
        else if (meshes[frame] != null)
        {
            filter.mesh = meshes[frame];

            // Cache frequently used meshes
            if (meshCache.Count < maxCachedMeshes)
            {
                meshCache[frame] = meshes[frame];
            }
        }

        lastDisplayedFrame = frame;
    }

    public override bool IsFrameReady(int frame)
    {
        if (!useLazyLocalLoading || !enableLazyLocalLoading)
        {
            return true;
        }

        return frameLoaded != null && frame >= 0 && frame < frameLoaded.Count && frameLoaded[frame];
    }


    /// <summary>
    /// Parses binary mesh data into a Unity Mesh.
    /// </summary>
    private Mesh ParseBinaryMesh(byte[] data)
    {
        using (var stream = new MemoryStream(data))
        using (var reader = new BinaryReader(stream))
        {
            // Read header
            int vertexCount = reader.ReadInt32();
            int triangleCount = reader.ReadInt32();

            if (vertexCount > maxVerticesPerMesh)
            {
                Debug.LogWarning($"[Isosurface] Mesh has too many vertices ({vertexCount})");
            }

            // Read vertices
            Vector3[] vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle()
                );
            }

            // Read triangles
            int[] triangles = new int[triangleCount * 3];
            for (int i = 0; i < triangleCount * 3; i++)
            {
                triangles[i] = reader.ReadInt32();
            }

            // Create mesh
            Mesh mesh = new Mesh();
            mesh.indexFormat = vertexCount > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.Optimize();

            return mesh;
        }
    }

    protected override void loadSimulation()
    {
        // Local file loading mode only
        for (int i = 0; i < maxFrame; i++)
        {
            string filePath = GetMeshPath(i);
            LoadMesh(filePath);
        }
    }

    private IEnumerator LoadSimulationAsync()
    {
        // Pre-load first few frames synchronously for immediate display
        int preloadCount = Mathf.Min(3, maxFrame);
        for (int i = 0; i < preloadCount; i++)
        {
            string filePath = GetMeshPath(i);
            LoadMesh(filePath);
        }

        // Load remaining frames asynchronously
        for (int i = preloadCount; i < maxFrame; i++)
        {
            loadingQueue.Enqueue(i);
        }

        StartCoroutine(ProcessLoadingQueue());
        yield return null;
    }

    private IEnumerator ProcessLoadingQueue()
    {
        while (loadingQueue.Count > 0)
        {
            int frameIndex = loadingQueue.Dequeue();
            string filePath = GetMeshPath(frameIndex);

            // Fire the async task and discard — errors are logged inside LoadMeshAsync
            _ = LoadMeshAsync(filePath, frameIndex);

            // Yield every few frames to prevent blocking
            if (frameIndex % 2 == 0)
            {
                yield return null;
            }
        }
    }

    private string GetMeshPath(int frameIndex)
    {
        return System.IO.Path.Combine(basePath, frameIndex + ".obj");
    }

    private void InitializeFramePlaceholders()
    {
        meshes = new List<Mesh>(maxFrame);
        frameLoaded = new List<bool>(maxFrame);
        loadingFrames.Clear();
        loadedFrameQueue.Clear();

        for (int i = 0; i < maxFrame; i++)
        {
            meshes.Add(null);
            frameLoaded.Add(false);
        }
    }

    private IEnumerator PreloadFramesCoroutine()
    {
        int preloadCount = Mathf.Clamp(preloadFrames, 0, maxFrame);
        for (int i = 0; i < preloadCount; i++)
        {
            RequestLocalFrameLoad(i);
            yield return null;
        }
    }

    private void RequestLocalFrameLoad(int frame)
    {
        if (frame < 0 || frame >= maxFrame)
        {
            return;
        }

        if (frameLoaded[frame] || loadingFrames.Contains(frame))
        {
            return;
        }

        loadingFrames.Add(frame);
        _ = LoadLocalFrameAsync(frame);
    }

    private void PreloadFutureFrames(int frame)
    {
        if (maxFrame <= 0)
        {
            return;
        }

        int endFrame = Mathf.Min(frame + preloadFrames, maxFrame - 1);
        for (int i = frame; i <= endFrame; i++)
        {
            RequestLocalFrameLoad(i);
        }
    }

    private async Task LoadLocalFrameAsync(int frame)
    {
        try
        {
            string filePath = GetMeshPath(frame);
            long startTime = System.Diagnostics.Stopwatch.GetTimestamp();
            
            MeshData meshData = await Task.Run(() => LoadMeshDataFromFile(filePath));
            
            long parseTime = System.Diagnostics.Stopwatch.GetTimestamp() - startTime;

            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                long mainThreadStart = System.Diagnostics.Stopwatch.GetTimestamp();
                
                if (frame < meshes.Count)
                {
                    if (meshes[frame] != null)
                    {
                        DestroyImmediate(meshes[frame]);
                    }
                    meshes[frame] = CreateMeshFromData(meshData);
                    MarkFrameLoaded(frame);
                }
                loadingFrames.Remove(frame);
                
                long mainThreadEnd = System.Diagnostics.Stopwatch.GetTimestamp();
                long mainThreadTicks = mainThreadEnd - mainThreadStart;
                long mainThreadMs = (long)(mainThreadTicks / (System.Diagnostics.Stopwatch.Frequency / 1000.0));
                long parseMs = (long)(parseTime / (System.Diagnostics.Stopwatch.Frequency / 1000.0));
                
                Debug.Log($"[Isosurface] Frame {frame}: Parse={parseMs}ms, MainThread={mainThreadMs}ms, Vertices={meshData.vertices?.Length ?? 0}, Triangles={meshData.triangles?.Length / 3 ?? 0}");
            });
        }
        catch (System.Exception e)
        {
            loadingFrames.Remove(frame);
            Debug.LogError($"[Isosurface] Error loading local frame {frame}: {e.Message}");
        }
    }

    private void MarkFrameLoaded(int frame)
    {
        if (frameLoaded == null || frame < 0 || frame >= frameLoaded.Count)
        {
            return;
        }

        frameLoaded[frame] = true;

        if (maxLocalMeshCache <= 0)
        {
            return;
        }

        loadedFrameQueue.Enqueue(frame);
        TrimLocalMeshCache();
    }

    private void TrimLocalMeshCache()
    {
        int safety = loadedFrameQueue.Count;
        while (loadedFrameQueue.Count > maxLocalMeshCache && safety-- > 0)
        {
            int candidate = loadedFrameQueue.Dequeue();
            if (candidate == lastDisplayedFrame)
            {
                loadedFrameQueue.Enqueue(candidate);
                continue;
            }

            if (candidate >= 0 && candidate < meshes.Count && meshes[candidate] != null)
            {
                DestroyImmediate(meshes[candidate]);
                meshes[candidate] = null;
            }

            if (meshCache.ContainsKey(candidate))
            {
                meshCache.Remove(candidate);
            }

            if (candidate >= 0 && candidate < frameLoaded.Count)
            {
                frameLoaded[candidate] = false;
            }
        }
    }

    // mesh loading with binary format
    void LoadMesh(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            meshes.Add(CreateEmptyMesh());
            return;
        }

        try
        {
            // Handle both binary and OBJ formats
            if (path.EndsWith(".bin"))
            {
                LoadBinaryMesh(path);
            }
            else if (path.EndsWith(".obj"))
            {
                // Use OBJ loading (no Dummiesman dependency)
                LoadOBJ(path);
            }
            else
            {
                Debug.LogWarning($"[Isosurface] Unsupported file format: {path}");
                meshes.Add(CreateEmptyMesh());
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Isosurface] Failed to load mesh: {path} - {e.Message}");
            meshes.Add(CreateEmptyMesh());
        }
    }

    private async Task LoadMeshAsync(string path, int frameIndex)
    {
        try
        {
            MeshData meshData = await Task.Run(() => LoadMeshDataFromFile(path));

            // Update on main thread
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                Mesh loadedMesh = CreateMeshFromData(meshData);
                if (frameIndex < meshes.Count)
                {
                    meshes[frameIndex] = loadedMesh;
                }
                else
                {
                    // Extend list if needed
                    while (meshes.Count <= frameIndex)
                    {
                        meshes.Add(CreateEmptyMesh());
                    }
                    meshes[frameIndex] = loadedMesh;
                }
            });
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Isosurface] Async load failed: {path} - {e.Message}");
        }
    }

    // Binary mesh format loading (much faster than OBJ)
    void LoadBinaryMesh(string path)
    {
        byte[] data = LoadFileData(path);
        if (data == null || data.Length == 0)
        {
            meshes.Add(CreateEmptyMesh());
            return;
        }

        using (var stream = new MemoryStream(data))
        using (var reader = new BinaryReader(stream))
        {
            // Read header
            int vertexCount = reader.ReadInt32();
            int triangleCount = reader.ReadInt32();

            if (vertexCount > maxVerticesPerMesh)
            {
                Debug.LogWarning($"[Isosurface] Mesh has too many vertices ({vertexCount}), decimating...");
                // Implement mesh decimation here if needed
            }

            // Read vertices
            Vector3[] vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle()
                );
            }

            // Read triangles
            int[] triangles = new int[triangleCount * 3];
            for (int i = 0; i < triangleCount * 3; i++)
            {
                triangles[i] = reader.ReadInt32();
            }

            // Create mesh
            Mesh mesh = new Mesh();
            mesh.indexFormat = vertexCount > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.Optimize();

            meshes.Add(mesh);
        }
    }

    // Fallback OBJ loading
    void LoadOBJ(string path)
    {
        string objText = LoadFileText(path);
        if (string.IsNullOrEmpty(objText))
        {
            meshes.Add(CreateEmptyMesh());
            return;
        }

        // Fast OBJ parsing without creating GameObjects
        Mesh mesh = CreateMeshFromData(ParseOBJData(objText));
        meshes.Add(mesh);
    }

    // Direct OBJ parsing without intermediate GameObjects
    private MeshData ParseOBJData(string objText)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        string[] lines = objText.Split('\n');
        int lineNum = 0;

        foreach (string line in lines)
        {
            lineNum++;
            string trimmed = line.Trim();
            
            // Skip empty lines and comments
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith("mtllib"))
            {
                continue;
            }

            // Split by whitespace(s), not just single space
            string[] parts = System.Text.RegularExpressions.Regex.Split(trimmed, @"\s+");

            if (parts.Length == 0) continue;

            if (parts[0] == "v" && parts.Length >= 4)
            {
                // Parse vertex
                if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                    float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    vertices.Add(new Vector3(x, y, z));
                }
                else
                {
                        Debug.LogWarning($"[Isosurface] Failed to parse vertex on line {lineNum}: {trimmed}");
                }
            }
            else if (parts[0] == "f" && parts.Length >= 4)
            {
                // Parse face (triangulate if needed)
                int[] faceIndices = new int[parts.Length - 1];
                bool validFace = true;
                
                for (int i = 1; i < parts.Length; i++)
                {
                    int idx = ParseVertexIndex(parts[i]) - 1;
                    if (idx < 0 || idx >= vertices.Count)
                    {
                            Debug.LogWarning($"[Isosurface] Invalid vertex index {parts[i]} on line {lineNum} (parsed as {idx}, max={vertices.Count - 1})");
                        validFace = false;
                        break;
                    }
                    faceIndices[i - 1] = idx;
                }
                
                // Add triangles (fan triangulation for polygons)
                if (validFace && faceIndices.Length >= 3)
                {
                    for (int i = 1; i < faceIndices.Length - 1; i++)
                    {
                        triangles.Add(faceIndices[0]);
                        triangles.Add(faceIndices[i]);
                        triangles.Add(faceIndices[i + 1]);
                    }
                }
            }
        }

        Debug.Log($"[Isosurface] OBJ parsed: {vertices.Count} vertices, {triangles.Count / 3} triangles from {lines.Length} lines");

        return new MeshData
        {
            vertices = vertices.ToArray(),
            triangles = triangles.ToArray(),
            hasData = triangles.Count > 0
        };
    }

    private int ParseVertexIndex(string indexStr)
    {
        string[] parts = indexStr.Split('/');
        if (int.TryParse(parts[0], out int index))
        {
            return index;
        }
        return -1;
    }

    private MeshData LoadMeshDataFromFile(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return MeshData.Empty();
        }

        // Try binary format first (check if .bin exists)
        string binPath = System.IO.Path.ChangeExtension(path, ".bin");
        if (File.Exists(binPath))
        {
            long fileReadStart = System.Diagnostics.Stopwatch.GetTimestamp();
            byte[] data = File.ReadAllBytes(binPath);
            long fileReadMs = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - fileReadStart) / (System.Diagnostics.Stopwatch.Frequency / 1000.0));
            
            long parseStart = System.Diagnostics.Stopwatch.GetTimestamp();
            MeshData result = ParseBinaryMeshData(data);
            long parseMs = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - parseStart) / (System.Diagnostics.Stopwatch.Frequency / 1000.0));
            
            Debug.Log($"[Isosurface] {System.IO.Path.GetFileName(binPath)}: FileRead={fileReadMs}ms ({data.Length / 1024}KB), Parse={parseMs}ms [binary]");
            return result;
        }

        // Check if OBJ file exists
        if (!File.Exists(path))
        {
            return MeshData.Empty();
        }

        // Quick format detection: peek at first 8 bytes
        long fileStartTime = System.Diagnostics.Stopwatch.GetTimestamp();
        bool isBinary = false;
        
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
        {
            if (fs.Length >= 8)
            {
                byte[] header = new byte[8];
                fs.Read(header, 0, 8);
                
                // Binary format: first 4 bytes = vertex count (int32), typically 100-100000
                int possibleVertexCount = System.BitConverter.ToInt32(header, 0);
                
                // OBJ text starts with '#', 'v', 'm', 'o' (ASCII 35, 118, 109, 111)
                // Binary vertex count is typically 100-500000, not in ASCII range
                if (possibleVertexCount > 50 && possibleVertexCount < 1000000 && header[0] != '#' && header[0] != 'v' && header[0] != 'm')
                {
                    isBinary = true;
                }
            }
        }

        MeshData meshResult;
        long parseStartTime = System.Diagnostics.Stopwatch.GetTimestamp();
        
        if (isBinary)
        {
            byte[] data = File.ReadAllBytes(path);
            long fileReadMs = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - fileStartTime) / (System.Diagnostics.Stopwatch.Frequency / 1000.0));
            
            Debug.Log($"[Isosurface] {System.IO.Path.GetFileName(path)}: binary data detected in .obj file");
            meshResult = ParseBinaryMeshData(data);
            
            long parseMs = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - parseStartTime) / (System.Diagnostics.Stopwatch.Frequency / 1000.0));
            Debug.Log($"[Isosurface] {System.IO.Path.GetFileName(path)}: FileRead={fileReadMs}ms ({data.Length / 1024}KB), Parse={parseMs}ms [binary]");
        }
        else
        {
            string objText = File.ReadAllText(path);
            long fileReadMs = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - fileStartTime) / (System.Diagnostics.Stopwatch.Frequency / 1000.0));
            
            meshResult = ParseOBJData(objText);
            
            long parseMs = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - parseStartTime) / (System.Diagnostics.Stopwatch.Frequency / 1000.0));
            Debug.Log($"[Isosurface] {System.IO.Path.GetFileName(path)}: FileRead={fileReadMs}ms ({objText.Length / 1024}KB), Parse={parseMs}ms [OBJ text]");
        }
        
        return meshResult;
    }

    private MeshData ParseBinaryMeshData(byte[] data)
    {
        if (data == null || data.Length < 8)
        {
            Debug.LogWarning($"[Isosurface] Invalid binary data (length={data?.Length ?? 0})");
            return MeshData.Empty();
        }

        using (var stream = new MemoryStream(data))
        using (var reader = new BinaryReader(stream))
        {
            int vertexCount = reader.ReadInt32();
            int triangleCount = reader.ReadInt32();

            // Allow point clouds (vertices with no triangles)
            if (vertexCount <= 0)
            {
                Debug.LogWarning($"[Isosurface] Invalid vertex count: {vertexCount}");
                return MeshData.Empty();
            }

            if (vertexCount > maxVerticesPerMesh)
            {
                // Do NOT clamp and continue — triangle indices in the file are based on the
                // original vertex count, so clamping silently creates out-of-range indices.
                // Skip the whole mesh instead and log a clear error.
                Debug.LogWarning($"[Isosurface] Mesh has {vertexCount} vertices which exceeds the limit of {maxVerticesPerMesh}. Skipping mesh to avoid corrupt triangle indices. Increase maxVerticesPerMesh or reduce mesh complexity.");
                return MeshData.Empty();
            }

            Vector3[] vertices = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new Vector3(
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle()
                );
            }

            int[] triangles = new int[triangleCount * 3];
            for (int i = 0; i < triangleCount * 3; i++)
            {
                triangles[i] = reader.ReadInt32();
            }

            return new MeshData
            {
                vertices = vertices,
                triangles = triangles,
                hasData = vertices.Length > 0 // Changed: count mesh as valid if it has vertices
            };
        }
    }

    private Mesh CreateMeshFromData(MeshData data)
    {
        if (!data.hasData || data.vertices == null || data.vertices.Length == 0)
        {
            return CreateEmptyMesh();
        }

        long meshStart = System.Diagnostics.Stopwatch.GetTimestamp();
        
        Mesh mesh = new Mesh();
        mesh.indexFormat = data.vertices.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = data.vertices;
        
        // Handle point clouds (no triangles)
        if (data.triangles != null && data.triangles.Length > 0)
        {
            mesh.triangles = data.triangles;
            mesh.RecalculateNormals();
        }
        
        mesh.RecalculateBounds();
        mesh.Optimize();
        
        return mesh;
    }

    private byte[] LoadFileData(string path)
    {
        // Try filesystem first (for cached files)
        if (System.IO.File.Exists(path))
            return System.IO.File.ReadAllBytes(path);

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android: Try UnityWebRequest
        var www = UnityEngine.Networking.UnityWebRequest.Get(path);
        www.SendWebRequest();
        while (!www.isDone) { }
        if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            return www.downloadHandler.data;
#endif
        Debug.LogWarning($"[Isosurface] File not found: {path}");
        return null;
    }

    private string LoadFileText(string path)
    {
        // Try filesystem first (for cached files)
        if (System.IO.File.Exists(path))
            return System.IO.File.ReadAllText(path);

#if UNITY_ANDROID && !UNITY_EDITOR
        // Android: Try UnityWebRequest
        var www = UnityEngine.Networking.UnityWebRequest.Get(path);
        www.SendWebRequest();
        while (!www.isDone) { }
        if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            return www.downloadHandler.text;
#endif
        Debug.LogWarning($"[Isosurface] Text file not found: {path}");
        return null;
    }

    private Mesh CreateEmptyMesh()
    {
        return new Mesh();
    }

    private struct MeshData
    {
        public Vector3[] vertices;
        public int[] triangles;
        public bool hasData;

        public static MeshData Empty()
        {
            return new MeshData
            {
                vertices = System.Array.Empty<Vector3>(),
                triangles = System.Array.Empty<int>(),
                hasData = false
            };
        }
    }

    void OnDestroy()
    {
        // Clean up meshes to prevent memory leaks
        if (meshes != null)
        {
            foreach (var mesh in meshes)
            {
                if (mesh != null)
                {
                    DestroyImmediate(mesh);
                }
            }
        }

        if (meshCache != null)
        {
            foreach (var mesh in meshCache.Values)
            {
                if (mesh != null)
                {
                    DestroyImmediate(mesh);
                }
            }
        }
    }
}

// Helper class for main thread dispatching
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    private Queue<System.Action> _executionQueue = new Queue<System.Action>();

    public static UnityMainThreadDispatcher Instance()
    {
        if (_instance == null)
        {
            _instance = FindFirstObjectByType<UnityMainThreadDispatcher>();
            if (_instance == null)
            {
                GameObject go = new GameObject("UnityMainThreadDispatcher");
                _instance = go.AddComponent<UnityMainThreadDispatcher>();
                DontDestroyOnLoad(go);
            }
        }
        return _instance;
    }

    public void Enqueue(System.Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }

    void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }
}