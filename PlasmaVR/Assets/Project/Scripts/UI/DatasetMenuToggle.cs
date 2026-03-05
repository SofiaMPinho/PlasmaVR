using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach this script to any GameObject (e.g. a persistent HUD canvas) to give
/// a single button the ability to open and close the DatasetSelectionUI panel.
/// </summary>
public class DatasetMenuToggle : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The button that will open / close the dataset menu.")]
    [SerializeField] private Button toggleButton;

    [Tooltip("The DatasetSelectionUI component that controls the panel.")]
    [SerializeField] private DatasetSelectionUI datasetSelectionUI;

    [Header("Button Labels (optional)")]
    [Tooltip("Label shown on the button when the panel is closed.")]
    [SerializeField] private string openLabel = "Datasets";

    [Tooltip("Label shown on the button when the panel is open.")]
    [SerializeField] private string closeLabel = "Close";

    // Cached reference to the button's TMP label — may be null if no TMP child exists.
    private TextMeshProUGUI buttonLabel;

    // Tracks the panel's current visual state so we can toggle correctly.
    private bool isPanelOpen = false;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (toggleButton == null)
        {
            Debug.LogError("[DatasetMenuToggle] toggleButton is not assigned!", this);
            return;
        }

        if (datasetSelectionUI == null)
        {
            Debug.LogError("[DatasetMenuToggle] datasetSelectionUI is not assigned!", this);
            return;
        }

        buttonLabel = toggleButton.GetComponentInChildren<TextMeshProUGUI>();
        toggleButton.onClick.AddListener(OnToggleButtonClicked);
    }

    private void Start()
    {
        if (datasetSelectionUI == null) return;

        // Read the panel's actual active state AFTER DatasetSelectionUI.Start() has run.
        // DatasetSelectionUI.Start() is where showOnStartup is applied, so by the time
        // our Start() executes the selectionPanel's SetActive state is already correct.
        isPanelOpen = datasetSelectionUI.IsPanelOpen;
        SyncLabel();
    }

    // -------------------------------------------------------------------------
    // Toggle logic
    // -------------------------------------------------------------------------

    private void OnToggleButtonClicked()
    {
        if (isPanelOpen)
            ClosePanel();
        else
            OpenPanel();
    }

    /// <summary>Opens the dataset selection panel.</summary>
    public void OpenPanel()
    {
        if (datasetSelectionUI == null) return;

        datasetSelectionUI.ShowPanel();
        isPanelOpen = true;
        SyncLabel();
    }

    /// <summary>Closes the dataset selection panel.</summary>
    public void ClosePanel()
    {
        if (datasetSelectionUI == null) return;

        datasetSelectionUI.HidePanel();
        isPanelOpen = false;
        SyncLabel();
    }

    /// <summary>
    /// Explicitly sets the open/closed state from external code (e.g. a keyboard shortcut
    /// or another UI element that needs to force-close the panel).
    /// </summary>
    public void SetPanelOpen(bool open)
    {
        if (open) OpenPanel();
        else ClosePanel();
    }

    /// <summary>
    /// Update the internal open/closed state and label without invoking
    /// ShowPanel/HidePanel. Use this when an external UI element changed
    /// the panel's visibility and you only need to sync the button label.
    /// </summary>
    public void UpdatePanelStateVisual(bool open)
    {
        isPanelOpen = open;
        SyncLabel();
    }

    // -------------------------------------------------------------------------
    // Helper
    // -------------------------------------------------------------------------

    private void SyncLabel()
    {
        if (buttonLabel == null) return;
        buttonLabel.text = isPanelOpen ? closeLabel : openLabel;
    }
}
