# PlasmaVR — Developer Overview

A Meta Quest 3 VR app for visualising plasma physics simulation data. Users can navigate a 3D dataset, scrub through simulation frames, toggle particle/isosurface/streamline renderers, apply clipping planes, and take screenshots.

---

## How the app works

At startup the app shows a **dataset selection panel**. The user either loads a dataset that was previously cached on-device, or connects to the Python server (`Server/PlasmaVRServer.py`) over WiFi to download and cache one. Once a dataset is loaded, playback begins and all three renderer types (particles, isosurface, streamlines) animate through frames in sync.

---

## Core architecture

### `Simulation` (abstract base class)

Every renderer inherits from `Simulation`. It defines three abstract methods all renderers must implement:

```csharp
bool  startSim(int numFrames, Vector3 dims, string simPath)
void  displayFrame(int frame)
void  loadSimulation()
```

`startSim` initialises the renderer with the dataset path and frame count.  
`displayFrame` seeks to a specific frame.  
`loadSimulation` is called internally to load data from disk.

### `SimulationController`

The central coordinator. Holds references to all three `Simulation` instances and drives the playback loop. Responsibilities:

- Loads a dataset (full download or single-frame from cache) by calling `startSim` on each renderer
- Advances `currFrame` each update according to `targetFPS` and `playbackSpeed`
- Calls `displayFrame` on all renderers each tick
- Activates/deactivates individual renderers (`activatePart`, `activateIso`, `activateStream`)
- Coordinates with `DatasetCacheManager` and `DatasetServerClient` for data access
- Re-applies toggle states via `SimulationToggleController` after every load

---

## Script reference

### Root — `Assets/Project/Scripts/`

| Script | Role |
|--------|------|
| `Simulation.cs` | Abstract base for all renderer types |
| `SimulationController.cs` | Playback loop, frame dispatch, dataset loading |
| `DatasetLoaderManager.cs` | Convenience wrapper that wires `DatasetSelectionUI`, `DatasetServerClient`, and `SimulationController` together; attach to a scene GameObject |

---

### `API/`

Handles all communication with the Python server and on-device caching.

| Script | Role |
|--------|------|
| `DatasetServerClient.cs` | HTTP client; fetches dataset list/info, downloads files, uploads screenshots. Exposes `ServerUrl` property. |
| `DatasetCacheManager.cs` | Manages `Application.persistentDataPath/DatasetCache`. Downloads full datasets and tracks them with a JSON index. Enforces a max size (default 50 GB) with LRU eviction — never evicts the currently active dataset. |
| `ServerDiscovery.cs` | Sends a UDP broadcast (`PLASMAVR_DISCOVER`) on the LAN and collects JSON responses `{name, url, token}` from any running server. Populates the server dropdown and persists the last-used URL in `PlayerPrefs`. |

Data flow for a fresh dataset load:

```
ServerDiscovery  →  DatasetServerClient  →  DatasetCacheManager
   (UDP find)         (HTTP download)         (write to disk)
                                                    ↓
                                          SimulationController
                                           (startSim per renderer)
```

---

### `Particles/`

| Script | Role |
|--------|------|
| `PointCloudRenderer.cs` | Renders particles using Unity VFX Graph. Loads `.raw` files (one per frame), uploads a position texture to the VFX asset. Supports threshold filtering, clipping, lazy frame loading, and a configurable particle capacity. |

---

### `Scalars/`

| Script | Role |
|--------|------|
| `IsosurfaceRenderer.cs` | Builds meshes from binary `.raw` frames (pre-converted from OBJ by the server). Supports async loading, per-frame mesh caching, optional LOD, and shader-based clipping. Vertices are split to respect Quest 3's 65k vertex limit per mesh. |
| `StreamlineRenderer.cs` | Builds `LineRenderer`-style meshes from consolidated `.sbin` frames (200 `.raw` streamlines merged by the server into one file). Applies a three-color magnitude gradient and shader clipping. |

---

### `Clipping/`

The clipping system writes box transforms to shader globals so all renderers are clipped consistently without any renderer needing to know about the others.

| Script | Role |
|--------|------|
| `ClippingBox.cs` | Holds references to two box Transforms. Each frame it writes center/size/rotation to shader globals (`_ClipBoxCenter1`, `_ClipBoxSize1` etc.) and matching VFX Graph properties. Triggers a frame refresh when clipping changes while paused. |
| `ClippingBoxToggleSwitcher.cs` | UI manager with X/Y/Z buttons that swaps which axis´s slider controls are visible and which clipping box is active. |
| `ConstrainedSliderHandle.cs` | VR-grabbable slider handle; constrains movement to a single axis between two endpoint Transforms. Fires a `UnityEvent<float>` when the value changes. |
| `SliderHandlePair.cs` | Enforces min-separation and ordering between two `ConstrainedSliderHandle` instances (the min/max handles of a range slider). |

---

### `Misc/`

| Script | Role |
|--------|------|
| `GridCreator.cs` | Procedurally generates a 3D axis-aligned reference grid mesh with tick marks and value labels. Reads axis metadata from `info.txt` via `SimulationController`. |
| `LabelBillboard.cs` | Rotates a world-space GameObject each frame so it always faces the main camera. Used on axis labels created by `GridCreator`. |
| `ScreenshotCapture.cs` | Captures the framebuffer as a PNG and POSTs it to `DatasetServerClient.UploadScreenshot()`. Triggered by a controller button (Input System action). |
| `VRKeyboardTrigger.cs` | Opens the Meta Quest system keyboard when a `TMP_InputField` is tapped with a controller. Syncs keyboard output back to the field each frame. |

---

### `UI/`

| Script | Role |
|--------|------|
| `DatasetSelectionUI.cs` | The main dataset picker panel. Shows cached datasets (Load / Trash buttons) and server datasets (Download button) in a unified scrollable list. Talks to `DatasetServerClient`, `DatasetCacheManager`, and `SimulationController`. |
| `DatasetMenuToggle.cs` | Single-button toggle to open/close `DatasetSelectionUI`. Attach to any persistent HUD object. |
| `SimulationToggleController.cs` | Three UI toggles (particles / isosurfaces / streamlines) wired to `SimulationController.activate*()`. Exposes `ApplyCurrentToggleStates()` so the controller can re-apply toggle state after loading a new dataset. |
| `SimulationTimeScrubControl.cs` | Timeline scrub bar and play/pause button. Reads `SimulationController.maxFrame` and writes `currFrame` back via `SimulationController.GoToFrame()`. |
| `ControllerMenuToggle.cs` | Binds X/Y controller buttons to animated side panel GameObjects with auto-hide behaviour. |
| `SceneBrightnessSlider.cs` | Slider that adjusts `DirectionalLight.intensity` and `RenderSettings.ambientIntensity`. Captures baseline values in `Awake` so `ResetBrightness()` can restore them. Uses a `_started` flag to defer listener registration until after `Start` to avoid corrupting lighting at startup. |
| `RotationGear.cs` | VR-grabbable gear that applies rotation around a fixed axis only. Optional snap-to-corners. Used to rotate the simulation volume. |
| `DualControllerHaptics.cs` | Central haptic manager. Call `PlayLeft()`, `PlayRight()`, or `PlayBoth()` from any script that needs controller vibration feedback. |
| `UIHoverDepthEffect.cs` | Animates Z-translation and scale on UI elements when the VR pointer hovers over them, giving a sense of depth. |

---

## Data format

The server pre-processes raw simulation files before serving them to the headset:

| Type | On-disk format | What the server sends |
|------|---------------|-----------------------|
| Particles | `.raw` (float32 x/y/z per particle) | `.raw` unchanged |
| Isosurfaces | `.obj` mesh | `.bin` (binary vertex/triangle buffer, ~25× faster to parse) |
| Streamlines | 200× `.raw` per frame | `.sbin` (all 200 lines consolidated into one file) |

The `info.txt` file in each dataset root encodes frame count, axis names, dimensions, and units in a semicolon-delimited format. `SimulationController` parses it to configure `GridCreator` and scale the renderers correctly.

---

## Known limitation — cubic-only visualisation

Although `info.txt` is read and all three axis ranges are parsed into `axisMin` / `axisMax` in `SimulationController`, the visualisation currently assumes the domain is a cube. Specifically:

- `SimulationController` computes a per-axis `dims` vector correctly (`(axisMax.x - axisMin.x) / 20`, etc.) but then assigns only `dims.x` to both `grid.upperBound` and `grid.lowerBound`. The Y and Z extents of the grid are silently set to the same value as X.
- `GridCreator` has a single `upperBound` / `lowerBound` pair shared across all three axes — it has no concept of different extents per axis.
- The tick-label data-range remapping added for the label fix (`dataLowerBound` / `dataUpperBound`) is also X-only; Y and Z labels are remapped using the same X data range.
- The clipping box system (`ClippingBox.cs`) passes world-space box transforms to shader globals and is not directly tied to the cubic assumption, but any manually-placed clip boxes in the scene will be mis-sized if renderers are scaled non-uniformly for a non-cubic domain.

Supporting arbitrary aspect ratios is **future work**. The scripts that will need changes are:

| Script | What needs to change |
|--------|---------------------|
| `GridCreator.cs` | Replace single `upperBound`/`lowerBound` with per-axis pairs (`upperBoundX/Y/Z`, `lowerBoundX/Y/Z`) and separate `dataUpperBound`/`dataLowerBound` per axis |
| `SimulationController.cs` | Pass `dims.y` and `dims.z` to the grid, and pass per-axis data ranges (`axisMin.y`, `axisMax.y`, etc.) |
| `PointCloudRenderer.cs` | Verify that the VFX Graph bounds and any origin offset respect per-axis scale |
| `IsosurfaceRenderer.cs` | Already uses `dims.x/y/z` for the origin offset — verify mesh coordinate system matches the non-cubic world scale |
| `StreamlineRenderer.cs` | Same as above; check that streamline vertex coordinates are mapped into the correct non-cubic world space |

---

## Adding a new renderer type

1. Create a class that extends `Simulation` and implement `startSim`, `displayFrame`, and `loadSimulation`.
2. Add a `[SerializeField]` reference to it in `SimulationController` alongside the existing three.
3. Call `sim.gameObject.SetActive(true)` before `startSim` in both load paths (the GameObject must be active for coroutines to run).
4. Add an `activate*` method in `SimulationController` and a matching toggle in `SimulationToggleController`.

---

## Key dependencies

| Package | Used for |
|---------|----------|
| Meta XR SDK (Oculus) | XR session, controller input, hand tracking |
| XR Interaction Toolkit | Grabbable objects, `ConstrainedSliderHandle`, `RotationGear` |
| TextMeshPro | All UI text |
| Unity VFX Graph | `PointCloudRenderer` particle system |
| OBJImport (Dummiesman) | Fallback OBJ parsing (isosurfaces only; server normally pre-converts) |
