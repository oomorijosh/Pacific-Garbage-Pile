# Continuous water cube

The preview presents one exhibit cube with an animated upper surface. Its side
material uses `TEX_GPGP_LayeredDebris.png`, a flat four-band exhibit rendering
that supplies the detailed Micro, Meso, Macro and Mega debris appearance.

## Open the preview

1. Exit Play mode and allow the changed assets to import.
2. Reopen Assets/Scenes/GPGP_WaterTest.unity outside Play mode, then press Play.
   Reopening clears the previous generated stack if Unity retained it; this
   project has scene/domain reload disabled for Play mode.
3. The preview generates one GPGPWaterCompartment, size (4,4.4,3.5), containing
   one textured open-top volume mesh plus one subdivided upper surface mesh.

The original waterMaterials array remains ordered Micro, Meso, Macro, Mega.
The preview clones Mega's material at runtime for its wave and lighting settings,
then reads each preset's WaterShallowColor for the four-region palette. The
runtime material is destroyed with the test component. Existing material asset
GUIDs and the CyberCANOE camera configuration are unchanged.

## Geometry and appearance

The volume has four stable side faces and one bottom. Only the top grid moves;
its perimeter stays pinned to the volume rim. Two world-space geometric waves
and two independent normal ripple patterns animate this single surface.
Analytic normals account for the pinned-edge envelope, and mesh bounds include
both geometric wave amplitudes. No CPU mesh deformation runs every frame.

Normalized mesh height controls the color regions: Micro at the bottom,
then Meso, Macro, and Mega at the top. Smooth transitions centered at 25%, 50%,
and 75% height replace the old reflective shelves. The tint is evaluated per
fragment, so no extra side-wall subdivisions or geometry are needed.

Body alpha and emission provide a restrained depth-colored water approximation
against the dark background. This is not physical volumetric scattering or
refraction. A shared local reflection probe and test lights illuminate the
upper ripples. Narrow outer-edge highlights remain subdued; there are no
highlights at the internal color transitions.

The layered debris and the three internal wave separators are baked into the
side texture. The procedural debris and internal surface generators are not
instantiated in this preview, avoiding duplicate objects and wave lines. The
upper grid remains real geometry and continues to use GPU wave displacement.

## Controls

- Compartment Size on the test root: X/Z are the full cube footprint; Y is the
  height of one color region, so the complete cube is four times Y.
- Show Visibility Probes: off by default; enable before Play to create three
  diagnostic primitives per depth region. Nothing inside is created when off.
- Camera Position and Test Light Intensity affect only the test setup.
- LayeredTint: enables four-region color blending, 0 for ordinary single-preset
  water and 1 for the continuous cube. The preview sets this to 1 at runtime.
- MicroLayerColor, MesoLayerColor, MacroLayerColor, MegaLayerColor: palette from
  bottom to top. The preview initializes these from the four existing presets.
- LayerBlendWidth: 0.06 by default, the half-width of each color transition as a
  fraction of total height. It is clamped to 0.001?0.12 in the shader.
- WaterOpacity: body transparency; SurfaceOpacity: top transparency.
- DepthStrength: smooth depth darkening; VolumeEmissionStrength: subtle body tint.
- WaveAmplitude / WaveAmplitude2, WaveSpeed / WaveSpeed2, WaveFrequency /
  WaveFrequency2, and WaveDirection / WaveDirection2 control the top waves.
  Wavelength in world units is 2*pi/frequency.
- RippleScale, RippleSpeed, RippleStrength, RippleDirection and their second
  counterparts independently control the two normal patterns.
- Smoothness, SpecularStrength, SurfaceEmissionColor and SurfaceEmissionStrength
  control the top response. BoundaryEmissionStrength controls outer edges only;
  the preview reduces it to 0.25. Set it to zero to remove these highlights.

To tune the live cube in Play mode, inspect its generated runtime material.
Those edits do not persist; change the source presets or preview defaults for
persistent changes. Keep the cube horizontal with positive scale, preferably
unit scale. Generated children and meshes belong to GPGPWaterCompartment.

## Validation

Static graph/material checks and the existing wave-math checks passed.
The updated runtime C# scripts compiled with Unity's bundled compiler and this
project's Unity references. Updated rendering, shader compilation and stereo
appearance have not been verified by the agent in Unity.

Run Tools > GPGP > Validate Water Shader in the open Unity editor, then inspect
Play mode. Check that only the upper surface moves, all three color transitions
are smooth, and no internal planes remain. Enable diagnostic probes separately
when checking transparency. Standard transparent sorting still applies; this
front-face-only shader is intended for viewing the cube from outside.

Tools > GPGP > Capture Water Test Screenshot saves the Game view to
Logs/GPGP_WaterTest.png while the test scene is playing.

The existing tools/build_gpgp_water.py generator updates the graph and presets;
it resets preset water parameters, so do not rerun it over custom tuning.
Run python tools/validate_gpgp_water.py for static validation.
