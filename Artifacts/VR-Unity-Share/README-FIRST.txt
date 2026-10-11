PACIFIC GARBAGE PILE - UNITY VR SHARE

This package contains the complete Plastic_Type exhibit scene, the connected
Cyber Canoe cameras and virtual screen, the headset room, the UI, models,
materials, source scripts, and the imported-project setup command.

Use Unity 6000.6.0f1 on Windows for the same configuration as the authoring
project. Recipients need Unity and a Windows OpenXR headset connection.
This is an Editor project package, not an executable or a standalone Quest APK.

OPEN IN A FRESH PROJECT

1. Create a new Universal 3D project in Unity Hub using Unity 6000.6.0f1.
   Close its Editor after creation.
2. Copy this share folder's Packages/manifest.json into the new project's
   Packages folder, replacing the fresh template's manifest.json.
   This step is intended for a new project. It installs URP 17.6.0,
   Input System 1.20.0, uGUI/TMP 2.6.0, XR Management 4.7.0,
   OpenXR 1.18.0, and glTFast 6.19.0, with their dependencies.
3. Open the new project and let Package Manager finish downloading packages.
   Accept the Input System backend change/restart if Unity requests it.
   Under Edit > Project Settings > Player > Other Settings, Active Input
   Handling must be Input System Package (New) or Both. Restart if changed.
4. Choose Assets > Import Package > Custom Package, select
   Pacific-Garbage-Pile-VR.unitypackage, and import all checked assets.
   Wait for model/shader import and script compilation to finish.
5. Choose Tools > Pacific Exhibit > Prepare Imported VR Demo.
   This opens Plastic_Type, registers its desktop OpenXR settings, assigns
   the PC URP pipeline, uses Linear colour, names the headset room layer,
   enables headset startup, and adds the scene to the build list.
   Save the scene when setup finishes.
6. Start the headset connection before pressing Play:
   - Steam Frame / SteamVR: start SteamVR and make it the active OpenXR runtime.
   - Meta Quest: connect through Meta Horizon Link (USB Link or Air Link),
     launch Quest Link, and set Meta Link as the active OpenXR runtime under
     the PC app's Settings > General.
7. Press Play and wear the headset. The first tracked pose positions you at
   the calibrated viewing location, facing the virtual exhibit screen.

THE CONNECTED CAMERA RIG

Keep the scene intact for the first test. No camera wiring is required.

CC_HUB / CC_HEAD contains CC_LEFT_CAMERA and CC_RIGHT_CAMERA. They render the
exhibit from the two calibrated eye positions. The projection screen defines
the size and shape of the physical display being simulated.

CyberCanoe_HeadsetSimulator is a separate scene root. Its Headset_Room contains
VirtualCyberCanoeScreen and TrackingSpace / HeadsetCamera. At runtime the
simulator creates a render texture for each content camera and assigns both
to the virtual screen material. The tracked headset camera sees that screen
in the room, with the correct image delivered to each headset eye.

Keep the headset room outside CC_HUB: exhibit navigation can move the
content without moving the room around your head. Keep rig parent scales at
(1, 1, 1). The supplied screen placement and 4.282-metre viewing calibration
are already saved. Each content eye renders 4800 x 1620 with HDR.

CONTROLS AND MODE SWITCH

Use the existing PC gamepad for exhibit navigation, or the supported PC
navigation controls. Quest Touch navigation and pointer interaction are not
configured in this package. Head tracking works through OpenXR.

Select/Back/View on a PC gamepad, or Tab on the keyboard, hides/restores the
exhibit UI. Start/Menu restores hidden panels. D-pad/Start browses the UI.
In Play mode, select CyberCanoe_HeadsetSimulator and use Recenter Viewer at
Calibrated Position in its Inspector if the starting view needs recentering.

For physical-wall mode, stop Play, disable the Cyber Canoe Headset Simulator
component, and save. Keep global Initialize XR on Startup off. For VR mode,
enable the component and its Initialize XR field. The setup command prepares
VR mode without changing the saved screen geometry or exhibit placement.

TROUBLESHOOTING

- Red compilation errors after import usually mean a required package was
  not installed first. Check the manifest step and let Package Manager finish.
- Pink materials: run Prepare Imported VR Demo and check that the exported
  PC_RPAsset is assigned in Graphics and the active Quality level.
- No headset image: check the active OpenXR runtime, wear the connected
  headset, and inspect the simulator's Status in Play mode and the Console.
- If layer 31 is already occupied by a different layer, use a fresh project.
  The setup command deliberately leaves an existing conflicting layer alone.
- Preserve both CC content cameras, the projection screen, simulator, room,
  virtual screen, and headset camera. Deleting one breaks the saved references.

EXPORT AN UPDATED COPY

Save your changes, then choose Tools > Pacific Exhibit > Export VR Unity
Share Package. Share Artifacts/Pacific-Garbage-Pile-VR-Unity.zip in full.
It includes the .unitypackage, dependency manifest, version and this guide.

ATTRIBUTION

Source credit and license notices are included with the assets. In particular,
Assets/Wildlife/SeaTurtle/ATTRIBUTION.txt documents the DigitalLife3D turtle
under CC BY-NC 4.0 (attribution and non-commercial use). Preserve these notices
when sharing. Other supplied assets retain their original notices.

REFERENCES

Meta Link / Unity Play mode:
https://developers.meta.com/vr/documentation/unity/unity-link/
Unity asset package export:
https://docs.unity.com/en-us/engine/6000.5/manual/assets-and-media/asset-packages/create
