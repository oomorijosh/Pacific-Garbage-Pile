# Cyber Canoe Headset Preview

Test your own Cyber Canoe scene at home on a virtual LAVA LED wall in a VR headset, then turn preview off to present in class.

This folder distributes the headset camera, virtual stereo screen, room materials, and setup tools. Use it with your existing class Cyber Canoe Unity project. It is an add-on, rather than a complete Unity project to open in Unity Hub.

**Supported setup:** Windows PC VR in Unity, using SteamVR or a Meta Quest connected through USB Link or Air Link. Standalone Quest Android builds require separate configuration.

## Download

[Download CyberCanoe-HeadsetPreview.unitypackage](Downloads/CyberCanoe-HeadsetPreview.unitypackage?raw=true)

You only need this small Unity package and the instructions below. Your own scene and the class's Cyber Canoe rig stay in your existing project.

## 1. Install the required XR packages yourself

**Install these before importing the Unity package or copying the source files.** XR packages are not bundled or installed automatically. The setup menu configures packages you have already installed.

Open **Window > Package Management > Package Manager**, choose **Install package by name**, and install these tested versions in order:

| Package | Name | Version |
| --- | --- | --- |
| XR Plug-in Management | `com.unity.xr.management` | `4.7.0` |
| OpenXR Plugin | `com.unity.xr.openxr` | `1.18.0` |

If they are already installed, check their versions. OpenXR's automatic dependencies alone do not guarantee XR Plug-in Management 4.7.0. Wait for installation and compilation to finish.

XR Plug-in Management starts the headset provider. OpenXR connects Unity to the headset runtime so it can display stereo images and track your head.

Your class project must already include its Cyber Canoe scripts and rig, an active Universal Render Pipeline (URP) asset, uGUI, and Input System. OpenXR installs Input System if missing, but its dependency version differs from the tested version below. Keep the class project's other dependencies.

| Tested baseline | Version |
| --- | --- |
| Unity Editor | `6000.6.0f1` |
| Universal Render Pipeline | `17.6.0` |
| uGUI | `2.6.0` |
| Input System | `1.20.0` |

[REQUIRED-PACKAGES.json](REQUIRED-PACKAGES.json) lists the tested package versions for reference. Preserve your project's existing manifest and other packages.

Under **Edit > Project Settings > Player > Other Settings**, set **Active Input Handling** to **Input System Package (New)** or **Both**. Restart Unity if prompted.

## 2. Import and set up your scene

1. Download the Unity package above.
2. In your class project, choose **Assets > Import Package > Custom Package** and import all assets. Wait for compilation.
3. Open your own scene containing the Cyber Canoe rig.
4. Choose **Tools > Cyber Canoe > Set Up Headset Preview for Current Scene** and save your scene.
5. Connect your headset and start its PC runtime. For Quest, start Meta Link and launch Quest Link in the headset. For SteamVR headsets, start SteamVR. Select that runtime as the active OpenXR runtime.
6. Press Play and wear the headset. If needed, use **Recenter Viewer at Calibrated Position** in the simulator Inspector during Play.

Setup finds `CC_HEAD` with `CC_CAMERA_SETUP` and two `CC_CAMERAOFFSET` cameras sharing a projection screen under `CC_HUB`. It connects those existing cameras to a separate headset room and preserves the scene's current calibration. Running setup again reuses the simulator.

For the supplied Keller 102 measurements, choose **Tools > Cyber Canoe > Apply LAVA LED Wall Calibration (Keller 102)**. This optional action changes the screen/head calibration and eye render texture sizes; it supports Undo.

Read the [full setup and troubleshooting guide](Assets/HeadsetPreview/README.txt) for calibration details and classroom presentation settings.

## How it works

The existing left and right Cyber Canoe cameras render the two wall images. A stereo material displays them on `VirtualCyberCanoeScreen`. A tracked `HeadsetCamera` views that virtual wall, sending the corresponding content image to each headset eye.

Moving your head changes your view of the virtual wall. Use your scene's existing controls to navigate the content. The preview does not add controller navigation or pointer interaction.

## Turn preview off for class

Stop Play, select `CyberCanoe_HeadsetSimulator`, and uncheck the checkbox beside the **Cyber Canoe Headset Simulator** component title. Save the scene. Keep global **Initialize XR on Startup** off in XR Plug-in Management.

The original Cyber Canoe camera settings are restored when preview stops. Keep the XR packages installed so the scripts continue to compile. To test at home again, enable the simulator component and its **Initialize XR** field, connect the headset, and press Play.

## Install from source instead

After installing the prerequisites, copy this folder's `Assets/HeadsetPreview` directory and `Assets/HeadsetPreview.meta` into your existing Unity project's `Assets` directory. Keep all `.meta` files and the exact destination `Assets/HeadsetPreview`; setup uses that path for materials.

Choose either source copying or Unity package import. This distribution uses ordinary Unity assets; it is not a Package Manager Git URL package.

## Update the download after editing

In a Unity project containing this add-on, choose **Tools > Cyber Canoe > Export Headset Preview Add-on**. Replace this folder's `Downloads/CyberCanoe-HeadsetPreview.unitypackage` with the exported package, and update the matching source assets and `.meta` files.

The exporter includes only the reusable preview assets and excludes generated project XR settings. The included [import validation report](IMPORT-VALIDATION.txt) records the isolated Unity import and setup checks for this version; it does not claim physical headset testing.
