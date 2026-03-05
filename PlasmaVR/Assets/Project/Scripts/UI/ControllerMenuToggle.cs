using UnityEngine;
using UnityEngine.InputSystem;

namespace PlasmaVR.UI
{
    /// <summary>
    /// Manages animated menu panels attached to VR controller input.
    /// Toggles X and Y button menus with configurable animations and auto-hide behavior.
    /// Enforces menu anchoring to prevent drift during locomotion or XR Origin movement.
    /// </summary>
    public class ControllerMenuToggle : MonoBehaviour
    {
        [Header("Menu Settings")]
        [SerializeField, Tooltip("The X menu panel GameObject to show/hide")]
        private GameObject xMenuPanel;
        [SerializeField, Tooltip("The Y menu panel GameObject to show/hide")]
        private GameObject yMenuPanel;
        
        [SerializeField, Tooltip("Input action for X button to toggle its menu")]
        private InputActionReference xButtonToggleAction;
        [SerializeField, Tooltip("Input action for Y button to toggle its menu")]
        private InputActionReference yButtonToggleAction;
        
        [Header("Animation Settings")]
        [SerializeField, Tooltip("Should the menu animate in/out?")]
        private bool useAnimation = true;
        
        [SerializeField, Tooltip("Animation duration in seconds")]
        private float animationDuration = 0.3f;
        
        [SerializeField, Tooltip("Animation curve for smooth transitions")]
        private AnimationCurve animationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        
        [Header("Auto-Hide Settings")]
        [SerializeField, Tooltip("Auto-hide menu after inactivity?")]
        private bool autoHideEnabled = false;
        
        [SerializeField, Tooltip("Seconds of inactivity before auto-hide")]
        private float autoHideDelay = 5f;

    #if UNITY_EDITOR
        [Header("Editor Testing")]
        [SerializeField, Tooltip("[Editor Only] Show X menu on startup")]
        private bool showXMenuOnStartInEditor = false;
        [SerializeField, Tooltip("[Editor Only] Show Y menu on startup")]
        private bool showYMenuOnStartInEditor = false;
    #endif
        
        // No explicit bools; we rely on panel active state
        private bool isAnimating = false;
        private Coroutine animationCoroutine;
        private Coroutine autoHideCoroutine;
        private Vector3 xOriginalScale;
        private Vector3 yOriginalScale;
        
        // Cache original local positions to restore after locomotion
        private Vector3 xOriginalLocalPosition;
        private Vector3 yOriginalLocalPosition;
        private Quaternion xOriginalLocalRotation;
        private Quaternion yOriginalLocalRotation;
        private bool hasStoredTransforms = false;
        
        void OnEnable()
        {
            if (xButtonToggleAction != null)
            {
                xButtonToggleAction.action.performed += ToggleXMenu;
                xButtonToggleAction.action.Enable();
            }
            if (yButtonToggleAction != null)
            {
                yButtonToggleAction.action.performed += ToggleYMenu;
                yButtonToggleAction.action.Enable();
            }
        }
        
        void OnDisable()
        {
            if (xButtonToggleAction != null)
            {
                xButtonToggleAction.action.performed -= ToggleXMenu;
                xButtonToggleAction.action.Disable();
            }
            if (yButtonToggleAction != null)
            {
                yButtonToggleAction.action.performed -= ToggleYMenu;
                yButtonToggleAction.action.Disable();
            }
                
            StopAllCoroutines();
        }
        
        void Start()
        {
            InitializeMenu();
            CacheOriginalTransforms();

#if UNITY_EDITOR
            if (showXMenuOnStartInEditor)
            {
                ShowXMenu();
            }

            if (showYMenuOnStartInEditor)
            {
                ShowYMenu();
            }
#endif
        }
        
        void InitializeMenu()
        {
            if (xMenuPanel != null)
            {
                // Store original scale for animations
                xOriginalScale = xMenuPanel.transform.localScale;
                
                // Start with menu hidden
                if (useAnimation)
                {
                    xMenuPanel.transform.localScale = Vector3.zero;
                    xMenuPanel.SetActive(false);
                }
                else
                {
                    xMenuPanel.SetActive(false);
                }
            }
            else
            {
                Debug.LogWarning($"[ControllerMenuToggle] X menu panel not assigned on {gameObject.name}");
            }

            if (yMenuPanel != null)
            {
                yOriginalScale = yMenuPanel.transform.localScale;
                
                if (useAnimation)
                {
                    yMenuPanel.transform.localScale = Vector3.zero;
                    yMenuPanel.SetActive(false);
                }
                else
                {
                    yMenuPanel.SetActive(false);
                }
            }
            else
            {
                Debug.LogWarning($"[ControllerMenuToggle] Y menu panel not assigned on {gameObject.name}");
            }
        }
        
        /// <summary>
        /// Cache the original local transforms so we can restore them after locomotion.
        /// </summary>
        private void CacheOriginalTransforms()
        {
            if (xMenuPanel != null)
            {
                xOriginalLocalPosition = xMenuPanel.transform.localPosition;
                xOriginalLocalRotation = xMenuPanel.transform.localRotation;
            }
            if (yMenuPanel != null)
            {
                yOriginalLocalPosition = yMenuPanel.transform.localPosition;
                yOriginalLocalRotation = yMenuPanel.transform.localRotation;
            }
            hasStoredTransforms = true;
        }
        
        /// <summary>
        /// Restore menu transforms to their original local positions/rotations.
        /// Call this when locomotion occurs while menus are open.
        /// </summary>
        private void RestoreMenuTransforms()
        {
            if (!hasStoredTransforms) return;
            
            if (xMenuPanel != null && xMenuPanel.activeSelf)
            {
                xMenuPanel.transform.localPosition = xOriginalLocalPosition;
                xMenuPanel.transform.localRotation = xOriginalLocalRotation;
            }
            if (yMenuPanel != null && yMenuPanel.activeSelf)
            {
                yMenuPanel.transform.localPosition = yOriginalLocalPosition;
                yMenuPanel.transform.localRotation = yOriginalLocalRotation;
            }
        }
        
        void LateUpdate()
        {
            // Continuously enforce correct local transform when menus are open
            // This prevents drift during locomotion or parent transform changes
            RestoreMenuTransforms();
        }
        
        private void ToggleXMenu(InputAction.CallbackContext context)
        {
            if (isAnimating) return;
            bool isActive = xMenuPanel != null && xMenuPanel.activeSelf;
            if (isActive)
            {
                HideXMenu();
            }
            else
            {
                // ensure other menu is closed
                if (yMenuPanel != null && yMenuPanel.activeSelf) HideYMenu();
                ShowXMenu();
            }
        }
        
        private void ToggleYMenu(InputAction.CallbackContext context)
        {
            if (isAnimating) return;
            bool isActive = yMenuPanel != null && yMenuPanel.activeSelf;
            if (isActive)
            {
                HideYMenu();
            }
            else
            {
                if (xMenuPanel != null && xMenuPanel.activeSelf) HideXMenu();
                ShowYMenu();
            }
        }
        
        public void ShowXMenu()
        {
            if (xMenuPanel == null || isAnimating) return;
            
            // Only one can be open: close Y
            if (yMenuPanel != null && yMenuPanel.activeSelf) HideYMenu();
            
            // Stop auto-hide if running
            if (autoHideCoroutine != null)
            {
                StopCoroutine(autoHideCoroutine);
                autoHideCoroutine = null;
            }
            
            if (useAnimation)
            {
                if (animationCoroutine != null)
                    StopCoroutine(animationCoroutine);
                
                animationCoroutine = StartCoroutine(AnimateMenuShow(xMenuPanel, xOriginalScale, () => { isAnimating = false; if (autoHideEnabled) autoHideCoroutine = StartCoroutine(AutoHideTimer()); }));
            }
            else
            {
                xMenuPanel.SetActive(true);
                xMenuPanel.transform.localScale = xOriginalScale;
            }
            
            Debug.Log("[ControllerMenuToggle] X Menu shown");
        }
        
        public void HideXMenu()
        {
            if (xMenuPanel == null || isAnimating) return;
            
            // Stop auto-hide if running
            if (autoHideCoroutine != null)
            {
                StopCoroutine(autoHideCoroutine);
                autoHideCoroutine = null;
            }
            
            if (useAnimation)
            {
                if (animationCoroutine != null)
                    StopCoroutine(animationCoroutine);
                
                animationCoroutine = StartCoroutine(AnimateMenuHide(xMenuPanel));
            }
            else
            {
                xMenuPanel.SetActive(false);
            }
            
            Debug.Log("[ControllerMenuToggle] X Menu hidden");
        }

        public void ShowYMenu()
        {
            if (yMenuPanel == null || isAnimating) return;

            if (autoHideCoroutine != null)
            {
                StopCoroutine(autoHideCoroutine);
                autoHideCoroutine = null;
            }

            if (useAnimation)
            {
                if (animationCoroutine != null)
                    StopCoroutine(animationCoroutine);

                animationCoroutine = StartCoroutine(AnimateMenuShow(yMenuPanel, yOriginalScale, () => { isAnimating = false; if (autoHideEnabled) autoHideCoroutine = StartCoroutine(AutoHideTimer()); }));
            }
            else
            {
                yMenuPanel.SetActive(true);
                yMenuPanel.transform.localScale = yOriginalScale;
            }

            Debug.Log("[ControllerMenuToggle] Y Menu shown");
        }

        public void HideYMenu()
        {
            if (yMenuPanel == null || isAnimating) return;
            if (autoHideCoroutine != null)
            {
                StopCoroutine(autoHideCoroutine);
                autoHideCoroutine = null;
            }

            if (useAnimation)
            {
                if (animationCoroutine != null)
                    StopCoroutine(animationCoroutine);

                animationCoroutine = StartCoroutine(AnimateMenuHide(yMenuPanel));
            }
            else
            {
                yMenuPanel.SetActive(false);
            }

            Debug.Log("[ControllerMenuToggle] Y Menu hidden");
        }
        
        private System.Collections.IEnumerator AnimateMenuShow(GameObject panel, Vector3 targetScale, System.Action onComplete)
        {
            isAnimating = true;
            panel.SetActive(true);

            float elapsedTime = 0f;
            Vector3 startScale = Vector3.zero;

            while (elapsedTime < animationDuration)
            {
                elapsedTime += Time.deltaTime;
                float progress = elapsedTime / animationDuration;
                float curveValue = animationCurve.Evaluate(progress);

                panel.transform.localScale = Vector3.Lerp(startScale, targetScale, curveValue);
                yield return null;
            }

            panel.transform.localScale = targetScale;
            onComplete?.Invoke();
        }
        
        private System.Collections.IEnumerator AnimateMenuHide(GameObject panel)
        {
            isAnimating = true;

            float elapsedTime = 0f;
            Vector3 startScale = panel.transform.localScale;
            Vector3 targetScale = Vector3.zero;

            while (elapsedTime < animationDuration)
            {
                elapsedTime += Time.deltaTime;
                float progress = elapsedTime / animationDuration;
                float curveValue = animationCurve.Evaluate(progress);

                panel.transform.localScale = Vector3.Lerp(startScale, targetScale, curveValue);
                yield return null;
            }

            panel.transform.localScale = targetScale;
            panel.SetActive(false);
            isAnimating = false;
        }
        
        private System.Collections.IEnumerator AutoHideTimer()
        {
            yield return new WaitForSeconds(autoHideDelay);
            
            if (!isAnimating)
            {
                if (xMenuPanel != null && xMenuPanel.activeSelf)
                    HideXMenu();
                else if (yMenuPanel != null && yMenuPanel.activeSelf)
                    HideYMenu();
            }
        }
        
        // Public methods for external control
        public void ForceShowXMenu() => ShowXMenu();
        public void ForceHideXMenu() => HideXMenu();
        public void ForceShowYMenu() => ShowYMenu();
        public void ForceHideYMenu() => HideYMenu();
        public bool IsXMenuVisible() => xMenuPanel != null && xMenuPanel.activeSelf;
        public bool IsYMenuVisible() => yMenuPanel != null && yMenuPanel.activeSelf;
        
        public void SetAutoHide(bool enabled)
        {
            autoHideEnabled = enabled;
            
            if (!enabled && autoHideCoroutine != null)
            {
                StopCoroutine(autoHideCoroutine);
                autoHideCoroutine = null;
            }
        }
        
        public void ResetAutoHideTimer()
        {
            if (autoHideEnabled && autoHideCoroutine != null && ((xMenuPanel != null && xMenuPanel.activeSelf) || (yMenuPanel != null && yMenuPanel.activeSelf)))
            {
                StopCoroutine(autoHideCoroutine);
                autoHideCoroutine = StartCoroutine(AutoHideTimer());
            }
        }
        
        // Context menu for testing
        [ContextMenu("Test Show X Menu")] private void TestShowXMenu() => ShowXMenu();
        [ContextMenu("Test Hide X Menu")] private void TestHideXMenu() => HideXMenu();
        [ContextMenu("Test Show Y Menu")] private void TestShowYMenu() => ShowYMenu();
        [ContextMenu("Test Hide Y Menu")] private void TestHideYMenu() => HideYMenu();
    }
}