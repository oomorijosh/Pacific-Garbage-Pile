using UnityEngine;

namespace PacificExhibit
{
    /// <summary>Art controls for the four empty bays. Surfaces animate on the GPU.</summary>
    [ExecuteAlways]
    public sealed class PacificExhibitSettings : MonoBehaviour
    {
        [Header("Water separators")]
        public bool animateWater = true;
        [Range(0f, 0.08f)] public float waveHeight = 0.024f;
        [Range(0f, 2f)] public float waterSpeed = 0.35f;
        [Range(0.02f, 0.65f)] public float waterOpacity = 0.22f;
        [Range(0f, 6f)] public float waterGlow = 2.6f;
        [ColorUsage(false, true)] public Color waterColor = new Color(0.12f, 0.68f, 1f);

        [Header("Box")]
        [ColorUsage(false, true)] public Color frameColor = new Color(0.14f, 0.75f, 1f) * 4f;
        [Range(0f, 0.3f)] public float glassOpacity = 0.035f;

        [HideInInspector] public Renderer[] waterSurfaces;
        [HideInInspector] public Renderer[] frameBars;
        [HideInInspector] public Renderer[] glassPanels;
        MaterialPropertyBlock properties;

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        [ContextMenu("Apply Appearance")]
        public void Apply()
        {
            properties ??= new MaterialPropertyBlock();
            if (waterSurfaces != null)
                foreach (Renderer surface in waterSurfaces)
                {
                    if (!surface) continue;
                    surface.GetPropertyBlock(properties);
                    properties.SetFloat("_WaveHeight", animateWater ? waveHeight : 0f);
                    properties.SetFloat("_WaveSpeed", animateWater ? waterSpeed : 0f);
                    properties.SetFloat("_Opacity", waterOpacity);
                    properties.SetFloat("_EmissionStrength", waterGlow);
                    properties.SetColor("_GlowColor", waterColor);
                    surface.SetPropertyBlock(properties);
                }
            if (frameBars != null)
                foreach (Renderer bar in frameBars)
                {
                    if (!bar) continue;
                    properties.Clear();
                    properties.SetColor("_BaseColor", frameColor);
                    bar.SetPropertyBlock(properties);
                }
            if (glassPanels != null)
                foreach (Renderer panel in glassPanels)
                {
                    if (!panel) continue;
                    properties.Clear();
                    properties.SetFloat("_Opacity", glassOpacity);
                    panel.SetPropertyBlock(properties);
                }
        }
    }
}
