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
5. Browse to `Server/PlasmaVR.apk` and open it — SideQuest will install it automatically

### Linux

1. Download **SideQuest** (Easy Installer) from [sidequestvr.com](https://sidequestvr.com/setup-howto) — choose the Linux AppImage
2. Follow the SideQuest setup guide to enable Developer Mode on your headset
3. Connect your headset with a USB cable — accept the "Allow USB Debugging" prompt inside the headset
4. In SideQuest, click the **Install APK from folder** button (box with an arrow icon, top-right)
5. Browse to `Server/PlasmaVR.apk` and open it — SideQuest will install it automatically

---

## Running the App

After installing, put on your headset and:

1. Go to **Apps** in the Quest home menu
2. In the top-right filter, select **Unknown Sources**
3. Find and launch **PlasmaVR**

---

## Using the App

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
