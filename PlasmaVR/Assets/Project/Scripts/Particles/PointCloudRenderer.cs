using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Renders particle point cloud data using Unity's Visual Effect Graph.
/// Loads particles from .raw files and applies clipping, thresholding, and scaling effects.
/// </summary>
public class PointCloudRenderer : Simulation
{
    public string basePath = "";
    public int maxFrame = 0;

    VisualEffect vfx;
    uint resolution = 512;
    uint particleCount = 0;

    public float particleSize = 0.1f;

    [Range(0.0f, 1.0f)]
    public float threshold = 0.0f;

    public bool applyThreshold = false;

    [Header("Local Cache Loading")]
    [SerializeField] private bool enableLazyLocalLoading = true;
    [SerializeField] private int preloadFrames = 2;
    [SerializeField] private int maxFrameCache = 8;

    [Header("Capacity")]
    [SerializeField] private uint particleCapacity = 300000;

    [Header("Debug")]
    [SerializeField] private bool disableReinitForDebug = false; // Keep Reinit enabled to test post-Reinit clipping

    List<Texture2D> positionTextures;
    // Stores the particle count for each frame — avoids the shared particleCount field
    // being overwritten by a background load while a different frame is being displayed.
    private List<uint> perFrameParticleCounts;
    private List<bool> frameLoaded;
    private readonly HashSet<int> loadingFrames = new HashSet<int>();
    private readonly Queue<int> loadedFrameQueue = new Queue<int>();
    private int lastDisplayedFrame = -1;
    private Texture2D lastDisplayedTexture = null;
    private bool loggedParticleCount = false;
    private bool loggedCapacityWarning = false;
    private bool loggedPositionBounds = false;
    private bool loggedVfxTransform = false;

    private void LogPositionBoundsOnce(Vector4[] positions, int frame)
    {
        if (loggedPositionBounds || positions == null || positions.Length == 0)
        {
            return;
        }

        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < positions.Length; i++)
        {
            Vector4 p = positions[i];
            if (p.x < min.x) min.x = p.x;
            if (p.y < min.y) min.y = p.y;
            if (p.z < min.z) min.z = p.z;
            if (p.x > max.x) max.x = p.x;
            if (p.y > max.y) max.y = p.y;
            if (p.z > max.z) max.z = p.z;
        }

        loggedPositionBounds = true;
    }

    private void LogVfxTransformOnce()
    {
        if (loggedVfxTransform || vfx == null)
        {
            return;
        }

        Transform t = vfx.transform;
        loggedVfxTransform = true;
    }


    public override bool startSim(int numFrames, Vector3 dims, string simPath)
    {
        try
        {
            positionTextures = new List<Texture2D>();

            maxFrame = numFrames;
            frameLoaded = new List<bool>(maxFrame);
            for (int i = 0; i < maxFrame; i++)
            {
                frameLoaded.Add(false);
            }
            basePath = System.IO.Path.Combine(simPath, "Particles");
            
            Debug.Log($"[Particles] startSim called - basePath: {basePath}");

            loadSimulation();
            vfx = GetComponent<VisualEffect>();
        }
        catch
        {
            return false;
        }
        return true;
    }

    protected override void loadSimulation()
    {
        if (useLazyLocalLoading && enableLazyLocalLoading)
        {
            InitializeFramePlaceholders();
            StartCoroutine(PreloadFramesCoroutine());
            return;
        }

        // Local file loading mode only
        Debug.Log($"[Particles] Loading from cache: {basePath}");
        
#if !UNITY_ANDROID || UNITY_EDITOR
        if (!System.IO.Directory.Exists(basePath))
        {
            Debug.LogWarning($"[Particles] Base path missing: {basePath}. Creating empty particle frames.");
            for (int i = 0; i < maxFrame; i++)
            {
                AppendParticles(System.Array.Empty<Vector4>());
            }
            return;
        }
#endif

        for (int i = 0; i < maxFrame; i++)
        {
            string filePath = System.IO.Path.Combine(basePath, i + ".raw");
            importAppendPartFile(filePath);
        }
    }

    public override bool IsFrameReady(int frame)
    {
        if (!useLazyLocalLoading || !enableLazyLocalLoading)
        {
            return true;
        }

        return frameLoaded != null && frame >= 0 && frame < frameLoaded.Count && frameLoaded[frame];
    }

    public override void displayFrame(int frame)
    {
        // Safety check: if vfx is not initialized, skip
        if (vfx == null || positionTextures == null || positionTextures.Count == 0)
        {
            return;
        }

        LogVfxTransformOnce();

        if (frame >= positionTextures.Count)
        {
            Debug.LogError($"[Particles] Frame {frame} out of range — only {positionTextures.Count} textures initialized");
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
        
        uint particleCountCapped = particleCount > particleCapacity ? particleCapacity : particleCount;
        if (particleCountCapped != particleCount && !loggedCapacityWarning)
        {
            Debug.LogWarning($"[Particles] ParticleCount ({particleCount}) exceeds capacity ({particleCapacity}). Clamping to capacity.");
            loggedCapacityWarning = true;
        }

        // Use the per-frame stored count when available (avoids async race condition)
        if (perFrameParticleCounts != null && frame < perFrameParticleCounts.Count)
        {
            uint frameCount = perFrameParticleCounts[frame];
            particleCountCapped = frameCount > particleCapacity ? particleCapacity : frameCount;
        }

        bool shouldReinit = !disableReinitForDebug &&
                            (frame != lastDisplayedFrame || positionTextures[frame] != lastDisplayedTexture);
        
        if (shouldReinit)
        {
            vfx.Reinit();
        }

        // Reapply clipping AFTER Reinit so VFX properties are not reset.
        if (shouldReinit)
        {
            var clippingBox = Object.FindFirstObjectByType<ClippingBox>();
            if (clippingBox != null)
            {
                // Removed: clippingBox.ReapplyClippingToVfx();
            }
        }
        
        vfx.SetUInt(Shader.PropertyToID("ParticleCount"), particleCountCapped);
        vfx.SetTexture(Shader.PropertyToID("TexPosScale"), positionTextures[frame]);
        vfx.SetUInt(Shader.PropertyToID("Resolution"), resolution);
        vfx.SetFloat(Shader.PropertyToID("ParticleSize"), particleSize);
        vfx.SetFloat(Shader.PropertyToID("Threshold"), threshold);
        vfx.SetBool(Shader.PropertyToID("ApplyThreshold"), applyThreshold);

        lastDisplayedFrame = frame;
        lastDisplayedTexture = positionTextures[frame];
    }


    /// <summary>
    /// Updates the texture for a specific frame with new particle data.
    /// </summary>
    private void UpdateFrameTexture(int frame, Vector4[] positions)
    {
        if (frame >= positionTextures.Count)
        {
            Debug.LogError($"[Particles] Cannot update frame {frame} - out of range");
            return;
        }

        LogPositionBoundsOnce(positions, frame);

        // Store the per-frame particle count so displayFrame uses the correct value
        // regardless of which frame background tasks most recently finished loading.
        if (perFrameParticleCounts == null)
            perFrameParticleCounts = new List<uint>(positionTextures.Count);
        while (perFrameParticleCounts.Count <= frame)
            perFrameParticleCounts.Add(0);
        perFrameParticleCounts[frame] = (uint)positions.Length;

        int width = positions.Length > 0 ? (positions.Length > (int)resolution ? (int)resolution : positions.Length) : 1;
        int height = positions.Length > 0 ? Mathf.Clamp(positions.Length / (int)resolution, 1, (int)resolution) : 1;

        Texture2D texPosScale = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
        int texWidth = texPosScale.width;
        int texHeight = texPosScale.height;

        if (positions.Length > 0)
        {
            for (int y = 0; y < texHeight; y++)
            {
                for (int x = 0; x < texWidth; x++)
                {
                    int index = x + y * texWidth;
                    if (index < positions.Length)
                    {
                        var data = new Color(positions[index].x, positions[index].y, positions[index].z, positions[index].w);
                        texPosScale.SetPixel(x, y, data);
                    }
                }
            }
        }
        else
        {
            texPosScale.SetPixel(0, 0, Color.clear);
        }

        texPosScale.Apply();

        // Replace the existing texture
        if (positionTextures[frame] != null)
        {
            Object.Destroy(positionTextures[frame]);
        }
        positionTextures[frame] = texPosScale;

        MarkFrameLoaded(frame);
    }

    public void AppendParticles(Vector4[] positions)
    {
        LogPositionBoundsOnce(positions, positionTextures != null ? positionTextures.Count : -1);
        // Handle empty positions array by creating a minimal 1x1 texture
        int width = positions.Length > 0 ? (positions.Length > (int)resolution ? (int)resolution : positions.Length) : 1;
        int height = positions.Length > 0 ? Mathf.Clamp(positions.Length / (int)resolution, 1, (int)resolution) : 1;
        
        Texture2D texPosScale = new Texture2D(width, height, TextureFormat.RGBAFloat, false);

        int texWidth = texPosScale.width;
        int texHeight = texPosScale.height;

        if (positions.Length > 0)
        {
            for (int y = 0; y < texHeight; y++)
            {
                for (int x = 0; x < texWidth; x++)
                {
                    int index = x + y * texWidth;
                    if (index < positions.Length)
                    {
                        var data = new Color(positions[index].x, positions[index].y, positions[index].z, positions[index].w);
                        texPosScale.SetPixel(x, y, data);
                    }
                }
            }
        }
        else
        {
            // Fill empty texture with transparent black
            texPosScale.SetPixel(0, 0, Color.clear);
        }

        texPosScale.Apply();
        positionTextures.Add(texPosScale);
        particleCount = (uint)positions.Length;
        toUpdate = true;
    }

    private void InitializeFramePlaceholders()
    {
        positionTextures = new List<Texture2D>(maxFrame);
        frameLoaded = new List<bool>(maxFrame);
        perFrameParticleCounts = new List<uint>(maxFrame);
        loadingFrames.Clear();
        loadedFrameQueue.Clear();

        for (int i = 0; i < maxFrame; i++)
        {
            positionTextures.Add(null);
            frameLoaded.Add(false);
            perFrameParticleCounts.Add(0);
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
            Vector4[] positions = await Task.Run(() => ReadPositionsFromFile(frame));

            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                UpdateFrameTexture(frame, positions);
                // particleCount is kept in sync here for non-lazy-loading callers;
                // displayFrame uses perFrameParticleCounts for the accurate per-frame value.
                particleCount = (uint)positions.Length;
                loadingFrames.Remove(frame);
            });
        }
        catch (System.Exception e)
        {
            loadingFrames.Remove(frame);
            Debug.LogError($"[Particles] Error loading local frame {frame}: {e.Message}");
        }
    }

    private Vector4[] ReadPositionsFromFile(int frame)
    {
        string filePath = System.IO.Path.Combine(basePath, frame + ".raw");
        if (!System.IO.File.Exists(filePath))
        {
            return System.Array.Empty<Vector4>();
        }

        byte[] fileData = System.IO.File.ReadAllBytes(filePath);
        if (fileData == null || fileData.Length == 0)
        {
            return System.Array.Empty<Vector4>();
        }

        int count = fileData.Length / 16;
        Vector4[] positions = new Vector4[count];

        for (int i = 0; i < count; i++)
        {
            int idx = i * 16;
            float x = System.BitConverter.ToSingle(fileData, idx);
            float y = System.BitConverter.ToSingle(fileData, idx + 4);
            float z = System.BitConverter.ToSingle(fileData, idx + 8);
            float w = System.BitConverter.ToSingle(fileData, idx + 12);
            positions[i] = new Vector4(x, y, z, w);
        }

        return positions;
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

            if (candidate >= 0 && candidate < positionTextures.Count && positionTextures[candidate] != null)
            {
                Object.Destroy(positionTextures[candidate]);
                positionTextures[candidate] = null;
            }

            if (candidate >= 0 && candidate < frameLoaded.Count)
            {
                frameLoaded[candidate] = false;
            }
        }
    }

    private void importAppendPartFile(string path)
    {
        List<Vector4> positions = new List<Vector4>();
        byte[] fileData = null;
        
        // Try file system first for local/cached files
        if (System.IO.File.Exists(path))
        {
            fileData = System.IO.File.ReadAllBytes(path);
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        else
        {
            // Android: use UnityWebRequest for streaming assets
            var www = UnityEngine.Networking.UnityWebRequest.Get(path);
            www.SendWebRequest();
            while (!www.isDone) { }
            if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                fileData = www.downloadHandler.data;
            }
            else
            {
                Debug.LogError($"[Particles] Failed to load particle file: {path} — {www.error}");
                return;
            }
        }
#else
        else
        {
            Debug.LogWarning($"[Particles] Particle file not found: {path}. Using empty frame.");
            AppendParticles(System.Array.Empty<Vector4>());
            return;
        }
#endif
        if (fileData == null || fileData.Length == 0)
        {
            // Still create empty texture for this frame
            AppendParticles(new Vector4[0]);
            return;
        }

        int count = fileData.Length / 16;
        
        for (int i = 0; i < count; i++)
        {
            int idx = i * 16;
            float x = System.BitConverter.ToSingle(fileData, idx);
            float y = System.BitConverter.ToSingle(fileData, idx + 4);
            float z = System.BitConverter.ToSingle(fileData, idx + 8);
            float w = System.BitConverter.ToSingle(fileData, idx + 12);
            positions.Add(new Vector4(x, y, z, w));
        }
        AppendParticles(positions.ToArray());
    }

    public void swapThreshold(float number)
    {
        threshold = number;
    }

    public void toggleThreshold(bool value)
    {
        applyThreshold = value;
    }
}

