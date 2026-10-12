CYBER CANOE - HEADSET PREVIEW ADD-ON

Test your own Cyber Canoe scene at home on a virtual LAVA display, then turn
preview off to present on the physical LED wall in class.

This add-on contains the headset viewer, virtual stereo screen, room materials,
and setup/calibration tools. It does not include the Pacific garbage exhibit,
student content, a replacement Cyber Canoe rig, or saved project settings.
The setup tool connects to the Cyber Canoe cameras already in your scene.

BEFORE IMPORT

Use your class's Cyber Canoe Unity project with Universal Render Pipeline (URP).
The tested editor is Unity 6000.6.0f1. The class's Cyber Canoe package must already
be installed: CC_CAMERA_SETUP, CC_CAMERAOFFSET, CC_SETTINGS and CC_CANVAS_SETUP
are required. This add-on leaves those source files and their GUIDs alone.

You must install the Unity XR packages yourself before importing this add-on.
They are not included in the .unitypackage and are not installed by the setup
tool. XR Plug-in Management starts the headset provider; OpenXR connects Unity
to the headset runtime so it can display stereo images and track your head.

1. Open Window > Package Management > Package Manager. Use Install package by
   name to install each missing XR package with these tested versions:
     XR Plug-in Management: com.unity.xr.management    4.7.0
     OpenXR Plugin:         com.unity.xr.openxr        1.18.0
   Install XR Plug-in Management first, then OpenXR Plugin. If they are already
   installed, check their versions. OpenXR also installs its package
   dependencies, including Input System. Wait for installation to finish.
   OpenXR alone does not guarantee XR Plug-in Management 4.7.0 or the tested
   Input System 1.20.0, so check those versions separately.

   Your class project should already contain URP, uGUI and Input System. The
   tested versions are URP 17.6.0, uGUI 2.6.0 and Input System 1.20.0; install
   any missing packages by name:
     com.unity.render-pipelines.universal
     com.unity.ugui
     com.unity.inputsystem
   REQUIRED-PACKAGES.json lists all five tested package versions for reference.
   Keep the project's other packages; do not replace its Packages/manifest.json
   with this dependency list.
2. Under Edit > Project Settings > Player > Other Settings, set Active Input
   Handling to Input System Package (New) or Both. Restart Unity if prompted.
3. Use Assets > Import Package > Custom Package to import
   CyberCanoe-HeadsetPreview.unitypackage. Import all its assets, then wait for
   compilation. All installed files belong to Assets/HeadsetPreview.

ADD PREVIEW TO YOUR SCENE

1. Open your own scene containing the Cyber Canoe rig.
2. Choose Tools > Cyber Canoe > Set Up Headset Preview for Current Scene.
   It finds CC_HEAD's CC_CAMERA_SETUP and the two CC_CAMERAOFFSET cameras that
   share a projection screen under CC_HUB. It creates a separate headset room,
   connects the cameras and stereo material, and configures the installed
   desktop OpenXR packages. This menu does not download or install packages.
   Running it again reuses the existing simulator. Save your scene afterwards.
3. Adding preview preserves your current screen dimensions, CC_HEAD position,
   eye render texture sizes and HDR choice. If the scene needs the supplied
   Keller 102 LED wall measurements, choose:
   Tools > Cyber Canoe > Apply LAVA LED Wall Calibration (Keller 102).
   This deliberately changes the rig calibration in your current scene and
   supports Undo. It does not move your scene content or CC_HUB navigation.
4. Connect the headset before pressing Play:
   - SteamVR headset: start SteamVR and select it as the active OpenXR runtime.
   - Meta Quest: use the Windows Meta Link PC app with USB Link or Air Link;
     set Meta Link as its active OpenXR runtime and launch Quest Link.
5. Press Play and wear the headset. The first tracked pose places you at the
   calibrated viewer position. Use Recenter Viewer at Calibrated Position in
   the simulator Inspector during Play if the starting view needs resetting.

This is Windows PC VR preview in Unity, including Quest connected to a PC.
The package does not configure standalone Android Quest applications.
Use your scene's existing navigation controls. Headset/controller navigation
and pointer interaction are not added by the preview tool.

HOW THE CAMERAS AND SCREEN WORK

Your scene's left/right Cyber Canoe cameras render two full-wall eye images.
The simulator routes those images into a material on VirtualCyberCanoeScreen.
A third, tracked HeadsetCamera views that screen from inside Headset_Room.
The screen shader sends the appropriate content image to each headset eye.

Moving your head changes your view of the virtual wall. The Cyber Canoe content
viewpoint remains calibrated to the classroom viewer, while your existing
navigation controls move the scene. Keep CyberCanoe_HeadsetSimulator outside
CC_HUB so navigation cannot move the room around your head. Keep rig parents
at scale (1, 1, 1); one Unity unit represents one metre.

The saved Headset_Room stays inactive until preview starts. Do not enable it
manually. A dedicated CC_HeadsetRoom user layer isolates it from scene content.
The setup tool uses an existing layer with that name or reserves an unused one.

LAVA LED WALL PRESET (KELLER 102)

The supplied calibration matches the classroom XML provided with this project:
  Physical wall:      6.047 x 2.042 metres
  Wall centre:       (0, 1.75, 2.282) relative to CC_HUB
  Head centre:       (0, 1.76, -2) relative to CC_HUB
  Viewing distance:  4.282 metres, 0.01 metre above wall centre
  Eye separation:    0.061 metre, normal side-by-side stereo
  Each eye image:    4800 x 1620 pixels covering the entire physical wall
  Combined output:   9600 x 1620 pixels

The preset requires existing CC settings to use 61 mm eye separation, normal
eye order and side-by-side output; it checks those settings before changing
geometry. The optional older 4080 x 1152 / 20 ft preset remains available for
other displays. Use the Keller 102 preset for the supplied LED wall.

Assets/HeadsetPreview/Calibration/CCUnityConfig.xml contains the supplied
physical-wall configuration. The Unity Editor does not load that XML; apply
the calibration menu to match its geometry during headset preview. Follow
your class's Cyber Canoe build/configuration instructions for the real wall.
In this Cyber Canoe runtime, a local CCUnityConfig.xml beside the executable
is used when the application is launched with that folder as its working
directory. Other classrooms/displays require their own calibration.

TURN PREVIEW OFF FOR CLASS

Stop Play, select CyberCanoe_HeadsetSimulator, and uncheck the checkbox beside
the Cyber Canoe Headset Simulator component's title. Save the scene.
Keep global Initialize XR on Startup unchecked in XR Plug-in Management.
Your original Cyber Canoe cameras, canvas, render targets and head transforms
are restored when preview stops. The room is hidden and ordinary wall output
can run without a headset. Keep the add-on and XR packages installed so the
scene's scripts continue to compile. To preview at home again, enable the
component and its Initialize XR field, connect the headset, and press Play.

TROUBLESHOOTING

- Missing CC_* types: import the class's Cyber Canoe package first.
- Missing Unity XR/Input System types: install the packages listed under
  BEFORE IMPORT yourself, then wait for Unity to finish compiling.
- Setup cannot find a source rig: CC_HEAD needs CC_CAMERA_SETUP with its
  settings and left/right cameras assigned; both cameras need CC_CAMERAOFFSET
  pointing at the same projection screen, under an ancestor named CC_HUB.
- No headset image: check its PC connection, active OpenXR runtime, simulator
  component checkbox and Initialize XR field. Read Status and the Console.
- Preview is slow: reduce both per-eye texture dimensions while preserving
  their pixel aspect ratio. Keep the physical wall size and viewing distance.
- Headset starts in class mode: keep global Initialize XR on Startup off and
  disable the simulator in the scene you are actually running or building.
- Pink room/screen: this add-on requires an active URP asset. Keep your class
  project's URP pipeline; the setup tool does not replace its rendering assets.

Headset preview helps assess scale, framing, stereo depth and label readability.
Check final brightness and stereo appearance on the real wall before class.

SHARE UPDATED ADD-ON FILES

Choose Tools > Cyber Canoe > Export Headset Preview Add-on.
Share Artifacts/CyberCanoe-HeadsetPreview-Unity.zip. The exporter includes only
Assets/HeadsetPreview tools/materials/docs/calibration, excluding any generated
project XR settings. It does not export your scene or content.

Meta Link / Unity Play mode:
https://developers.meta.com/vr/documentation/unity/unity-link/
Unity asset packages:
https://docs.unity.com/en-us/engine/6000.5/manual/assets-and-media/asset-packages/create
