using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Renders streamline data from .raw files with configurable color gradients and magnitude scaling.
/// Supports three-color gradients (min/mid/max) and automatic scaling based on data range.
/// Applies shader-based clipping and threshold effects to streamline meshes.
/// </summary>
public class StreamlineRenderer : Simulation
{
    public string basePath = "";
    public int maxFrame = 0;

    [Header("Color Gradient Settings")]
    [Tooltip("Color for minimum magnitude values")]
    public Color minColor = new Color(0.18f, 0.24f, 0.38f, 1f);
    [Tooltip("Color for middle magnitude values")]
    public Color midColor = new Color(0.38f, 0.55f, 0.50f, 1f);
    [Tooltip("Color for maximum magnitude values")]
    public Color maxColor = new Color(0.82f, 0.80f, 0.64f, 1f);
    [Tooltip("Use three-color gradient (min->mid->max) instead of two-color")]
    public bool useThreeColorGradient = true;
    [Tooltip("Use auto-scaling based on data range (recommended)")]
    public bool autoScale = true;
    [Tooltip("Manual min magnitude (used when autoScale is false)")]
    public float manualMinMagnitude = 0f;
    [Tooltip("Manual max magnitude (used when autoScale is false)")]
    public float manualMaxMagnitude = 1f;

    [Header("Local Cache Loading")]
    [SerializeField] private bool enableLazyLocalLoading = true;
    [SerializeField] private int preloadFrames = 2;
    [SerializeField] private int maxFrameCache = 6;

    private float globalMinMagnitude = float.MaxValue;
    private float globalMaxMagnitude = float.MinValue;

    private List<Mesh> meshes;
    private List<bool> frameLoaded;
    private readonly HashSet<int> loadingFrames = new HashSet<int>();
    private readonly Queue<int> loadedFrameQueue = new Queue<int>();
    private int lastDisplayedFrame = -1;

    private MeshFilter filter = null;

    [ContextMenu("Apply Default Palette")]
    public void ApplyDefaultPalette()
    {
        minColor = new Color(0.18f, 0.24f, 0.38f, 1f);
        midColor = new Color(0.38f, 0.55f, 0.50f, 1f);
        maxColor = new Color(0.82f, 0.80f, 0.64f, 1f);
    }

    protected override void loadSimulation()
    {
        if (useLazyLocalLoading && enableLazyLocalLoading)
        {
            InitializeFramePlaceholders();
            StartCoroutine(PreloadFramesCoroutine());
            return;
        }

        if (!Directory.Exists(basePath))
        {
            Debug.LogWarning($"[Streamline] Base path missing: {basePath}. Creating empty streamline frames.");
            for (int i = 0; i < maxFrame; i++)
            {
                meshes.Add(CreateEmptyMesh());
            }
            return;
        }

        for (int i = 0; i < maxFrame; i++)
        {
            string framePath = Path.Combine(basePath, i.ToString());
            loadFrame(framePath);
        }
    }

    public override void displayFrame(int frame)
    {
        if (filter == null || meshes == null || meshes.Count == 0)
        {
            return;
        }

        if (frame < 0 || frame >= meshes.Count)
        {
            return;
        }

        if (useLazyLocalLoading && enableLazyLocalLoading)
        {
            PreloadFutureFrames(frame);
            if (!IsFrameReady(frame))
            {
                return;
            }
        }

        filter.mesh = meshes[frame];
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

    private List<Color> CalculateColors(IReadOnlyList<float> magnitudes)
    {
        List<Color> colors = new List<Color>();

        List<float> validMagnitudes = new List<float>();
        foreach (float mag in magnitudes)
        {
            if (!float.IsInfinity(mag) && !float.IsNaN(mag))
            {
                validMagnitudes.Add(mag);
            }
        }

        float frameMinMag = float.MaxValue;
        float frameMaxMag = float.MinValue;
        foreach (float mag in validMagnitudes)
        {
            if (mag < frameMinMag) frameMinMag = mag;
            if (mag > frameMaxMag) frameMaxMag = mag;
        }

        if (validMagnitudes.Count == 0)
        {
            frameMinMag = 0f;
            frameMaxMag = 1f;
        }
        else
        {
            if (frameMinMag < globalMinMagnitude) globalMinMagnitude = frameMinMag;
            if (frameMaxMag > globalMaxMagnitude) globalMaxMagnitude = frameMaxMag;
        }

        float minMag = autoScale ? globalMinMagnitude : manualMinMagnitude;
        float maxMag = autoScale ? globalMaxMagnitude : manualMaxMagnitude;
        float range = maxMag - minMag;
        bool hasRange = range >= 0.0001f;

        foreach (float magnitude in magnitudes)
        {
            Color vertexColor;

            if (!hasRange)
            {
                vertexColor = Color.white;
            }
            else if (float.IsInfinity(magnitude) || float.IsNaN(magnitude))
            {
                vertexColor = minColor;
            }
            else
            {
                float t = Mathf.Clamp01((magnitude - minMag) / range);

                if (useThreeColorGradient)
                {
                    if (t < 0.5f)
                    {
                        vertexColor = Color.Lerp(minColor, midColor, t * 2f);
                    }
                    else
                    {
                        vertexColor = Color.Lerp(midColor, maxColor, (t - 0.5f) * 2f);
                    }
                }
                else
                {
                    vertexColor = Color.Lerp(minColor, maxColor, t);
                }
            }

            colors.Add(vertexColor);
        }

        return colors;
    }

    public override bool startSim(int numFrames, Vector3 dims, string simPath)
    {
        try
        {
            meshes = new List<Mesh>();
            filter = GetComponent<MeshFilter>();

            globalMinMagnitude = float.MaxValue;
            globalMaxMagnitude = float.MinValue;

            transform.localPosition = new Vector3(-dims.x, -dims.y, -dims.z);
            transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            maxFrame = numFrames;
            frameLoaded = new List<bool>(maxFrame);
            for (int i = 0; i < maxFrame; i++)
            {
                frameLoaded.Add(false);
            }
            basePath = Path.Combine(simPath, "Vectors");
            if (!Directory.Exists(basePath))
            {
                string fallbackPath = Path.Combine(simPath, "Streamlines");
                if (Directory.Exists(fallbackPath))
                {
                    basePath = fallbackPath;
                }
            }

            loadSimulation();
        }
        catch
        {
            return false;
        }
        return true;
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
            StreamlineFrameData data = await Task.Run(() => ReadStreamlineFrameFromDisk(frame));

            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                if (!data.hasData)
                {
                    if (frame < meshes.Count)
                    {
                        if (meshes[frame] != null)
                        {
                            Object.DestroyImmediate(meshes[frame]);
                        }
                        meshes[frame] = CreateEmptyMesh();
                    }
                    MarkFrameLoaded(frame);
                    loadingFrames.Remove(frame);
                    return;
                }

                List<Color> colors = CalculateColors(data.magnitudes);

                Mesh m = new Mesh();
                m.vertices = data.vertices;
                m.colors = colors.ToArray();
                m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetIndices(data.indices, MeshTopology.Lines, 0, true);

                if (frame < meshes.Count)
                {
                    if (meshes[frame] != null)
                    {
                        Object.DestroyImmediate(meshes[frame]);
                    }
                    meshes[frame] = m;
                }

                MarkFrameLoaded(frame);
                loadingFrames.Remove(frame);
            });
        }
        catch (System.Exception e)
        {
            loadingFrames.Remove(frame);
            Debug.LogError($"[Streamline] Error loading local frame {frame}: {e.Message}");
        }
    }

    private StreamlineFrameData ReadStreamlineFrameFromDisk(int frame)
    {
        long totalStartTime = System.Diagnostics.Stopwatch.GetTimestamp();
        
        string framePath = Path.Combine(basePath, frame.ToString());
        
        // First, try loading consolidated .sbin file (200 files → 1 file)
        string consolidatedPath = Path.Combine(basePath, $"{frame}.sbin");
        if (File.Exists(consolidatedPath))
        {
            long fileReadStart = System.Diagnostics.Stopwatch.GetTimestamp();
            byte[] consolidatedData = File.ReadAllBytes(consolidatedPath);
            long fileReadMs = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - fileReadStart) / (System.Diagnostics.Stopwatch.Frequency / 1000.0));
            
            long parseStart = System.Diagnostics.Stopwatch.GetTimestamp();
            StreamlineFrameData result = ParseConsolidatedStreamlineData(consolidatedData);
            long consolidatedParseMs = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - parseStart) / (System.Diagnostics.Stopwatch.Frequency / 1000.0));
            
            long consolidatedTotalMs = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - totalStartTime) / (System.Diagnostics.Stopwatch.Frequency / 1000.0));
            Debug.Log($"[Streamline] Frame {frame}: CONSOLIDATED 1 file, {consolidatedData.Length / 1024}KB, FileRead={fileReadMs}ms, Parse={consolidatedParseMs}ms, Total={consolidatedTotalMs}ms");
            
            return result;
        }

        Debug.Log($"[Streamline] Frame {frame}: Consolidated file not found, falling back to individual .raw files");
        
        // Fallback: Load from individual .raw files (slower, 200 file reads)
        if (!Directory.Exists(framePath))
        {
            return StreamlineFrameData.Empty();
        }

        List<Vector3> vertices = new List<Vector3>();
        List<float> magnitudes = new List<float>();
        List<int> indices = new List<int>();
        bool newline = true;
        int vertcount = 0;
        bool anyLineData = false;
        
        long fileReadTimeTotal = 0;
        int filesRead = 0;
        long totalBytes = 0;

        int numLines = 200;
        for (int lineIndex = 0; lineIndex < numLines; lineIndex++)
        {
            string linepath = Path.Combine(framePath, $"{lineIndex}.raw");
            if (!File.Exists(linepath))
            {
                break;
            }

            long fileReadStart = System.Diagnostics.Stopwatch.GetTimestamp();
            byte[] fileData = File.ReadAllBytes(linepath);
            long fileReadMs = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - fileReadStart) / (System.Diagnostics.Stopwatch.Frequency / 1000.0));
            fileReadTimeTotal += fileReadMs;
            filesRead++;
            totalBytes += fileData.Length;
            
            if (fileData == null || fileData.Length == 0)
            {
                break;
            }

            anyLineData = true;
            int idx = 0;
            int entrySize = 16;
            int lineStartVertex = vertcount;

            while (idx + entrySize <= fileData.Length)
            {
                Vector3 vec = new Vector3();
                vec.x = System.BitConverter.ToSingle(fileData, idx);
                vec.y = System.BitConverter.ToSingle(fileData, idx + 4);
                vec.z = System.BitConverter.ToSingle(fileData, idx + 8);
                float magnitude = System.BitConverter.ToSingle(fileData, idx + 12);
                idx += entrySize;

                vertices.Add(vec);
                magnitudes.Add(0f);

                if (!newline)
                {
                    indices.Add(vertcount - 1);
                    indices.Add(vertcount);
                }
                else
                {
                    newline = false;
                }
                vertcount++;
            }

            int lineVertexCount = vertcount - lineStartVertex;
            if (lineVertexCount > 0)
            {
                for (int i = 0; i < lineVertexCount; i++)
                {
                    // Guard against divide-by-zero when the line has only one vertex
                    float t = lineVertexCount > 1 ? (float)i / (lineVertexCount - 1) : 0.5f;
                    float fabricatedMagnitude = (t <= 0.5f) ? (t * 2f) : (2f - (t * 2f));
                    magnitudes[lineStartVertex + i] = fabricatedMagnitude;
                }
            }

            newline = true;
        }

        if (!anyLineData)
        {
            return StreamlineFrameData.Empty();
        }
        
        long totalMs = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - totalStartTime) / (System.Diagnostics.Stopwatch.Frequency / 1000.0));
        long parseMs = totalMs - fileReadTimeTotal;
        
        Debug.Log($"[Streamline] Frame {frame}: {filesRead} files, {totalBytes / 1024}KB, FileRead={fileReadTimeTotal}ms, Parse={parseMs}ms, Total={totalMs}ms");

        return new StreamlineFrameData
        {
            vertices = vertices.ToArray(),
            magnitudes = magnitudes,
            indices = indices.ToArray(),
            hasData = true
        };
    }

    private void MarkFrameLoaded(int frame)
    {
        if (frameLoaded == null || frame < 0 || frame >= frameLoaded.Count)
        {
            return;
        }

        frameLoaded[frame] = true;

        if (maxFrameCache <= 0)
        {
            return;
        }

        loadedFrameQueue.Enqueue(frame);
        TrimFrameCache();
    }

    private void TrimFrameCache()
    {
        int safety = loadedFrameQueue.Count;
        while (loadedFrameQueue.Count > maxFrameCache && safety-- > 0)
        {
            int candidate = loadedFrameQueue.Dequeue();
            if (candidate == lastDisplayedFrame)
            {
                loadedFrameQueue.Enqueue(candidate);
                continue;
            }

            if (candidate >= 0 && candidate < meshes.Count && meshes[candidate] != null)
            {
                Object.DestroyImmediate(meshes[candidate]);
                meshes[candidate] = null;
            }

            if (candidate >= 0 && candidate < frameLoaded.Count)
            {
                frameLoaded[candidate] = false;
            }
        }
    }

    private StreamlineFrameData ParseConsolidatedStreamlineData(byte[] data)
    {
        try
        {
            if (data == null || data.Length < 4)
            {
                return StreamlineFrameData.Empty();
            }
            
            List<Vector3> vertices = new List<Vector3>();
            List<float> magnitudes = new List<float>();
            List<int> indices = new List<int>();
            int vertcount = 0;
            
            int offset = 0;
            
            // Read number of lines
            int numLines = System.BitConverter.ToInt32(data, offset);
            offset += 4;
            
            // Read each line
            for (int lineIdx = 0; lineIdx < numLines; lineIdx++)
            {
                if (offset + 4 > data.Length) break;
                
                // Read vertex count for this line
                int vertexCount = System.BitConverter.ToInt32(data, offset);
                offset += 4;
                
                int lineStartVertex = vertcount;
                int entrySize = 16; // 4 floats: x, y, z, magnitude
                
                // Read all vertices for this line
                for (int v = 0; v < vertexCount; v++)
                {
                    if (offset + entrySize > data.Length) break;
                    
                    Vector3 vec = new Vector3();
                    vec.x = System.BitConverter.ToSingle(data, offset);
                    vec.y = System.BitConverter.ToSingle(data, offset + 4);
                    vec.z = System.BitConverter.ToSingle(data, offset + 8);
                    float magnitude = System.BitConverter.ToSingle(data, offset + 12);
                    offset += entrySize;
                    
                    vertices.Add(vec);
                    magnitudes.Add(0f);
                    
                    // Create line segment indices (except for first vertex of line)
                    if (v > 0)
                    {
                        indices.Add(vertcount - 1);
                        indices.Add(vertcount);
                    }
                    
                    vertcount++;
                }
                
                // Apply fabricated magnitude gradient (same as original code)
                int lineVertexCount = vertcount - lineStartVertex;
                if (lineVertexCount > 0)
                {
                    for (int i = 0; i < lineVertexCount; i++)
                    {
                        // Guard against divide-by-zero when the line has only one vertex
                        float t = lineVertexCount > 1 ? (float)i / (lineVertexCount - 1) : 0.5f;
                        float fabricatedMagnitude = (t <= 0.5f) ? (t * 2f) : (2f - (t * 2f));
                        magnitudes[lineStartVertex + i] = fabricatedMagnitude;
                    }
                }
            }
            
            if (vertices.Count == 0)
            {
                return StreamlineFrameData.Empty();
            }
            
            return new StreamlineFrameData
            {
                vertices = vertices.ToArray(),
                magnitudes = magnitudes,
                indices = indices.ToArray(),
                hasData = true
            };
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Streamline] Error parsing consolidated format: {e.Message}");
            return StreamlineFrameData.Empty();
        }
    }

    private struct StreamlineFrameData
    {
        public Vector3[] vertices;
        public List<float> magnitudes;
        public int[] indices;
        public bool hasData;

        public static StreamlineFrameData Empty()
        {
            return new StreamlineFrameData
            {
                vertices = System.Array.Empty<Vector3>(),
                magnitudes = new List<float>(),
                indices = System.Array.Empty<int>(),
                hasData = false
            };
        }
    }

    private void loadFrame(string path)
    {
#if !UNITY_ANDROID || UNITY_EDITOR
        if (!Directory.Exists(path))
        {
            Debug.LogWarning($"[Streamline] Missing frame directory: {path}. Adding empty mesh.");
            meshes.Add(CreateEmptyMesh());
            return;
        }
#endif

        List<string> lineFiles = GetLineFilesForFrame(path);

        List<Vector3> vertices = new List<Vector3>();
        List<float> magnitudes = new List<float>();
        List<int> indices = new List<int>();
        bool newline = true;
        int vertcount = 0;
        bool anyLineData = false;

        foreach (string fileName in lineFiles)
        {
            int lineStartVertex = vertcount;
            string linepath = Path.Combine(path, fileName);

            if (!File.Exists(linepath))
            {
                continue;
            }

            byte[] fileData = File.ReadAllBytes(linepath);
            if (fileData == null || fileData.Length == 0)
            {
                continue;
            }

            anyLineData = true;
            int idx = 0;
            int entrySize = 16;
            while (idx + entrySize <= fileData.Length)
            {
                Vector3 vec = new Vector3();
                vec.x = System.BitConverter.ToSingle(fileData, idx);
                vec.y = System.BitConverter.ToSingle(fileData, idx + 4);
                vec.z = System.BitConverter.ToSingle(fileData, idx + 8);
                float magnitude = System.BitConverter.ToSingle(fileData, idx + 12);
                idx += entrySize;

                vertices.Add(vec);
                magnitudes.Add(0f);

                if (!newline)
                {
                    indices.Add(vertcount - 1);
                    indices.Add(vertcount);
                }
                else
                {
                    newline = false;
                }
                vertcount++;
            }

            int lineVertexCount = vertcount - lineStartVertex;
            if (lineVertexCount > 0)
            {
                for (int i = 0; i < lineVertexCount; i++)
                {
                    // Guard against divide-by-zero when the line has only one vertex
                    float t = lineVertexCount > 1 ? (float)i / (lineVertexCount - 1) : 0.5f;
                    float fabricatedMagnitude = (t <= 0.5f) ? (t * 2f) : (2f - (t * 2f));
                    magnitudes[lineStartVertex + i] = fabricatedMagnitude;
                }
            }

            newline = true;
        }

        if (!anyLineData)
        {
            meshes.Add(CreateEmptyMesh());
            return;
        }

        List<Color> colors = CalculateColors(magnitudes);

        Mesh m = new Mesh();
        m.vertices = vertices.ToArray();
        m.colors = colors.ToArray();
        m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetIndices(indices.ToArray(), MeshTopology.Lines, 0, true);
        meshes.Add(m);
    }

    private Mesh CreateEmptyMesh()
    {
        Mesh empty = new Mesh();
        empty.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        empty.SetIndices(System.Array.Empty<int>(), MeshTopology.Lines, 0, false);
        return empty;
    }

    private List<string> GetLineFilesForFrame(string framePath)
    {
        List<string> files = new List<string>();
        int numLines = 200;
        for (int i = 0; i < numLines; i++)
        {
            files.Add($"{i}.raw");
        }
        return files;
    }
}
