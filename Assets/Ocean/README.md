Open `Assets/Scenes/CC_LAVA_LED_WALL_SCENE.unity` and enter Play mode. The Ocean uses Ocean_Material and Ocean_Shader, with a generated 128 x 128 grid and expanded bounds for waves. Its flat MeshCollider is disabled because it cannot follow GPU displacement.

Adjust Wave_Height, Water_Color, Water_Smoothness and Water_Alpha on Ocean_Material. Wave_Height is amplitude in local units; peak-to-trough height is twice that. The wave is y = Wave_Height * sin(local x + local z + time). Object scale changes wavelength; keep the ocean horizontal. The graph computes the corresponding vertex normal and layers scrolling ripple normals over it.

Add FloatingDebris to garbage objects and assign Ocean to its ocean field (or it finds an active OceanSurface). Adjust waterlineOffset to place the object's waterline at the surface. Existing rigidbodies become kinematic while enabled; this is surface-following animation, not force-based buoyancy. OceanSurface controls circulation speed, convergence rate and accumulation radius in world units. This is an illustrative current field, not a calibrated Pacific circulation model. Small noise ripples affect lighting only, not buoyancy.

The ocean scene is first in Build Settings. SampleScene remains included. The existing display-wall camera setup is retained.

`Ripple_Speed` on Ocean_Material controls the small surface ripples: 0 freezes them, 0.15 is the slower default, and 1 restores the original speed. It does not change large waves or debris movement.

The scene contains 60 editable thin square blocks under `Floating Debris Placeholders`. They range from 0.4 to 2.4 world units wide, use four shared placeholder materials, and each has FloatingDebris assigned to Ocean. They are visible before Play and begin bobbing, tilting, and drifting during Play. Move, resize, duplicate, or delete individual blocks in the Hierarchy as needed. They do not collide with one another and may overlap as the current accumulates them.
