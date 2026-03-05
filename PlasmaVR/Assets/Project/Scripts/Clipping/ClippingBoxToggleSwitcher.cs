using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// UI manager that switches clipping box assignments between X, Y, and Z axes.
/// Uses four buttons to control which axis is active and shows/hides corresponding slider controls.
/// Provides visual feedback via button color to indicate the currently active axis.
/// </summary>
public class ClippingBoxToggleSwitcher : MonoBehaviour
{
    public enum Axis { X = 0, Y = 1, Z = 2 }

    [Tooltip("The ClippingBox component to control. If empty, will try to find one on the same GameObject.")]
    public ClippingBox clippingBoxTarget;

    [Header("Axis box assignments (box1 / box2 per axis)")]
    public Transform box1_X;
    public Transform box2_X;
    public Transform box1_Y;
    public Transform box2_Y;
    public Transform box1_Z;
    public Transform box2_Z;

    [Header("UI Buttons (single active state)")]
    public Button buttonX;
    public Button buttonY;
    public Button buttonZ;
    public Button buttonNone; // disables clipping

    [Header("Button Visuals")]
    [Tooltip("Color used for the X axis button when active.")]
    public Color activeColorX = new Color(0.2f, 0.6f, 1f, 1f);
    [Tooltip("Color used for the Y axis button when active.")]
    public Color activeColorY = new Color(0.2f, 0.8f, 0.2f, 1f);
    [Tooltip("Color used for the Z axis button when active.")]
    public Color activeColorZ = new Color(1f, 0.6f, 0.2f, 1f);
    [Tooltip("Color used for the None button when active (disables clipping).")]
    public Color activeColorNone = new Color(0.6f, 0.6f, 0.6f, 1f);

    [Tooltip("Hover color for the X axis button.")]
    public Color hoverColorX = new Color(0.3f, 0.7f, 1f, 1f);
    [Tooltip("Hover color for the Y axis button.")]
    public Color hoverColorY = new Color(0.3f, 0.9f, 0.3f, 1f);
    [Tooltip("Hover color for the Z axis button.")]
    public Color hoverColorZ = new Color(1f, 0.7f, 0.3f, 1f);
    [Tooltip("Hover color for the None button.")]
    public Color hoverColorNone = new Color(0.7f, 0.7f, 0.7f, 1f);

    [Tooltip("Color used for inactive buttons.")]
    public Color inactiveButtonColor = new Color(1f, 1f, 1f, 1f);

    [Tooltip("When true, set the Button's target Graphic (usually Image) color. Otherwise, modify the Button ColorBlock's normalColor.")]
    public bool useTargetGraphicColor = true;

    [Header("Slider group GameObjects (parents) to enable per axis")]
    public GameObject slidersGroupX;
    public GameObject slidersGroupY;
    public GameObject slidersGroupZ;

    [Header("Startup")]
    [Tooltip("If enabled, the switcher will force the selected axis to be active at Start (overrides toggle initial states).")]
    public bool useStartAxis = false;
    [Tooltip("The axis to activate on Start when `useStartAxis` is enabled.")]
    public Axis startAxis = Axis.X;

    // cached originals so we can restore if needed
    private Transform originalBox1;
    private Transform originalBox2;

    private UnityEngine.Events.UnityAction cbX;
    private UnityEngine.Events.UnityAction cbY;
    private UnityEngine.Events.UnityAction cbZ;
    private UnityEngine.Events.UnityAction cbNone;
    // currently active axis (null when none)
    private Axis? activeAxis = null;

    // maps for per-button active/hover colors
    private System.Collections.Generic.Dictionary<Button, Color> activeColorMap;
    private System.Collections.Generic.Dictionary<Button, Color> hoverColorMap;
    private System.Collections.Generic.List<EventTrigger> createdEventTriggers = new System.Collections.Generic.List<EventTrigger>();

    private void Awake()
    {
        if (clippingBoxTarget == null)
            clippingBoxTarget = GetComponent<ClippingBox>();

        // Hide slider groups early to avoid them flashing in the Editor/Play transition.
        if (slidersGroupX != null) slidersGroupX.SetActive(false);
        if (slidersGroupY != null) slidersGroupY.SetActive(false);
        if (slidersGroupZ != null) slidersGroupZ.SetActive(false);
    }

    private void Start()
    {
        if (clippingBoxTarget != null)
        {
            originalBox1 = clippingBoxTarget.box1;
            originalBox2 = clippingBoxTarget.box2;
        }

        cbX = () => SetActiveAxis(Axis.X);
        cbY = () => SetActiveAxis(Axis.Y);
        cbZ = () => SetActiveAxis(Axis.Z);
        cbNone = () => ApplyAxisNone();

        if (buttonX != null) buttonX.onClick.AddListener(cbX);
        if (buttonY != null) buttonY.onClick.AddListener(cbY);
        if (buttonZ != null) buttonZ.onClick.AddListener(cbZ);
        if (buttonNone != null) buttonNone.onClick.AddListener(cbNone);

        // build color maps
        activeColorMap = new System.Collections.Generic.Dictionary<Button, Color>();
        hoverColorMap = new System.Collections.Generic.Dictionary<Button, Color>();
        if (buttonX != null) { activeColorMap[buttonX] = activeColorX; hoverColorMap[buttonX] = hoverColorX; }
        if (buttonY != null) { activeColorMap[buttonY] = activeColorY; hoverColorMap[buttonY] = hoverColorY; }
        if (buttonZ != null) { activeColorMap[buttonZ] = activeColorZ; hoverColorMap[buttonZ] = hoverColorZ; }
        if (buttonNone != null) { activeColorMap[buttonNone] = activeColorNone; hoverColorMap[buttonNone] = hoverColorNone; }

        // register pointer enter/exit handlers to implement hover color behaviour
        AddPointerEvents(buttonX);
        AddPointerEvents(buttonY);
        AddPointerEvents(buttonZ);
        AddPointerEvents(buttonNone);

        // If configured, enforce a start axis (overrides toggle initial states).
        if (useStartAxis)
        {
            SetActiveAxis(startAxis);
            UpdateButtonVisuals();
            return;
        }

        // No start axis requested: ensure everything is off by default.
        ApplyAxisNone();
        UpdateButtonVisuals();
    }

    private void OnDestroy()
    {
        if (buttonX != null && cbX != null) buttonX.onClick.RemoveListener(cbX);
        if (buttonY != null && cbY != null) buttonY.onClick.RemoveListener(cbY);
        if (buttonZ != null && cbZ != null) buttonZ.onClick.RemoveListener(cbZ);
        if (buttonNone != null && cbNone != null) buttonNone.onClick.RemoveListener(cbNone);

        // clean up event triggers we created
        foreach (var et in createdEventTriggers)
        {
            if (et != null) Destroy(et);
        }
        createdEventTriggers.Clear();
    }

    private void OnDisable()
    {
        // Optionally restore original boxes when the switcher is disabled
        RestoreOriginals();
    }

    /// <summary>
    /// Make the given axis the only active axis: assigns boxes, and enables slider group for that axis.
    /// </summary>
    public void SetActiveAxis(Axis axis)
    {
        if (clippingBoxTarget == null) return;

        // record active axis
        activeAxis = axis;

        // assign boxes based on axis; fall back to originals when a specific transform is missing
        switch (axis)
        {
            case Axis.X:
                clippingBoxTarget.box1 = box1_X != null ? box1_X : originalBox1;
                clippingBoxTarget.box2 = box2_X != null ? box2_X : originalBox2;
                break;
            case Axis.Y:
                clippingBoxTarget.box1 = box1_Y != null ? box1_Y : originalBox1;
                clippingBoxTarget.box2 = box2_Y != null ? box2_Y : originalBox2;
                break;
            case Axis.Z:
                clippingBoxTarget.box1 = box1_Z != null ? box1_Z : originalBox1;
                clippingBoxTarget.box2 = box2_Z != null ? box2_Z : originalBox2;
                break;
        }

        // show/hide slider groups
        if (slidersGroupX != null) slidersGroupX.SetActive(axis == Axis.X);
        if (slidersGroupY != null) slidersGroupY.SetActive(axis == Axis.Y);
        if (slidersGroupZ != null) slidersGroupZ.SetActive(axis == Axis.Z);

        // enable clipping on the target
        if (clippingBoxTarget != null) clippingBoxTarget.SetClippingEnabled(true);

        // update button visuals
        UpdateButtonVisuals();
    }

    /// <summary>
    /// Restore original boxes and hide slider groups.
    /// </summary>
    public void ApplyAxisNone()
    {
        // clear active axis
        activeAxis = null;

        RestoreOriginals();

        if (slidersGroupX != null) slidersGroupX.SetActive(false);
        if (slidersGroupY != null) slidersGroupY.SetActive(false);
        if (slidersGroupZ != null) slidersGroupZ.SetActive(false);

        // disable clipping when no axis is active
        if (clippingBoxTarget != null) clippingBoxTarget.SetClippingEnabled(false);

        // update button visuals
        UpdateButtonVisuals();
    }

    private void UpdateButtonVisuals()
    {
        // Helper to set a button's visual color
        void SetButtonColor(Button btn, bool active)
        {
            if (btn == null) return;
            Color color;
            if (active && activeColorMap != null && activeColorMap.ContainsKey(btn)) color = activeColorMap[btn];
            else color = inactiveButtonColor;

            if (useTargetGraphicColor && btn.targetGraphic != null)
            {
                btn.targetGraphic.color = color;
            }
            else
            {
                var cb = btn.colors;
                cb.normalColor = color;
                // keep pressed/selected colors readable based on active
                cb.highlightedColor = color;
                btn.colors = cb;
            }
        }

        bool isX = activeAxis.HasValue && activeAxis.Value == Axis.X;
        bool isY = activeAxis.HasValue && activeAxis.Value == Axis.Y;
        bool isZ = activeAxis.HasValue && activeAxis.Value == Axis.Z;
        bool isNone = !activeAxis.HasValue;

        SetButtonColor(buttonX, isX);
        SetButtonColor(buttonY, isY);
        SetButtonColor(buttonZ, isZ);
        SetButtonColor(buttonNone, isNone);
    }

    private void AddPointerEvents(Button btn)
    {
        if (btn == null) return;
        var go = btn.gameObject;
        var et = go.GetComponent<EventTrigger>();
        if (et == null) et = go.AddComponent<EventTrigger>();
        createdEventTriggers.Add(et);

        // Pointer Enter
        var entryEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entryEnter.callback.AddListener((data) => { OnHoverEnter(btn); });
        et.triggers.Add(entryEnter);

        // Pointer Exit
        var entryExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        entryExit.callback.AddListener((data) => { OnHoverExit(btn); });
        et.triggers.Add(entryExit);
    }

    private void OnHoverEnter(Button btn)
    {
        if (btn == null) return;
        Color hover;
        if (hoverColorMap != null && hoverColorMap.TryGetValue(btn, out hover))
        {
            if (useTargetGraphicColor && btn.targetGraphic != null)
            {
                btn.targetGraphic.color = hover;
            }
            else
            {
                var cb = btn.colors;
                cb.normalColor = hover;
                cb.highlightedColor = hover;
                btn.colors = cb;
            }
        }
    }

    private void OnHoverExit(Button btn)
    {
        // restore according to active/inactive state
        if (btn == null) return;

        bool active = false;
        if (activeAxis.HasValue)
        {
            if (activeAxis.Value == Axis.X && btn == buttonX) active = true;
            if (activeAxis.Value == Axis.Y && btn == buttonY) active = true;
            if (activeAxis.Value == Axis.Z && btn == buttonZ) active = true;
        }
        else if (btn == buttonNone) active = true;

        // reuse SetButtonColor's logic by calling local helper via UpdateButtonVisuals
        UpdateSingleButtonVisual(btn, active);
    }

    private void UpdateSingleButtonVisual(Button btn, bool active)
    {
        if (btn == null) return;
        Color color;
        if (active && activeColorMap != null && activeColorMap.ContainsKey(btn)) color = activeColorMap[btn];
        else color = inactiveButtonColor;

        if (useTargetGraphicColor && btn.targetGraphic != null)
        {
            btn.targetGraphic.color = color;
        }
        else
        {
            var cb = btn.colors;
            cb.normalColor = color;
            cb.highlightedColor = color;
            btn.colors = cb;
        }
    }

    /// <summary>
    /// Restore the original boxes cached at Start.
    /// </summary>
    public void RestoreOriginals()
    {
        if (clippingBoxTarget == null) return;
        clippingBoxTarget.box1 = originalBox1;
        clippingBoxTarget.box2 = originalBox2;
    }

    /// <summary>
    /// Editor helper to force-apply the X axis state from the inspector.
    /// </summary>
    [ContextMenu("Apply X Axis")]
    private void ApplyXContext() { SetActiveAxis(Axis.X); }

    /// <summary>
    /// Editor helper to force-apply the Y axis state from the inspector.
    /// </summary>
    [ContextMenu("Apply Y Axis")]
    private void ApplyYContext() { SetActiveAxis(Axis.Y); }

    /// <summary>
    /// Editor helper to force-apply the Z axis state from the inspector.
    /// </summary>
    [ContextMenu("Apply Z Axis")]
    private void ApplyZContext() { SetActiveAxis(Axis.Z); }

    /// <summary>
    /// Editor helper to clear all axes (disable clipping).
    /// </summary>
    [ContextMenu("Apply None")]
    private void ApplyNoneContext() { ApplyAxisNone(); }
}
