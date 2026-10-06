Steam Frame desktop camera preview
==================================

For wearing your real headset as a virtual Cyber Canoe screen, use
Tools > Cyber Canoe > Add Headset Screen Simulator instead. See
CyberCanoe_Headset_Guide.txt in this folder for that workflow.

Use this desktop preview to inspect your scene from a simulated headset position,
similar to using CC_HEAD for the Cyber Canoe. It works in Edit mode and Play mode
without SteamVR or additional XR packages. Enter Play mode to see animations and
other runtime behavior.

Getting started
---------------
1. Open Tools > Headset Preview > Steam Frame Preview.
2. Use Tools > Headset Preview > Add Steam Frame Head to add a SteamFrame_HEAD
   to the scene. You can also drag the SteamFrame_HEAD prefab into your scene.
3. Place the head at CC_HEAD or the current camera. The preview window has
   buttons to align the head with CC_HEAD or the Scene view.
4. Move and rotate SteamFrame_HEAD in the Scene view, or use the controls below.
5. Select Left Eye, Right Eye, or Side by Side to inspect the eye images.

Preview controls
----------------
Hold the right mouse button and drag inside the preview to look around.
Arrow keys move horizontally. Page Up and Page Down move vertically.
Hold Shift to move faster. Home resets the head pose.
These controls use arrow keys so they do not conflict with Cyber Canoe's WASD
shortcuts.

Preview settings
----------------
Eye preview resolutions are 540 x 540, 1080 x 1080, and 2160 x 2160.
The default eye separation (IPD) is 64 mm and can be adjusted from 60 to 70 mm.
The default Unity vertical field of view is 100 degrees and is adjustable.
This field of view is an approximation: Valve advertises up to 110 degrees but
does not specify a field-of-view axis in the cited product specifications.

SteamFrame_HEAD is tagged EditorOnly. Its left and right cameras are disabled
for normal rendering and are rendered manually by the preview window.
The head is a scene preview tool and is excluded from player builds.

What the preview shows
----------------------
Each eye sees scene geometry and world-space UI. Cyber Canoe's screen-space
desktop compositor is excluded from the eye images.
Use the preview to check placement, scale, stereo separation, and sight lines.
It does not simulate Steam Frame controllers, exact lens optics or runtime eye
projections, streaming quality, or performance on the headset.

Steam Frame specifications and device testing
--------------------------------------------
Valve lists a 2160 x 2160 LCD panel per eye, a field of view up to 110 degrees,
and a 60-70 mm IPD target range:
https://store.steampowered.com/hardware/steamframe

Steam Frame supports SteamVR and OpenXR:
https://partner.steamgames.com/doc/steamhardware/steamframe

For actual-device development, Valve recommends Unity's OpenXR plugin. Its
Unity guide describes desktop Play-in-Editor testing through SteamVR and
deployment to Steam Frame:
https://partner.steamgames.com/doc/steamhardware/steamframe/engines/unity
