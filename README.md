# PlasmaVR

A Virtual Reality app for visualising plasma physics simulation data on a Meta Quest headset.  
You can navigate, pause, scrub through frames, toggle particle/isosurface/streamline renderers, and take screenshots — all from inside the headset.

---

## Installing the App on Your Headset

The app is distributed as an APK file: **`PlasmaVR.apk`**

You need to sideload it onto your Quest using a USB cable.

### Windows

1. Download and install **Meta Quest Developer Hub** from [developer.oculus.com](https://developer.oculus.com/downloads/package/oculus-developer-hub-win)
2. Connect your headset with a USB cable and put it on — accept the "Allow USB Debugging" prompt inside the headset
3. Open Meta Quest Developer Hub, go to **Device Manager → My Device**
4. Drag and drop `Server/PlasmaVR.apk` onto the window, or use **Install APK** and browse to it
5. Wait for the installation to complete

### macOS

1. Download and install **SideQuest** (Easy Installer) from [sidequestvr.com](https://sidequestvr.com/setup-howto)
2. Follow the SideQuest setup guide to enable Developer Mode on your headset
3. Connect your headset with a USB cable — accept the "Allow USB Debugging" prompt inside the headset
4. In SideQuest, click the **Install APK from folder** button (box with an arrow icon, top-right)
5. Browse to `PlasmaVR.apk` and open it — SideQuest will install it automatically

### Linux

1. Download **SideQuest** (Easy Installer) from [sidequestvr.com](https://sidequestvr.com/setup-howto) — choose the Linux AppImage
2. Follow the SideQuest setup guide to enable Developer Mode on your headset
3. Connect your headset with a USB cable — accept the "Allow USB Debugging" prompt inside the headset
4. In SideQuest, click the **Install APK from folder** button (box with an arrow icon, top-right)
5. Browse to `PlasmaVR.apk` and open it — SideQuest will install it automatically

---

## Running the App

After installing, put on your headset and:

1. Go to **Apps** in the Quest home menu
2. In the top-right filter, select **Unknown Sources**
3. Find and launch **PlasmaVR**

### Without a Server (Offline)

The app works on its own as long as datasets have already been downloaded and cached onto the headset.  
Simply launch the app, select a cached dataset from the menu, and press Play.

### With the Server (Adding New Datasets)

To load new datasets that are not yet cached on the headset, the **PlasmaVR Server** must be running on a computer connected to the **same WiFi network** as the headset.

1. Open a terminal in the `Server/` folder
2. Install dependencies (first time only):
   ```bash
   pip install -r requirements.txt
   ```
3. Start the server:
   ```bash
   python PlasmaVRServer.py
   ```
4. A window will appear showing the server's URL and name — the headset will discover it automatically
5. In the VR app, open the dataset menu and the server will appear in the list — tap it to connect and browse its datasets

For more details on server setup and dataset folder structure, see `Server/SERVER_README.md`.

---

## Basic Tutorial

### Understanding Controllers: UI Press vs. Grab

The app uses two fundamental interaction types from the Unity VR Template:

**UI Press** — Interacting with Canvas UI elements (buttons, toggles, sliders):
- Target: 2D Canvas elements positioned in 3D space
- Input: Ray Interactor (point a laser at the element) or Poke Interactor (physically push with your finger)
- Button: Trigger button, or physical contact for poke
- Result: Element stays in place and fires a UI event (like OnClick); no object movement
- Example: Tapping a button in the Visualisation Menu or pressing an axis button in the Slice Menu

**Grab** — Picking up and manipulating 3D objects:
- Target: GameObjects with Collider and Rigidbody components
- Input: Direct Interactor (hand collides with object) or Ray Interactor (distance grab)
- Button: Grip button
- Result: Object attaches to your hand and moves through 3D space; respects physics and gravity; can be thrown
- Example: Grabbing a slider handle to scrub through frames, or grabbing the rotation gear to reorient the volume

When you look at your handheld controller for a moment, labels appear on the buttons showing which interactions are available from that controller.

### Selecting and Loading a Dataset

Press **X button** on your left controller to open the **Visualisation Menu**. Inside this menu, press the **[Open Dataset Menu]** button to summon the Dataset Selection panel.

The Dataset Menu is a free-floating panel (can be positioned anywhere in your workspace) showing a unified scrollable list:
- **Cached datasets** (stored on your headset): Listed first with **[Load]** and **[Trash]** buttons. Tap **[Load]** to begin playback immediately.
- **Server datasets** (from a running PlasmaVR Server): Listed second with **[Download]** buttons. Tap **[Download]** to download and cache the dataset, then begin playback.

Both cached and server datasets can coexist for the same simulation — deleting a cached copy doesn't remove the server listing, so you can always re-download. Once selected, playback begins and the Dataset Menu returns to its last position in your workspace.

### Basic Playback Controls

All playback controls are in the **Visualisation Menu** (press X to open):

- **Play/Pause button**: UI Press on the button to toggle playback state
- **Scrub slider**: **Grab** the slider handle with your grip button and drag left/right to seek to any frame. The visualisation updates as you drag.
- **FPS dropdown**: UI Press to select your preferred temporal resolution

### Toggling Visualisations

The **Visualisation Menu** contains three independent checkbox toggles (UI Press each one):
- **Particles**: Coloured point clouds showing velocity and flow structure
- **Isosurfaces**: Scalar field contours
- **Streamlines**: Vector field flow trajectories

You can mix and match visualisations.

### Clipping

Press **Y button** on your left controller to open the **Slice Menu**, a free-floating panel for spatial clipping.

The Slice Menu provides:

1. **Radial axis selector** — Four buttons in a radial layout (X, Y, Z, None). **UI Press** one to select which axis to clip, or disable clipping entirely.
2. **Grid Mode toggle** — **UI Press** to display coordinate grid values on the slice plane, helping you position it precisely.
3. **Clipping sliders** — A pair of min/max boundary sliders for the selected axis. **Grab** each slider handle and drag to define the clipping region.

**Interaction pattern**: **UI Press** an axis button → **Grab** and position the min/max sliders → Visualisation updates in real-time.

### Rotating the Visualisation

Look for the **rotation gear** at the base of the visualisation. Point with the raycast and then **Grab** it with your grip button and pull to rotate the visualisation.

### Taking Screenshots

While viewing your simulation, press **A button** to capture a high-resolution screenshot. Screenshots are saved locally for your session.
