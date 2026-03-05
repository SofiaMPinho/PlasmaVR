using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// Opens the Meta Quest system keyboard when a TMP_InputField is clicked with a VR controller.
/// Syncs keyboard text back to the input field each frame while the keyboard is active.
/// Attach to a GameObject that also has a TMP_InputField component.
/// </summary>
public class VRKeyboardTrigger : MonoBehaviour, IPointerClickHandler
{
    private TMP_InputField _inputField;
    private TouchScreenKeyboard _systemKeyboard;

    void Awake()
    {
        _inputField = GetComponent<TMP_InputField>();
    }

    // This fires when you click the input field with your VR controller
    public void OnPointerClick(PointerEventData eventData)
    {
        // Open the native Meta Quest system keyboard
        _systemKeyboard = TouchScreenKeyboard.Open(_inputField.text, TouchScreenKeyboardType.Default);
    }

    void Update()
    {
        // While the keyboard is open, sync the text to your input field
        if (_systemKeyboard != null && _systemKeyboard.active)
        {
            _inputField.text = _systemKeyboard.text;
        }
    }
}