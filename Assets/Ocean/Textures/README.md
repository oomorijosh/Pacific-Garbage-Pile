# Generated water texture set

Generated using the built-in image-generation tool. Exact submitted prompts
are recorded in generation-prompts.json. These are new source assets only;
the existing Shader Graph and materials have not been rewired in this step.

All four delivered PNGs are 1254 x 1254 (the tool returned this size despite the
1024-square prompt). Original pixels are preserved without image postprocessing.

| File | Intended shader use | Import |
| --- | --- | --- |
| TEX_GPGP_WaveHeight.png | Broad height gradients for surface normals | Linear |
| TEX_GPGP_RippleHeight.png | Independently scrolling fine height gradients for normals | Linear |
| TEX_GPGP_Caustics.png | Subtle animated cyan illumination mask | Linear |
| TEX_GPGP_ColorVariation.png | Low-strength body-color modulation | sRGB |
| TEX_GPGP_WaterSide.png | Vertical cube faces: waterline, caustics and depth fade | sRGB |
| TEX_GPGP_DebrisAtlas.png | 4x4 cutout atlas for camera-facing debris impostors | sRGB/alpha |
| TEX_GPGP_LayeredDebris.png | Flat four-band debris face for albedo/emission | sRGB |

Import metadata sets Repeat wrapping, trilinear filtering, mipmaps and a 2048
maximum size, with NPOT scaling disabled. Height textures are scalar data, not
normal maps: sample their red channel and derive normals in the shader. Do not
simply label the grayscale PNGs as normal maps. Keep geometric displacement on
the existing single top surface; do not add water surfaces inside the cube.

`TEX_GPGP_WaterSide.png` is assigned to `MAT_GPGP_WaterSide`. Its dedicated
URP shader adds a slow horizontal caustic drift while preserving the authored
vertical depth gradient. The water test uses it on the volume mesh and keeps
the existing animated Shader Graph material on the top grid.

`TEX_GPGP_DebrisAtlas.png` contains sixteen transparent object cutouts. The
`GPGP/Debris Impostor` shader selects cells per renderer, faces each quad toward
the active stereo camera, alpha-clips the silhouette and writes depth so the
cards intermix convincingly with the primitive debris and water volume.

The maps were prompted for seamless tiling. Opposing-edge mean absolute RGB
pixel differences (out of 255), horizontal/vertical, measured on saved files:
WaveHeight 2.12/4.11; RippleHeight 4.78/5.58; Caustics 4.46/4.71;
ColorVariation 4.12/2.54. Edges are close but not pixel-identical. Repeat wrapping
alone does not establish seamless appearance. Validate scrolling/repetition in
Unity before treating them as production-seamless; border crossfading in the
shader may be needed. No Unity render or normal conversion was performed here.
