using UnityEngine;
using UnityEngine.EventSystems;

namespace PlasmaVR.UI
{
    /// <summary>
    /// Provides hover depth and scale effects for UI elements in XR environments.
    /// Animates Z-translation and optional scaling when the pointer enters/exits an element.
    /// </summary>
    public class UIHoverDepthEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Hover Settings")]
        [SerializeField, Tooltip("How much to translate the element forward on the Z axis on hover")]
        private float zTranslation = 5f;
        
        [SerializeField, Tooltip("The RectTransform to move. If null, uses this GameObject's RectTransform")]
        private RectTransform targetElement;
        
        [SerializeField, Tooltip("Should the element scale slightly on hover?")]
        private bool useScaleEffect = false;
        
        [SerializeField, Tooltip("Scale multiplier when hovering (e.g., 1.1 for 10% larger)")]
        private float hoverScale = 1.05f;
        
        [Header("Animation Settings")]
        [SerializeField, Tooltip("Should the movement be animated smoothly?")]
        private bool useAnimation = false;
        
        [SerializeField, Tooltip("Animation duration in seconds")]
        private float animationDuration = 0.2f;
        
        [SerializeField, Tooltip("Animation curve for smooth transitions")]
        private AnimationCurve animationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        
        private float initialZ;
        private Vector3 initialScale;
        private bool isHovering = false;
        private Coroutine animationCoroutine;
        
        void Awake()
        {
            // Use this GameObject's RectTransform if no target specified
            if (targetElement == null)
            {
                targetElement = GetComponent<RectTransform>();
            }
            
            if (targetElement != null)
            {
                initialZ = targetElement.localPosition.z;
                initialScale = targetElement.localScale;
            }
            else
            {
                Debug.LogWarning($"[UIHoverDepthEffect] No RectTransform found on {gameObject.name}");
            }
        }
        
        /// <inheritdoc />
        public void OnPointerEnter(PointerEventData eventData)
        {
            PerformEntranceActions();
        }
        
        /// <inheritdoc />
        public void OnPointerExit(PointerEventData eventData)
        {
            PerformExitActions();
        }
        
        void PerformEntranceActions()
        {
            if (targetElement == null || isHovering) return;
            
            isHovering = true;
            
            if (useAnimation)
            {
                if (animationCoroutine != null)
                    StopCoroutine(animationCoroutine);
                    
                animationCoroutine = StartCoroutine(AnimateToHoverState());
            }
            else
            {
                // Immediate position change (original behavior)
                Vector3 newPosition = targetElement.localPosition;
                newPosition.z = initialZ - zTranslation;
                targetElement.localPosition = newPosition;
                
                // Apply scale effect if enabled
                if (useScaleEffect)
                {
                    targetElement.localScale = initialScale * hoverScale;
                }
            }
        }
        
        void PerformExitActions()
        {
            if (targetElement == null || !isHovering) return;
            
            isHovering = false;
            
            if (useAnimation)
            {
                if (animationCoroutine != null)
                    StopCoroutine(animationCoroutine);
                    
                animationCoroutine = StartCoroutine(AnimateToNormalState());
            }
            else
            {
                // Immediate return to original state (original behavior)
                Vector3 originalPosition = targetElement.localPosition;
                originalPosition.z = initialZ;
                targetElement.localPosition = originalPosition;
                
                // Reset scale
                targetElement.localScale = initialScale;
            }
        }
        
        private System.Collections.IEnumerator AnimateToHoverState()
        {
            Vector3 startPosition = targetElement.localPosition;
            Vector3 targetPosition = startPosition;
            targetPosition.z = initialZ - zTranslation;
            
            Vector3 startScale = targetElement.localScale;
            Vector3 targetScale = useScaleEffect ? initialScale * hoverScale : initialScale;
            
            float elapsedTime = 0f;
            
            while (elapsedTime < animationDuration)
            {
                elapsedTime += Time.deltaTime;
                float progress = elapsedTime / animationDuration;
                float curveValue = animationCurve.Evaluate(progress);
                
                // Animate position
                targetElement.localPosition = Vector3.Lerp(startPosition, targetPosition, curveValue);
                
                // Animate scale if enabled
                if (useScaleEffect)
                {
                    targetElement.localScale = Vector3.Lerp(startScale, targetScale, curveValue);
                }
                
                yield return null;
            }
            
            // Ensure final values are exact
            targetElement.localPosition = targetPosition;
            if (useScaleEffect)
            {
                targetElement.localScale = targetScale;
            }
        }
        
        private System.Collections.IEnumerator AnimateToNormalState()
        {
            Vector3 startPosition = targetElement.localPosition;
            Vector3 targetPosition = startPosition;
            targetPosition.z = initialZ;
            
            Vector3 startScale = targetElement.localScale;
            Vector3 targetScale = initialScale;
            
            float elapsedTime = 0f;
            
            while (elapsedTime < animationDuration)
            {
                elapsedTime += Time.deltaTime;
                float progress = elapsedTime / animationDuration;
                float curveValue = animationCurve.Evaluate(progress);
                
                // Animate position
                targetElement.localPosition = Vector3.Lerp(startPosition, targetPosition, curveValue);
                
                // Animate scale
                targetElement.localScale = Vector3.Lerp(startScale, targetScale, curveValue);
                
                yield return null;
            }
            
            // Ensure final values are exact
            targetElement.localPosition = targetPosition;
            targetElement.localScale = targetScale;
        }
        
        // Public methods for manual control
        public void ForceHoverState()
        {
            PerformEntranceActions();
        }
        
        public void ForceNormalState()
        {
            PerformExitActions();
        }
        
        public void SetZTranslation(float newTranslation)
        {
            zTranslation = newTranslation;
        }
        
        public void SetHoverScale(float newScale)
        {
            hoverScale = newScale;
        }
        
        public bool IsHovering()
        {
            return isHovering;
        }
        
        // Reset to initial state
        [ContextMenu("Reset to Initial State")]
        public void ResetToInitialState()
        {
            if (targetElement != null)
            {
                Vector3 resetPosition = targetElement.localPosition;
                resetPosition.z = initialZ;
                targetElement.localPosition = resetPosition;
                targetElement.localScale = initialScale;
                isHovering = false;
                
                if (animationCoroutine != null)
                {
                    StopCoroutine(animationCoroutine);
                    animationCoroutine = null;
                }
            }
        }
        
        void OnDisable()
        {
            // Clean up animation if component is disabled
            if (animationCoroutine != null)
            {
                StopCoroutine(animationCoroutine);
                animationCoroutine = null;
            }
            
            // Reset to normal state
            isHovering = false;
        }
    }
}