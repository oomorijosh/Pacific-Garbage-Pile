# Great Pacific Garbage Patch — Layered Cube Visualization

## Project brief for Codex

Build a **Unity URP** scene for an educational **CyberCANOE large-screen, stereoscopic 3D visualization** of the Great Pacific Garbage Patch (GPGP). The centerpiece is **one rectangular, translucent water cube**, visually subdivided into four horizontal layers. Each layer represents a *plastic size category* and its approximate **share of the patch's estimated floating plastic mass**, not an actual ocean-depth layer.

The overall visual should resemble a museum installation: a dark room, restrained cinematic lighting, a slightly reflective floor, a human figure next to the cube for scale, and debris suspended within gently moving translucent water. The plastic and water should appear as parts of **one continuous object**, not four separate cubes stacked with visible gaps.

## Important interpretation

- This is a **data sculpture**, **not** a literal 1:1 reconstruction of the GPGP, a solid island of trash, or a vertical cross-section of the real ocean.
- In the ocean, these four size categories **coexist and mix**. Their separation into bands in this scene is only an educational visual convention.
- Layer thickness encodes estimated **mass share**, **not** the physical volume of waste or the ocean depth where that category occurs.
- The human figure is a scale reference for the *display sculpture only*. Do not claim that the display cube is the real-world volume of the patch.
- The figures below are rounded estimates from a **2018 study**, not live measurements or current totals.

## The four data layers

Use the following order **from bottom to top** for the installation. This order is for legible presentation only, not a claim about buoyancy or depth.

| Layer | Category | Plastic size | Approx. floating mass | Share of total mass | Example 3D props |
|---|---|---|---:|---:|---|
| L1 (bottom) | Microplastics | 0.5–5 mm | 6,400 metric tonnes | 8% | Tiny fragments, pellets, thread-like bits |
| L2 | Mesoplastics | 5–50 mm | 10,000 metric tonnes | 13% | Bottle caps, small broken fragments, short fishing-line pieces |
| L3 | Macroplastics | 5–50 cm | 20,000 metric tonnes | 25% | Bottles, containers, packaging, broken fishing gear |
| L4 (top) | Megaplastics | >50 cm | 42,000 metric tonnes | ~54% | Large tangled fishing nets, ropes, buoys, crates, containers |

**Rounding note:** The published categories are approximate; the individual rounded percentages often appear as 8%, 13%, 25%, and 53% (99% total). Use **54% for the visual height allocation only** so all four bands fill 100% of the cube. Do not present that adjusted 54% as a measured scientific result; show `~53%` in the on-screen factual label, or derive normalized heights directly from the four source masses.

**Example at a 10 m display height:** L1 = 0.8 m, L2 = 1.3 m, L3 = 2.5 m, L4 = 5.4 m, if using the illustrative 8/13/25/54 split. Parameterize total display height rather than hardcoding 10 m.

## Scene and hierarchy

Create a sensible, editable hierarchy, for example:

```text
GPGP_Visualization
├── WaterCube
│   ├── OuterVolume / shared boundaries
│   ├── WaterSurface (one top surface, subtle ripples)
│   ├── L1_Microplastics
│   │   ├── WaterBand
│   │   └── Debris
│   ├── L2_Mesoplastics
│   │   ├── WaterBand
│   │   └── Debris
│   ├── L3_Macroplastics
│   │   ├── WaterBand
│   │   └── Debris
│   └── L4_Megaplastics
│       ├── WaterBand
│       └── Debris
├── ScaleHuman
├── InformationLabels
├── Floor
├── Lighting
└── CameraRig
```

Each layer should be a child of the **same cube**, cover the same X/Z footprint, and fit precisely against the layer above and below it. Avoid opaque internal walls, air gaps, visible intersecting surfaces, or four independent top water surfaces.

## Visual requirements

1. **Water:** A readable, softly tinted, semitransparent continuous cube. It should clearly show debris inside. Use subtle animated distortion or ripples rather than strong waves that obscure the data. Put the principal ripple animation on the **single top surface**; optional weak internal movement can make the water feel alive.
2. **Bands:** Use small differences in tint, haze, or edge illumination to distinguish layers without making them look like disconnected blocks. A fine separator line at each mass-share boundary is acceptable.
3. **Debris:** Each band contains props from its category, with visibly different sizes. Microplastics can be GPU-instanced particles or simple meshes; larger debris can use low-poly meshes/prefabs. Props should be distributed inside the appropriate band's bounds and should not clip the sides or cross bands by default.
4. **Animation:** Gentle, slow bobbing, drifting, and rotation. Avoid large per-frame CPU loops, unnecessary physics, garbage allocations, or noisy motion. Favor deterministic placement from a configurable seed so the scene is reproducible.
5. **Environment:** Dark gallery space, restrained highlights, reflective-looking floor, and a neutral human-scale figure next to (not inside) the cube. Keep text and debris readable on the CyberCANOE's large stereoscopic display.
6. **Information:** Each band has a simple toggleable label: category, size range, estimated mass, and approximate mass percentage. Include a prominent disclaimer: `Illustrative mass-share visualization; not an actual ocean-depth cross-section.` A separate source note identifies the 2018 study.

## Technical requirements

- Use **Unity URP** and existing project assets where practical. Inspect the current project structure, Unity version, render pipeline, shaders, and scene conventions **before** changing or creating anything.
- Prefer built-in Unity meshes and URP Shader Graph/materials where feasible. Keep the solution easy to edit later; expose cube dimensions, transparency, layer distribution, particle/prop density, animation intensity, random seed, and label visibility in the Inspector.
- Keep each layer's debris group independently controllable and replaceable with future FBX assets. Do not bake all props into one mesh or create an FBX as a prerequisite.
- Handle URP transparent-render sorting, depth writing, and nested transparent surfaces carefully. Ensure the rear bands and debris remain visible through the front face. Avoid z-fighting at touching band boundaries.
- Use an existing supported reflection method in the project (for example, an appropriately configured planar-reflection solution or a subtle reflection-probe/fake reflection). Do **not** assume hardware ray tracing is available in URP.
- Design for real-time display performance, with configurable debris counts and an inexpensive low-quality mode.
- Avoid changing unrelated project settings, packages, or scenes.

## Implementation sequence

Work in small verifiable stages:

1. Inspect the project and identify the correct scene, URP setup, existing assets, and any working water shader. Summarize what you found and which files you intend to touch.
2. Create **one correctly sized transparent cube** with a single animated top water surface. Verify it looks like one volume from the intended camera.
3. Add the four contiguous internal bands, calculated from data-driven mass shares. Verify they occupy the whole cube with no gaps or overlaps.
4. Add category-specific debris groups, starting with placeholder meshes/particles; expose counts and asset references for later replacement.
5. Add the human-scale figure, lighting, floor reflection, and optional labels.
6. Check transparency/depth sorting from the main camera, edit/play-mode behavior, and performance. Report exactly what was implemented and any remaining limitations.

**Do not jump ahead or rewrite the entire project at once. Start by inspecting the current Unity project and implementing Stage 1 or the smallest agreed first step.**

## Source and provenance

Lebreton, L. et al. (2018), *Evidence that the Great Pacific Garbage Patch is rapidly accumulating plastic*, **Scientific Reports** 8, article 4666. https://doi.org/10.1038/s41598-018-22939-w

The source study estimates approximately **79,000 metric tonnes** of floating plastic in an approximately **1.6 million km²** area at the time of analysis. Treat these as study-era estimates; cite them in the display rather than representing them as a live measurement.
