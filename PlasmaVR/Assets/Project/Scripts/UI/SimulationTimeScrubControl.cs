using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Unity.VRTemplate
{
    /// <summary>
    /// Connects a UI slider control to a simulation controller, allowing users to scrub to a particular frame in the simulation.
    /// </summary>

    [RequireComponent(typeof(SimulationController))]
    public class SimulationTimeScrubControl : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Simulation play/pause button GameObject")]
        GameObject m_ButtonPlayOrPause;

        [SerializeField]
        [Tooltip("Slider that controls the simulation")]
        Slider m_Slider;

        [SerializeField]
        [Tooltip("Play icon sprite")]
        Sprite m_IconPlay;

        [SerializeField]
        [Tooltip("Pause icon sprite")]
        Sprite m_IconPause;

        [SerializeField]
        [Tooltip("Play or pause button image.")]
        Image m_ButtonPlayOrPauseIcon;

        [SerializeField]
        [Tooltip("Text that displays the current frame of the simulation.")]
        TextMeshProUGUI m_SimulationTimeText;

        [SerializeField]
        [Tooltip("If checked, the slider will fade off after a few seconds. If unchecked, the slider will remain on.")]
        bool m_HideSliderAfterFewSeconds = false;

        [SerializeField]
        [Tooltip("Speed multiplier for simulation playback")]
        float m_PlaybackSpeed = 1.0f;

        bool m_IsDragging;
        bool m_SimulationIsPlaying;
        bool m_SimulationJumpPending;
        int m_LastFrameBeforeScrub;
        SimulationController m_SimulationController;

        void Start()
        {
            m_SimulationController = GetComponent<SimulationController>();
            
            // Initialize simulation state
            if (m_SimulationController != null)
            {
                m_SimulationIsPlaying = m_SimulationController.playing;
                m_SimulationController.SetPlaybackSpeed(m_PlaybackSpeed);
                
                // Set up slider range
                if (m_Slider != null)
                {
                    m_Slider.minValue = 0;
                    m_Slider.maxValue = m_SimulationController.maxFrame > 0 ? m_SimulationController.maxFrame - 1 : 0;
                    m_Slider.value = m_SimulationController.currFrame;
                }
            }

            if (m_ButtonPlayOrPause != null)
                m_ButtonPlayOrPause.SetActive(true);

            UpdateSimulationUI();
        }

        void OnEnable()
        {
            if (m_SimulationController != null)
            {
                // Reset to first frame when enabled
                m_SimulationController.changeFrame(0);
                UpdateSimulationUI();
            }

            if (m_Slider != null)
            {
                m_Slider.value = 0.0f;
                m_Slider.onValueChanged.AddListener(OnSliderValueChange);
                m_Slider.gameObject.SetActive(true);
            }

            if (m_HideSliderAfterFewSeconds)
                StartCoroutine(HideSliderAfterSeconds());
        }

        void OnDisable()
        {
            if (m_Slider != null)
            {
                m_Slider.onValueChanged.RemoveListener(OnSliderValueChange);
            }
        }

        void Update()
        {
            if (m_SimulationController == null) return;

            if (m_SimulationIsPlaying != m_SimulationController.playing)
            {
                m_SimulationIsPlaying = m_SimulationController.playing;
                UpdateSimulationUI();
            }

            if (m_SimulationJumpPending)
            {
                // We're trying to jump to a new position, check if simulation has updated
                if (m_LastFrameBeforeScrub == m_SimulationController.currFrame)
                    return;

                // If the simulation has been updated with desired jump frame, reset these values
                m_LastFrameBeforeScrub = -1;
                m_SimulationJumpPending = false;
            }

            if (!m_IsDragging && !m_SimulationJumpPending)
            {
                // Update slider to match current simulation frame
                if (m_SimulationController.maxFrame > 0)
                {
                    // Guard against divide-by-zero for single-frame datasets (maxFrame == 1)
                    var progress = m_SimulationController.maxFrame > 1
                        ? (float)m_SimulationController.currFrame / (m_SimulationController.maxFrame - 1)
                        : 0f;
                    m_Slider.value = progress * m_Slider.maxValue;
                }
                
                UpdateSimulationTimeText();
            }
        }

        public void OnPointerDown()
        {
            m_SimulationJumpPending = true;
            SimulationStop();
            SimulationJump();
        }

        public void OnRelease()
        {
            m_IsDragging = false;
            if (m_SimulationIsPlaying)
                SimulationPlay();
            SimulationJump();
        }

        void OnSliderValueChange(float sliderValue)
        {
            UpdateSimulationTimeText();
            if (m_IsDragging)
            {
                SimulationJump();
            }
        }

        IEnumerator HideSliderAfterSeconds(float duration = 3f)
        {
            yield return new WaitForSeconds(duration);
            if (m_Slider != null)
                m_Slider.gameObject.SetActive(false);
        }

        public void OnDrag()
        {
            m_IsDragging = true;
            m_SimulationJumpPending = true;
        }

        void SimulationJump()
        {
            if (m_SimulationController == null || m_Slider == null) return;

            m_SimulationJumpPending = true;
            var targetFrame = Mathf.RoundToInt(m_Slider.value);
            m_LastFrameBeforeScrub = m_SimulationController.currFrame;
            
            // Clamp to valid range
            targetFrame = Mathf.Clamp(targetFrame, 0, m_SimulationController.maxFrame - 1);
            
            m_SimulationController.changeFrame(targetFrame);
        }

        public void PlayOrPauseSimulation()
        {
            if (m_SimulationIsPlaying)
            {
                SimulationStop();
            }
            else
            {
                SimulationPlay();
            }
        }

        public void NextFrame()
        {
            if (m_SimulationController != null)
            {
                SimulationStop();
                m_SimulationController.nextFrame();
                UpdateSimulationUI();
            }
        }

        public void PreviousFrame()
        {
            if (m_SimulationController != null)
            {
                SimulationStop();
                m_SimulationController.prevFrame();
                UpdateSimulationUI();
            }
        }

        public void RestartSimulation()
        {
            if (m_SimulationController != null)
            {
                m_SimulationController.changeFrame(0);
                if (m_Slider != null)
                    m_Slider.value = 0;
                UpdateSimulationUI();
            }
        }

        public void SetPlaybackSpeed(float speed)
        {
            m_PlaybackSpeed = Mathf.Clamp(speed, 0.1f, 5.0f);
            if (m_SimulationController != null)
            {
                m_SimulationController.SetPlaybackSpeed(m_PlaybackSpeed);
            }
        }

        void UpdateSimulationTimeText()
        {
            if (m_SimulationController != null && m_SimulationTimeText != null)
            {
                var currentFrame = m_SimulationController.currFrame;
                var totalFrames = m_SimulationController.maxFrame;
                
                m_SimulationTimeText.SetText($"Frame\n{currentFrame + 1} / {totalFrames}");
            }
        }

        void UpdateSimulationUI()
        {
            UpdateSimulationTimeText();
            
            if (m_ButtonPlayOrPauseIcon != null)
            {
                m_ButtonPlayOrPauseIcon.sprite = m_SimulationIsPlaying ? m_IconPause : m_IconPlay;
            }
            
            if (m_ButtonPlayOrPause != null)
            {
                m_ButtonPlayOrPause.SetActive(true);
            }
        }

        void SimulationStop()
        {
            m_SimulationIsPlaying = false;
            if (m_SimulationController != null)
            {
                m_SimulationController.toggleAnimation(false);
            }
            UpdateSimulationUI();
        }

        void SimulationPlay()
        {
            m_SimulationIsPlaying = true;
            if (m_SimulationController != null)
            {
                m_SimulationController.toggleAnimation(true);
            }
            UpdateSimulationUI();
        }

        // Public methods for UI button connections
        public void TogglePlayPause()
        {
            PlayOrPauseSimulation();
        }

        public void SetSimulationFrame(int frame)
        {
            if (m_SimulationController != null && m_Slider != null)
            {
                frame = Mathf.Clamp(frame, 0, m_SimulationController.maxFrame - 1);
                m_SimulationController.changeFrame(frame);
                m_Slider.value = frame;
                UpdateSimulationUI();
            }
        }

        public float GetSimulationProgress()
        {
            if (m_SimulationController != null && m_SimulationController.maxFrame > 1)
            {
                // Guard: maxFrame == 1 means single-frame dataset; progress is always 0
                return (float)m_SimulationController.currFrame / (m_SimulationController.maxFrame - 1);
            }
            return 0f;
        }

        public bool IsPlaying()
        {
            return m_SimulationIsPlaying;
        }

        public int GetCurrentFrame()
        {
            return m_SimulationController != null ? m_SimulationController.currFrame : 0;
        }

        public int GetMaxFrames()
        {
            return m_SimulationController != null ? m_SimulationController.maxFrame : 0;
        }

        /// <summary>
        /// Refresh the slider range when a new dataset is loaded.
        /// </summary>
        public void RefreshSliderRange(int maxFrames, int currentFrame = 0)
        {
            if (m_Slider == null) return;

            m_Slider.minValue = 0;
            m_Slider.maxValue = maxFrames > 0 ? maxFrames - 1 : 0;
            m_Slider.value = Mathf.Clamp(currentFrame, 0, (int)m_Slider.maxValue);

            UpdateSimulationUI();
        }
    }
}