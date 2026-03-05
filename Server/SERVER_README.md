# PlasmaVR Server

A Python server that streams simulation datasets to the PlasmaVR Quest app over your local WiFi network.

---

## Setup

### 1. Install Python

Download Python 3.8+ from [python.org](https://python.org).  
On Windows, check **"Add Python to PATH"** during installation.

### 2. Install dependencies

Open a terminal in this (`Server`) folder and run:

```bash
pip install -r requirements.txt
```

---

## Running the server

```bash
python PlasmaVRServer.py
```

On macOS / Linux use `python3` instead of `python`.

The GUI window will open and the server starts immediately, listening for connections from the headset.

Your datasets should be placed inside the `simulation_data` folder:

```
simulation_data/
├── MyDataset/
│   ├── info.txt
│   ├── Particles/
│   ├── Vectors/
│   └── Scalars/
└── ...
```

---

## Using the GUI

Once the server is running the GUI shows:

- **Server URL** — the address the headset uses to connect. Use the **Copy** button to copy it to the clipboard.
- **Server Name** — the name shown on the headset's server discovery screen. You can edit it (up to 17 characters) and click **Apply** to update it without restarting.
- **Data Folder** — the folder the server reads datasets from. Click **Browse…** to pick a different folder and **Apply** to switch immediately. Click **Open** to open it in your file manager.
- **Screenshots Folder** — where screenshots taken inside the headset are saved. Click **Browse…** to change it. Click **Open** to open it in your file manager.

The headset app discovers the server automatically as long as both devices are on the same WiFi network. No manual URL entry is needed.

---

## Connecting from the headset

1. Launch **PlasmaVR** on the headset.
2. On the main menu, tap **Connect to Server**.
3. Your server should appear in the list — tap it to connect.
4. Select a dataset and press **Play**.

---

## Advanced usage

### Running without the GUI

```bash
python PlasmaVRServer.py --no-gui
```

### Custom port or data folder

```bash
python PlasmaVRServer.py --port 9090 --data-folder /path/to/data
```

Full list of flags:

| Flag | Default | Description |
|------|---------|-------------|
| `--data-folder PATH` | `./simulation_data` | Dataset root folder |
| `--port N` | `8080` | Port to listen on |
| `--host HOST` | `0.0.0.0` | Network interface to bind |
| `--no-gui` | — | Disable GUI, run console-only |
| `--no-wsgi` | — | Use Flask dev server instead of `waitress` |

### Virtual environment (recommended for isolation)

**Windows:**
```powershell
python -m venv venv
.\venv\Scripts\Activate.ps1
pip install -r requirements.txt
python PlasmaVRServer.py
```

**macOS / Linux:**
```bash
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
python3 PlasmaVRServer.py
```

### GUI not starting?

The GUI requires `tkinter`. If it fails to open, run with `--no-gui` or install the missing package:

| Platform | Command |
|----------|---------|
| Ubuntu / Debian | `sudo apt install python3-tk` |
| Fedora | `sudo dnf install python3-tkinter` |
| Arch | `sudo pacman -S tk` |
| macOS (Homebrew) | `brew install tcl-tk` |
| Windows | Reinstall Python from python.org with Tcl/Tk enabled |

### Verify the server is reachable

Open a browser on the same machine and visit `http://localhost:8080/health`.  
You should see:
```json
{"status": "ok", "server": "PlasmaVR Data Server"}
```

### Firewall

If the headset can't find the server, make sure your firewall allows inbound connections on the chosen port (default `8080`).

---