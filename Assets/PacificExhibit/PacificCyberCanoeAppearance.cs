using UnityEngine;

namespace PacificExhibit
{
    /// <summary>Keeps the editor preview clear until Cyber Canoe initializes its stereo output.</summary>
    [ExecuteAlways, DefaultExecutionOrder(-500), DisallowMultipleComponent]
    public sealed class PacificCyberCanoeAppearance : MonoBehaviour
    {
        [Tooltip("Cyber Canoe's output canvas. Hidden while editing and enabled once on entering Play mode.")]
        public Canvas stereoOutput;

        [Tooltip("Enable the stereo output when Play mode starts. Headset simulation can then take control of it.")]
        public bool enableStereoOutputInPlayMode = true;

        bool hasAppliedMode;
        bool appliedPlayMode;

        void OnEnable() => RefreshPreview();

        void Update() => ApplyMode(false);

        /// <summary>Applies the current mode after assigning the output canvas in the editor.</summary>
        public void RefreshPreview() => ApplyMode(true);

        void ApplyMode(bool force)
        {
            bool playMode = Application.isPlaying;
            if (!force && hasAppliedMode && appliedPlayMode == playMode) return;

            hasAppliedMode = true;
            appliedPlayMode = playMode;
            if (stereoOutput) stereoOutput.enabled = playMode && enableStereoOutputInPlayMode;
        }

        void OnDisable()
        {
            hasAppliedMode = false;
            // Restore normal editor behavior if this helper is disabled or removed.
            // During Play mode the headset simulator owns the output canvas state.
            if (!Application.isPlaying && stereoOutput)
                stereoOutput.enabled = enableStereoOutputInPlayMode;
        }
    }
}
