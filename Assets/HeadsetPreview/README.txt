CyberCanoe VR mode

Use a headset at home to view this project's scene on a virtual CyberCanoe
wall, then turn headset preview off to present on the physical LAVA display.
Each headset eye sees the corresponding CyberCanoe stereo image. Moving your
head changes where you view the virtual wall from; the existing CyberCanoe
navigation moves the content while the virtual room stays stationary.

This guide covers Windows PC headset preview in the Unity Editor. Quest 2,
Quest 3, Quest 3S and Quest Pro can use Meta Link. SteamVR headsets can use
SteamVR. The project already includes OpenXR and the desktop OpenXR loader.
Running independently on a Quest requires a separate Android configuration.

Requirements

- Open the project with the Unity version in ProjectSettings/ProjectVersion.txt.
- Use a Windows PC that meets the requirements of your headset's PC runtime.
- Connect the headset through Meta Link or SteamVR before entering Play mode.
- Use a scene containing the CyberCanoe rig, such as
  Assets/Scenes/Plastic_Type.unity.

One time Unity setup

Stop Play mode before changing these settings.

1. Open Edit > Project Settings > XR Plug-in Management.
2. On the Windows, Mac, Linux / desktop tab, keep the OpenXR provider enabled.
3. Uncheck Initialize XR on Startup. This lets the simulator manage headset
   startup and shutdown when its component is enabled.
4. Open Assets/Scenes/Plastic_Type.unity, or your own CyberCanoe scene.
5. Choose Tools > Cyber Canoe > Add Headset Screen Simulator.
   If it already exists, this selects it without creating another one.
6. Select CyberCanoe_HeadsetSimulator in the Hierarchy. In the Inspector,
   enable the checkbox beside the Cyber Canoe Headset Simulator component's
   title and check its Initialize XR field under Runtime.
7. Save the scene.

Initialize XR controls whether the simulator can start a headset session.
The checkbox beside the component title controls whether VR preview runs.
Use the component checkbox to switch modes. Unchecking Initialize XR alone
does not turn off preview if a headset session is already running.

The saved project settings may differ from this setup. In particular, global
XR startup can be enabled while a scene's Initialize XR field is unchecked.
Check both settings when using the project for the first time.

Meta Quest connection

1. Install and open the Meta Link PC app, also called Meta Quest Link or
   Meta Horizon Link. Connect the Quest through USB Link or wireless Air Link.
2. In the PC app, open Settings > General. Under OpenXR Runtime, set Meta Link
   as the active runtime.
3. Wear the headset and launch Quest Link so the PC connection is active.
4. Continue with Turn VR on below.

This route uses Meta's OpenXR runtime. Some existing Inspector help text
mentions SteamVR; that text also applies to Quest when Meta Link supplies the
runtime. Headset viewing uses the project's existing OpenXR packages.
Quest controller navigation is not configured by this simulator; use the
scene's existing keyboard, gamepad, Space Mouse or waypoint controls.

Meta's Link setup and Unity Play mode instructions:
https://developers.meta.com/vr/documentation/unity/unity-link/

SteamVR connection

1. Connect the headset to its PC runtime and start SteamVR.
2. Confirm SteamVR detects the headset and is set as the active OpenXR runtime
   in its settings.
3. Continue with Turn VR on below.

Valve's Unity and OpenXR instructions:
https://partner.steamgames.com/doc/steamhardware/steamframe/engines/unity

Turn VR on at home

1. With Play mode stopped, select CyberCanoe_HeadsetSimulator.
2. Check the box beside the Cyber Canoe Headset Simulator component's title.
   Keep its Initialize XR field checked and global Initialize XR on Startup
   unchecked, as described in One time Unity setup.
3. Start the headset connection using the appropriate section above.
4. Press Play in Unity and wear the headset. You should see a stationary room
   and a virtual wall showing the scene in stereo.
5. Look toward the wall. The first tracked headset pose sets your starting
   viewpoint. For a new starting direction, face the desired direction and
   click Recenter Viewer at Calibrated Position in the simulator's Inspector.

The Inspector's Status field reports startup, tracking and connection errors.
The room starts inactive in the saved scene and is activated by the simulator;
you do not need to activate Headset_Room manually.

Turn VR off for desktop or LAVA wall output

1. Stop Play mode.
2. Select CyberCanoe_HeadsetSimulator and uncheck the box beside the Cyber
   Canoe Headset Simulator component's title.
3. Confirm Initialize XR on Startup remains unchecked in Project Settings.
4. Save the scene, then enter Play mode or build for Windows.

Keep CC_HUB, CC_HEAD, both CyberCanoe eye cameras and the stereo output canvas
in place. Disabling the simulator restores its original camera, head and
canvas settings and hides the headset room. CyberCanoe stereo wall rendering
continues without a headset. Keep the OpenXR provider installed and enabled
so you can switch back to VR later.

Stopping Play mode ends the current preview session. Disable the component
before pressing Play again when you want to stay in wall mode. With global XR
startup enabled, disabling the simulator can leave the headset runtime active.

Prepare a wall presentation build

- In File > Build Profiles, check the Scene List for your Windows build.
  Include the intended presentation scene, such as Plastic_Type, and disable
  headset simulation in every included scene that contains a simulator.
  The project can still point to the original CC_LAVA_LCD_WALL_SCENE demo;
  opening another scene does not automatically change the build list.
- For the Planar Direct Light Pro LED display in Keller 102, use the supplied
  Presentation/Keller102/CCUnityConfig.xml. This is the lab configuration
  provided for this exhibit: 9600 x 1620 side-by-side output, 61 mm eye
  separation, a 6.047 x 2.042 metre wall, and a viewer 4.282 metres from it.
  Other physical displays need their own calibration.
- A built application loads CCUnityConfig.xml from its working directory
  first. Launching from the executable's folder lets you keep a local config
  beside the executable. It overrides the shared configuration path in
  Assets/CC_ALL/CC/CC_SETTINGS.asset, normally C:\CCUnityConfig.xml.
  Without either file, the built application uses its saved scene settings.
- XML calibration is applied by the built application, not by the Unity
  Editor. Match the scene's preview geometry to the intended lab calibration
  when you need the home preview to represent that display.

Match the Keller 102 LavaLab LED wall

1. Stop Play mode and keep the scene you want to present open.
2. Choose Tools > Cyber Canoe > Apply LavaLab LED Wall Calibration (Keller 102),
   or click the matching button on CyberCanoe_HeadsetSimulator.
3. Save the scene. The action updates the projection screen, CC_HEAD and the
   headset room in the current scene. It supports Undo and leaves the VR mode
   switch and Initialize XR setting as you set them.
4. At home, enable the simulator for headset preview as described above.
   Before the lab presentation, disable it and save as described in Turn VR off.
5. Build the intended scene for Windows. Copy
   Presentation/Keller102/CCUnityConfig.xml beside the built .exe and launch
   from that folder. For a shortcut, set Start in to the executable's folder.

Each eye renders 4800 x 1620 pixels and covers the full 6.047 x 2.042 metre
wall. The combined output is 9600 x 1620, with normal eye order. The wall
centre is (0, 1.75, 2.282) metres and the head centre is (0, 1.76, -2) metres.
Fullscreen stays 0 as supplied; the existing CC runtime positions its wall
window at (0, 0). The XML is applied in a built app. The menu/Inspector action
applies equivalent geometry and eye texture sizes for Unity headset preview.
Calibration preserves the exhibit and CC_HUB navigation placement. Use the
existing navigation controls to frame the content. The current Plastic_Type
view crops the tall exhibit vertically; showing every layer at once requires
a more distant viewing position in the scene while retaining these physical
wall/head calibration values.

Adjust the preview calibration

Keep an existing scene's calibration unless you intend to change it.
The older Plastic_Type preview used a 6.1887 x 1.75 metre screen, centred at
1.85 metres high, viewed from 6.096 metres (20 feet), with 2040 x 1152 pixels
per eye. Apply the Keller 102 action above to match the supplied LED wall.

With Play mode stopped, select the simulator to adjust Viewing Distance Meters,
Viewer Offset Meters, Per-Eye Texture Width and Per-Eye Texture Height.
Select Virtual Screen to Adjust Size selects the virtual wall for editing.
Keep CC_HUB and headset rig parents at scale (1, 1, 1) so units represent metres.
Each eye image covers the whole wall; do not halve the wall's physical width.

The optional Apply 4080 x 1152 SBS / 20 ft Center Calibration button resets
eye texture sizes to 2040 x 1152, distance to 6.096 metres and viewer offsets
to zero, then repositions the initial viewer. It overwrites custom preview
settings. It does not measure the wall or turn VR on. For this presentation,
use the Keller 102 action instead of the older 20-foot calibration button.

Troubleshooting

- No headset image: verify the connection inside the headset, the active
  OpenXR runtime, the desktop OpenXR provider, the simulator component checkbox
  and its Initialize XR field. Read Status and Unity's Console for the error.
- Simulator disabled itself: a startup failure automatically disables the
  component. Stop Play, fix the runtime or connection, enable the component
  again, and press Play to retry.
- Waiting for headset tracking: wear the connected headset and let its runtime
  acquire tracking. Recenter waits for a tracked pose.
- Wrong viewing direction: face the desired forward direction and use
  Recenter Viewer at Calibrated Position during Play mode.
- Room visible but wall blank: check the simulator's left and right content
  camera references, source screen, screen material, and any Console errors.
- Preview is slow: reduce both per-eye texture dimensions while preserving
  their pixel aspect ratio. Keep the physical wall size unchanged.
- Headset starts in wall mode: stop Play and confirm global Initialize XR on
  Startup and the simulator component are both off in the scene being run.
- Built app shows the wrong scene or layout: check the build Scene List and
  the local/shared CCUnityConfig.xml used when launching the application.

Use the headset preview to check scene scale, framing, stereo depth and label
readability. Confirm final brightness, viewing limits and stereo appearance
on the physical LAVA display before presenting.
