using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

/// <summary>
/// Procedurally generates a 3D axis-aligned grid mesh with optional tick marks and value labels.
/// Supports configurable step counts per axis, per-axis label offsets/rotations, billboard labels,
/// an inner-grid toggle, and optional dataset-driven divisions. Labels and inner grid can be shown
/// or hidden at runtime via a UI toggle or public methods.
/// </summary>
public class GridCreator : MonoBehaviour
{
    public float upperBound = 1.0f;
    public float lowerBound = -1.0f;
    public GameObject gridText = null;

    [Header("Label Data Range")]
    [Tooltip("Actual data-space minimum shown on tick labels. When both are 0 the world bounds are used directly.")]
    public float dataLowerBound = 0f;
    [Tooltip("Actual data-space maximum shown on tick labels. When both are 0 the world bounds are used directly.")]
    public float dataUpperBound = 0f;

    [Header("Grid Settings")]
    [Range(1, 200)] public int xSteps = 10;
    [Range(1, 200)] public int ySteps = 10;
    [Range(1, 200)] public int zSteps = 10;
    public bool showInnerGrid = true;
    public Color gridColor = Color.gray;
    // Deprecated: merged into `showValues` — labels shown when `showValues` is true
    public bool labelXAxis = true;
    public bool labelYAxis = true;
    public bool labelZAxis = true;
    // global offset removed; use per-axis offsets instead
    public int labelDecimals = 0;
    public float labelScale = 1.0f;
    public float labelOffsetValue = 0.0f;
    public int desiredTicks = 4;
    public bool showValues = true;

    [Header("Label Font Sizes")]
    [Tooltip("Font size for tick/value labels (TextMesh.fontSize)")]
    public int tickLabelFontSize = 24;
    [Tooltip("Font size for axis name labels (TextMesh.fontSize)")]
    public int axisLabelFontSize = 32;

    [Header("Axis Tick Marks")]
    [Tooltip("Length of small perpendicular tick marks on the main axes (world units)")]
    public float tickMarkSize = 0.02f;

    [Header("UI Toggle (optional)")]
    [Tooltip("Optional UI Toggle to control both grid and value visibility together (assign in Inspector)")]
    public Toggle uiToggleShowAll;

    // cached toggle callback so we can remove listener on destroy
    private UnityAction<bool> cbToggleShowAll;

    [Header("Label Per-Axis Settings")]
    public Vector3 labelLocalOffsetX = new Vector3(0.02f, 0.0f, 0.0f);
    public Vector3 labelLocalOffsetY = new Vector3(0.0f, 0.02f, 0.0f);
    public Vector3 labelLocalOffsetZ = new Vector3(0.0f, 0.0f, 0.02f);
    public Vector3 labelEulerX = Vector3.zero;
    public Vector3 labelEulerY = Vector3.zero;
    public Vector3 labelEulerZ = Vector3.zero;
    public bool labelBillboardX = true;
    public bool labelBillboardY = true;
    public bool labelBillboardZ = true;

    [Header("Dataset Driven Divisions")]
    public bool useDatasetDivisions = false;
    public Vector3Int datasetDivisions = new Vector3Int(10, 10, 10);


    private MeshFilter meshFilter = null;
    private MeshRenderer meshRenderer = null;
    // separate mesh for inner grid so axes can remain visible
    private MeshFilter meshFilterInner = null;
    private MeshRenderer meshRendererInner = null;

    
    // Start is called before the first frame update
    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        // prepare an inner grid child GameObject with its own MeshFilter/Renderer
        Transform innerT = this.transform.Find("GridInner");
        if (innerT == null)
        {
            GameObject innerGO = new GameObject("GridInner");
            innerGO.transform.SetParent(this.transform, false);
            innerT = innerGO.transform;
        }
        if (innerT != null)
        {
            meshFilterInner = innerT.GetComponent<MeshFilter>();
            if (meshFilterInner == null) meshFilterInner = innerT.gameObject.AddComponent<MeshFilter>();
            meshRendererInner = innerT.GetComponent<MeshRenderer>();
            if (meshRendererInner == null) meshRendererInner = innerT.gameObject.AddComponent<MeshRenderer>();
            if (meshRenderer != null && meshRendererInner != null) meshRendererInner.sharedMaterial = meshRenderer.sharedMaterial;
        }

        // initialize single UI toggle if assigned (controls inner-grid and labels)
        if (uiToggleShowAll != null)
        {
            uiToggleShowAll.isOn = showInnerGrid && showValues;
            cbToggleShowAll = (val) => {
                showInnerGrid = val;
                showValues = val;
                // rebuild mesh to add/remove inner grid and value labels when toggled
                createMesh();
            };
            uiToggleShowAll.onValueChanged.AddListener(cbToggleShowAll);
            // always keep renderer enabled so main axes remain visible
            if (meshRenderer != null) meshRenderer.enabled = true;
        }
    }

    // Compute "nice" tick positions between min and max targeting up to maxTicks intervals
    private List<float> ComputeTicks(float min, float max, int maxTicks)
    {
        List<float> ticks = new List<float>();
        if (max <= min)
        {
            ticks.Add(min);
            return ticks;
        }

        float range = max - min;
        int target = Mathf.Max(1, maxTicks);
        float rawStep = range / target;
        float niceStep = NiceNum(rawStep, true);

        // Find first and last tick within [min,max]
        float first = Mathf.Ceil(min / niceStep) * niceStep;
        float last = Mathf.Floor(max / niceStep) * niceStep;

        // If no ticks fit (e.g., tiny range), fall back to endpoints as single ticks
        if (first > last)
        {
            ticks.Add(min);
            ticks.Add(max);
            return ticks;
        }

        // Build ticks from first to last
        for (float v = first; v <= last + 1e-6f; v += niceStep)
        {
            // Clamp rounding errors
            float clamped = Mathf.Clamp(v, min, max);
            ticks.Add((float) System.Math.Round(clamped, 6));
        }

        // Do not force-add endpoints; keep ticks that lie on nice increments within bounds

        return ticks;
    }

    // Nicenum algorithm to find a "nice" number near range
    private float NiceNum(float range, bool round)
    {
        double exponent = System.Math.Floor(System.Math.Log10(range));
        double fraction = range / System.Math.Pow(10, exponent);
        double niceFraction;
        if (round)
        {
            if (fraction < 1.5) niceFraction = 1;
            else if (fraction < 3) niceFraction = 2;
            else if (fraction < 7) niceFraction = 5;
            else niceFraction = 10;
        }
        else
        {
            if (fraction <= 1) niceFraction = 1;
            else if (fraction <= 2) niceFraction = 2;
            else if (fraction <= 5) niceFraction = 5;
            else niceFraction = 10;
        }
        return (float)(niceFraction * System.Math.Pow(10, exponent));
    }

    // Determine decimal places required to represent the given step exactly
    private int GetDecimalsForStep(float step)
    {
        if (step <= 0f) return 0;
        double tmp = step;
        int decimals = 0;
        while (decimals < 10 && System.Math.Abs(tmp - System.Math.Round(tmp)) > 1e-9)
        {
            tmp *= 10.0;
            decimals++;
        }
        return decimals;
    }

    // Helper: compute ticks and step for a given range and target count
    private void GetTicksAndStep(float min, float max, int target, out List<float> ticks, out float step)
    {
        ticks = ComputeTicks(min, max, target);
        float len = max - min;
        step = (ticks != null && ticks.Count > 1) ? (ticks[1] - ticks[0]) : (len / Mathf.Max(1, target));
    }

    public void createMesh()
    {
        Debug.Log("[GridCreator] Creating grid mesh with bounds: " + lowerBound + " to " + upperBound);
        List<Vector3> verts = new List<Vector3>();
        List<int> indexes = new List<int>();
        List<Color> colors = new List<Color>();

        float xMin = lowerBound;
        float yMin = lowerBound;
        float zMin = lowerBound;
        float xMax = upperBound;
        float yMax = upperBound;
        float zMax = upperBound;

        int sx = xSteps;
        int sy = ySteps;
        int sz = zSteps;
        if (useDatasetDivisions)
        {
            sx = Mathf.Max(1, datasetDivisions.x);
            sy = Mathf.Max(1, datasetDivisions.y);
            sz = Mathf.Max(1, datasetDivisions.z);
        }

        // Compute "nice" tick positions based on bounds
        int targetX = useDatasetDivisions ? sx : Mathf.Max(1, desiredTicks);
        int targetY = useDatasetDivisions ? sy : Mathf.Max(1, desiredTicks);
        int targetZ = useDatasetDivisions ? sz : Mathf.Max(1, desiredTicks);
        List<float> ticksX; float stepX;
        List<float> ticksY; float stepY;
        List<float> ticksZ; float stepZ;
        GetTicksAndStep(xMin, xMax, targetX, out ticksX, out stepX);
        GetTicksAndStep(yMin, yMax, targetY, out ticksY, out stepY);
        GetTicksAndStep(zMin, zMax, targetZ, out ticksZ, out stepZ);

        // Build separate meshes: axes (always present) and inner grid (toggleable)
        Vector3 origin = new Vector3(xMin, yMin, zMin);

        List<Vector3> vertsAxes = new List<Vector3>();
        List<int> indexesAxes = new List<int>();
        List<Color> colorsAxes = new List<Color>();

        List<Vector3> vertsInner = new List<Vector3>();
        List<int> indexesInner = new List<int>();
        List<Color> colorsInner = new List<Color>();

        // Axes
        vertsAxes.Add(origin);
        vertsAxes.Add(new Vector3(xMax, yMin, zMin));
        vertsAxes.Add(origin);
        vertsAxes.Add(new Vector3(xMin, yMax, zMin));
        vertsAxes.Add(origin);
        vertsAxes.Add(new Vector3(xMin, yMin, zMax));
        for (int i = 0; i < 6; ++i) colorsAxes.Add(Color.white);
        indexesAxes.Add(0); indexesAxes.Add(1);
        indexesAxes.Add(2); indexesAxes.Add(3);
        indexesAxes.Add(4); indexesAxes.Add(5);

        // Add small perpendicular tick marks at each computed tick position.
        // Tick marks are now tied to the inner-grid toggle so they appear/disappear with the grid.
        if (tickMarkSize > 0f && showInnerGrid)
        {
            // X-axis ticks: small segments in +Y direction from axis
            foreach (float xVal in ticksX)
            {
                vertsAxes.Add(new Vector3(xVal, yMin, zMin));
                vertsAxes.Add(new Vector3(xVal, yMin + tickMarkSize, zMin));
                colorsAxes.Add(Color.white);
                colorsAxes.Add(Color.white);
                int n = vertsAxes.Count;
                indexesAxes.Add(n - 2); indexesAxes.Add(n - 1);
            }

            // Y-axis ticks: small segments in +X direction from axis
            foreach (float yVal in ticksY)
            {
                vertsAxes.Add(new Vector3(xMin, yVal, zMin));
                vertsAxes.Add(new Vector3(xMin + tickMarkSize, yVal, zMin));
                colorsAxes.Add(Color.white);
                colorsAxes.Add(Color.white);
                int n = vertsAxes.Count;
                indexesAxes.Add(n - 2); indexesAxes.Add(n - 1);
            }

            // Z-axis ticks: small segments in +X direction from axis
            // Rotate Z-axis tick marks by 45 degrees in the XY plane
            float angleRad = 90.0f * Mathf.Deg2Rad;
            float dx = tickMarkSize * Mathf.Cos(angleRad);
            float dy = tickMarkSize * Mathf.Sin(angleRad);
            foreach (float zVal in ticksZ)
            {
                vertsAxes.Add(new Vector3(xMin, yMin, zVal));
                vertsAxes.Add(new Vector3(xMin + dx, yMin + dy, zVal));
                colorsAxes.Add(Color.white);
                colorsAxes.Add(Color.white);
                int n = vertsAxes.Count;
                indexesAxes.Add(n - 2); indexesAxes.Add(n - 1);
            }
        }

        // Inner grid (only populate when requested)
        if (showInnerGrid)
        {
            // Lines parallel to X for each (y,z) using computed ticks
            foreach (float zPos in ticksZ)
            {
                foreach (float yPos in ticksY)
                {
                    vertsInner.Add(new Vector3(xMin, yPos, zPos));
                    vertsInner.Add(new Vector3(xMax, yPos, zPos));
                    colorsInner.Add(gridColor);
                    colorsInner.Add(gridColor);
                    int n = vertsInner.Count;
                    indexesInner.Add(n - 2);
                    indexesInner.Add(n - 1);
                }
            }

            // Lines parallel to Y for each (x,z)
            foreach (float zPos in ticksZ)
            {
                foreach (float xPos in ticksX)
                {
                    vertsInner.Add(new Vector3(xPos, yMin, zPos));
                    vertsInner.Add(new Vector3(xPos, yMax, zPos));
                    colorsInner.Add(gridColor);
                    colorsInner.Add(gridColor);
                    int n = vertsInner.Count;
                    indexesInner.Add(n - 2);
                    indexesInner.Add(n - 1);
                }
            }

            // Lines parallel to Z for each (x,y)
            foreach (float yPos in ticksY)
            {
                foreach (float xPos in ticksX)
                {
                    vertsInner.Add(new Vector3(xPos, yPos, zMin));
                    vertsInner.Add(new Vector3(xPos, yPos, zMax));
                    colorsInner.Add(gridColor);
                    colorsInner.Add(gridColor);
                    int n = vertsInner.Count;
                    indexesInner.Add(n - 2);
                    indexesInner.Add(n - 1);
                }
            }
        }

        // Build axes mesh
        Mesh mAxes = new Mesh();
        mAxes.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mAxes.vertices = vertsAxes.ToArray();
        mAxes.colors = colorsAxes.ToArray();
        mAxes.SetIndices(indexesAxes.ToArray(), MeshTopology.Lines, 0);
        mAxes.RecalculateBounds();
        if (meshFilter != null) meshFilter.mesh = mAxes;

        // Build inner grid mesh
        Mesh mInner = new Mesh();
        mInner.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mInner.vertices = vertsInner.ToArray();
        mInner.colors = colorsInner.ToArray();
        mInner.SetIndices(indexesInner.ToArray(), MeshTopology.Lines, 0);
        mInner.RecalculateBounds();
        if (meshFilterInner != null) meshFilterInner.mesh = mInner;

        // Keep axes renderer enabled so main axes lines are always visible; inner renderer enabled only when inner grid present
        if (meshRenderer != null) meshRenderer.enabled = true;
        if (meshRendererInner != null) meshRendererInner.enabled = showInnerGrid;
        if (gridText != null)
        {
            // Remove existing value labels (non-axis)
            RemoveNonAxisValueLabels();

            // Create tick/value labels if requested
            if (showValues)
            {
                // When dataLowerBound != dataUpperBound use them for label values;
                // otherwise fall back to raw world-space tick values.
                bool hasDataRange = !Mathf.Approximately(dataLowerBound, dataUpperBound);
                float worldMin = lowerBound;
                float worldMax = upperBound;

                // X axis labels (values from lowerBound..upperBound)
                if (labelXAxis)
                {
                    float stepUsedX = ticksX.Count > 1 ? ticksX[1] - ticksX[0] : stepX;
                    int neededX = GetDecimalsForStep(stepUsedX);
                    int decimalsX = Mathf.Clamp(Mathf.Max(labelDecimals, neededX), 0, 1);
                    foreach (float xVal in ticksX)
                    {
                        float raw = hasDataRange
                            ? Mathf.LerpUnclamped(dataLowerBound, dataUpperBound, (xVal - worldMin) / (worldMax - worldMin))
                            : xVal;
                        float val = raw * labelScale + labelOffsetValue;
                        string text = decimalsX <= 0 ? Mathf.RoundToInt(val).ToString() : val.ToString("F" + decimalsX);
                        Vector3 pos = new Vector3(xVal, yMin, zMin) + labelLocalOffsetX;
                        GameObject g = createAndWriteText(pos, text, labelEulerX);
                        if (g != null && labelBillboardX) g.AddComponent<LabelBillboard>();
                    }
                }

                // Y axis labels (values from lowerBound..upperBound)
                if (labelYAxis)
                {
                    float stepUsedY = ticksY.Count > 1 ? ticksY[1] - ticksY[0] : stepY;
                    int neededY = GetDecimalsForStep(stepUsedY);
                    int decimalsY = Mathf.Clamp(Mathf.Max(labelDecimals, neededY), 0, 1);
                    foreach (float yVal in ticksY)
                    {
                        float raw = hasDataRange
                            ? Mathf.LerpUnclamped(dataLowerBound, dataUpperBound, (yVal - worldMin) / (worldMax - worldMin))
                            : yVal;
                        float val = raw * labelScale + labelOffsetValue;
                        string text = decimalsY <= 0 ? Mathf.RoundToInt(val).ToString() : val.ToString("F" + decimalsY);
                        Vector3 pos = new Vector3(xMin, yVal, zMin) + labelLocalOffsetY;
                        GameObject g = createAndWriteText(pos, text, labelEulerY);
                        if (g != null && labelBillboardY) g.AddComponent<LabelBillboard>();
                    }
                }

                // Z axis labels (values from lowerBound..upperBound)
                if (labelZAxis)
                {
                    float stepUsedZ = ticksZ.Count > 1 ? ticksZ[1] - ticksZ[0] : stepZ;
                    int neededZ = GetDecimalsForStep(stepUsedZ);
                    int decimalsZ = Mathf.Clamp(Mathf.Max(labelDecimals, neededZ), 0, 1);
                    foreach (float zVal in ticksZ)
                    {
                        float raw = hasDataRange
                            ? Mathf.LerpUnclamped(dataLowerBound, dataUpperBound, (zVal - worldMin) / (worldMax - worldMin))
                            : zVal;
                        float val = raw * labelScale + labelOffsetValue;
                        string text = decimalsZ <= 0 ? Mathf.RoundToInt(val).ToString() : val.ToString("F" + decimalsZ);
                        Vector3 pos = new Vector3(xMin, yMin, zVal) + labelLocalOffsetZ;
                        GameObject g = createAndWriteText(pos, text, labelEulerZ);
                        if (g != null && labelBillboardZ) g.AddComponent<LabelBillboard>();
                    }
                }
            }

            // Ensure axis labels exist (create if missing)
            bool hasX = false, hasY = false, hasZ = false;
            foreach (Transform child in this.transform)
            {
                if (child.gameObject.name == "AxisLabel_X") hasX = true;
                if (child.gameObject.name == "AxisLabel_Y") hasY = true;
                if (child.gameObject.name == "AxisLabel_Z") hasZ = true;
            }
            if (!hasX)
            {
                GameObject ax = createAndWriteText(new Vector3(xMax + 0.1f * xMax, yMin, zMin), "x", labelEulerX, axisLabelFontSize);
                if (ax != null) ax.name = "AxisLabel_X";
            }
            if (!hasY)
            {
                GameObject ay = createAndWriteText(new Vector3(xMin, yMax + 0.1f * yMax, zMin), "y", labelEulerY, axisLabelFontSize);
                if (ay != null) ay.name = "AxisLabel_Y";
            }
            if (!hasZ)
            {
                GameObject az = createAndWriteText(new Vector3(xMin, yMin, zMax + 0.1f * zMax), "z", labelEulerZ, axisLabelFontSize);
                if (az != null) az.name = "AxisLabel_Z";
            }
        }

    }

    public GameObject createAndWriteText(Vector3 pos, string text)
    {
        return createAndWriteText(pos, text, Vector3.zero, tickLabelFontSize);
    }

    // Overload allowing to set local Euler orientation for the label; returns instantiated GameObject
    public GameObject createAndWriteText(Vector3 pos, string text, Vector3 localEuler)
    {
        return createAndWriteText(pos, text, localEuler, tickLabelFontSize);
    }

    // Full overload allowing to set font size for TextMesh
    public GameObject createAndWriteText(Vector3 pos, string text, Vector3 localEuler, int fontSize)
    {
        if (gridText == null) return null;
        GameObject obj = Instantiate(gridText, this.transform);
        TextMesh textMesh = obj.GetComponent(typeof(TextMesh)) as TextMesh;
        if (textMesh != null)
        {
            textMesh.text = text;
            textMesh.fontSize = fontSize;
        }
        obj.transform.localPosition = pos;
        // If the caller provided a non-zero localEuler, apply it.
        // Otherwise leave the instantiated prefab's rotation as set in the editor.
        if (localEuler != Vector3.zero) obj.transform.localEulerAngles = localEuler;
        return obj;
    }

    public void createAxisText()
    {
        Debug.Log("[GridCreator] Creating axis text labels");
        createAndWriteText(new Vector3(upperBound+ 0.1f*upperBound, lowerBound, lowerBound), "x", Vector3.zero, axisLabelFontSize);
        createAndWriteText(new Vector3(lowerBound, upperBound+0.1f * upperBound, lowerBound), "y", Vector3.zero, axisLabelFontSize);
        createAndWriteText(new Vector3(lowerBound, lowerBound, upperBound+0.1f * upperBound), "z", Vector3.zero, axisLabelFontSize);
    }

    // Remove any non-axis TextMesh children (keeps AxisLabel_* GameObjects)
    private void RemoveNonAxisValueLabels()
    {
        List<Transform> toRemove = new List<Transform>();
        foreach (Transform child in this.transform)
        {
            TextMesh tm = child.GetComponent<TextMesh>();
            if (tm == null) continue;
            string n = child.gameObject.name;
            if (n.StartsWith("AxisLabel_")) continue;
            toRemove.Add(child);
        }
        foreach (Transform t in toRemove)
        {
            if (Application.isPlaying) Destroy(t.gameObject); else DestroyImmediate(t.gameObject);
        }
    }

    // Erase and draw helpers for inner grid and value labels
    public void EraseValues()
    {
        RemoveNonAxisValueLabels();
        showValues = false;
    }

    public void DrawValues()
    {
        showValues = true;
        // Rebuild mesh/labels to create value labels (createMesh handles label creation)
        createMesh();
    }

    public void EraseInnerGrid()
    {
        showInnerGrid = false;
        createMesh();
    }

    public void DrawInnerGrid()
    {
        showInnerGrid = true;
        createMesh();
    }

    private void OnDestroy()
    {
        if (uiToggleShowAll != null && cbToggleShowAll != null) uiToggleShowAll.onValueChanged.RemoveListener(cbToggleShowAll);
    }

    
}
