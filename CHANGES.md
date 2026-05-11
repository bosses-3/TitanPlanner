# Mission Planner - Titan Dynamics Edition - Change Summary

A high-level summary of changes in this fork compared to upstream [ArduPilot/MissionPlanner](https://github.com/ArduPilot/MissionPlanner). For a chronological list of every individual change, see `ChangeLog.txt` (which still tracks upstream changes) and the project's git history.

This document covers the **385 commits** unique to the TitanPlanner fork.

---

# Branding & Theming

## Titan Dynamics System Theme
A new bundled theme (`titandynamics.mpsystheme`) is set as the default for new installs. Theme application has been extended to controls upstream did not theme — `ListBox`, `PropertyGrid`, `MessagesList`, status tab, menu-bar icons, the Splash form, and toast notifications.

## Dark Windows Title Bars
Native Windows title bars now follow the application theme rather than always being light.

## New Application Icon, Splash, and Tab Icons
A new app icon (Titan Dynamics branding), redesigned splash screen, and a fresh icon set for the main top-level tabs in both dark and light variants.

## Plane & Copter Default Icons
Tornado and Proton aircraft are the default rendered icons for plane and copter vehicle classes. Users can also drop in a custom aircraft icon image to override per-vehicle defaults entirely.

---

# HUD Overhaul

## Layout & Typography
Major reflow of the HUD. Larger, optimized fonts; centering uses cached per-character metrics for stability; left/right scroller arrows scale with font size; mode display has a redrawn trapezoid background; heading scroller now shows an `H` indicator for home heading; degree symbol (`°`) replaces the literal `deg` string; `Yaw` is now labelled `Heading`.

## New Telemetry Readouts
- **AOA / SSA / critical AOA gauge** for fixed-wing
- **Low airspeed warning** (red) and **rate-of-change of airspeed** (blue bar)
- **Low ground speed warning**
- **Dual GPS** support on the top bar
- **EKF / VIBE / GPS1 / GPS2** indicators repositioned to the right-most edge
- **ESC telemetry** in tlog graphs
- **Dual battery** display in the bottom-left

## Plugin-Overridable Icon Slots
Icon slots on the HUD can now be overridden by plugins, opening up custom telemetry/iconography integrations.

## Pre-Arm & Flight Mode Rendering
Cleaner, more reliable display of pre-arm status and the active flight mode on the HUD.

## Removals & Cleanups
The Russian-localized HUD has been removed entirely; CPU message relocated; time readout removed; link-stats moved.

---

# 3D Map (Map3D)

## Real-Time Vehicle Rendering
The connected vehicle now renders in 3D with default STL models per MAV type (plane, quadplane, copter, rover, boat, sub, tricopter, etc.). Plane color rendering is corrected; terrain render distance increased to 50 km.

## Tile Loading Pipeline
A weighted distance/zoom-level scoring algorithm prioritizes which tiles to load. At 500 m+ altitude, zoom level is dropped to keep the view fluid. Tiles fully covered by higher-zoom tiles are skipped. Disk caching has been added to avoid re-fetching across sessions, with the caching layer extracted into its own class. A latent thread race in `tileArea` was fixed by lock-token redesign.

## Camera & Visual Effects
- **FPV mode** with smooth camera movement and pitch-matched heading lines
- **Orbital camera** mode
- **Fog effect** (50–100 km) using theme HUD sky colors
- **Heading indicators** and dynamic render distance
- **Trailing flight path** with Kalman-smoothed lines on all axes; render gated on stable telemetry
- **High-altitude rendering** improvements

## ADS-B in 3D
ADS-B traffic is rendered with proper depth testing and pass ordering so it correctly occludes against terrain and the vehicle.

## Persistence & UX
- Camera distance, height, and angle saved on close
- Settings dialog overhaul with persistence and out-of-range guards
- Configurable marker size
- Reset button defaults aligned with settings defaults
- 2D ↔ 3D map waypoint and position synchronization
- FPS readout
- Required-valid-GPS gate before vehicle tracking begins
- 3D Map cleanly stops on disconnect and tears down before app close

## GPU Acceleration
GPU acceleration is enabled application-wide, with a registry-level Windows GPU-preference hint to prefer the discrete GPU on hybrid systems.

---

# Tab & Layout Restructuring

## New Themed Tab Strip
Replaced the dated FlightData tab bar with a modern themed strip and refreshed icons.

## New / Re-purposed Tabs
- **Params tab** in FlightData and a Params panel co-located on the map
- **Tuning tab** in FlightData (vehicle-aware: plane/copter/rover)
- **Video tab** with USB camera and GStreamer support, plus immediate HUD-overlay toggle
- **Inspector tab** (MAVLink Inspector exposed without `Ctrl+F`)
- **Logs tab** unifying former Dataflash and Telemetry tabs
- **Status tab** in FlightData fixed
- **Help tab** hidden by default; Gauges tab hidden by default

## Splits, Sizing & Defaults
Split percentages on every divider in the main GUI persist across launches, with faster swap and resize behavior. New installs default to maximized window and ~50% HUD width. HUD vertically centers when swapped with the map.

---

# Parameters

## Async Loading
Parameter documentation and ranges (~6000 entries) load asynchronously, eliminating the multi-second freeze on the Full Parameter List screen.

## Search & Autocomplete
Themed search bar with suggest-only autocomplete; search resets when navigating away. Removed the warning dialog and obsolete preset feature on the param page.

## Other
- Removed confirmation dialogs after param writes (kept reboot-required and save-failed)
- Toast notification on successful param write
- Tree collapsed by default in tab embeddings to save space
- User params can now be set on the Flight Modes page

---

# Setup & Configuration Pages

## Betaflight-Style Motor Setup
The former "Motor Test" page has been renamed to **Motor Setup**, redesigned in a Betaflight-inspired layout, promoted to the Mandatory Hardware section, and only displayed for copter/quadplane vehicles. Frame and class type images are shown live.

## Servo Output Tab
A revamped Servo Output panel with click-or-drag sliders for trim, an "Equidistant" min/max helper, and proper min/max-from-trim handling. Servo trim slider now syncs with the combobox.

## Radio & Calibration
Visual-stick-controls Radio Calibration UI; proper reversing logic restored to match upstream behavior; accel calibration polished.

## Other Setup Pages
GUI cleanups across Compass, Failsafe, Flight Modes, Initial Setup Serial; Battery Monitor pages merged into one unified screen; voltage/current calibration enabled regardless of selected sensor.

## Configuration Profile Management
Save and load named configuration profiles for the Mission Planner application itself.

---

# Auto-Connect & Devices

## USB Auto-Connect
A dedicated USB auto-connect path with VID coverage for all common flight controllers (including Cube). Toast notification fires when a device auto-connects. Auto-connect is disabled while flashing firmware to avoid races.

## ELRS
ELRS airport support added.

---

# Mode Selector & Vehicle State Toolbar

## Top-Bar Mode Selector
A compact mode selector lives in the top menu bar with a pin/favorite system that auto-populates from the vehicle's `FLTMODE1`–`FLTMODE6` parameters. The selector auto-sizes based on available space and hides when not connected.

## Vehicle State Toolbar
Arm/disarm indicator and a clickable GPS status popup with detailed real-time telemetry, with values that update live.

---

# ADS-B

- Built-in **ADS-B HTTP endpoint**
- Aircraft type & category fetched from the API
- Sprites updated to **tar1090** style
- Distance ranges adjusted to 1, 5, 10 km
- Enhanced tooltip with altitude delta and smart distance formatting
- Speech callouts of proximate aircraft
- Emitter category support; squawk fixes
- ADS-B URL auto-upgraded to adsb.lol so existing users move to the maintained endpoint

---

# Dashboard / QuickView

## Editing Workflow
- Drag-and-drop reordering of items
- Right-click → Edit option for individual items
- Overhauled edit dialog with categorization and search
- Quick tab now pops out instead of undocking awkwardly

## Display Modes
- **Gauge display mode** for any dashboard item
- 270° gauges with blinking on out-of-range values
- Minimum 25% panel sizes enforced
- Battery `Wh` used and `Wh/km` available as Quick tab metrics

## Defaults & Persistence
Sensible default Quick tab options on first run; reset and reset-all per-item; loiter radius increments by 10 instead of 1.

---

# Mission Planning

## Upstream Improvements (Pulled In)
WPOverlay2 + new mission rendering pipeline, MissionStyleEditor, `MissionSegmentizer` Arc support, `GMapRoute` arrow modes, `GMapMarkerWP` marker types and text centering, plane return path mavcmd, `DO_LAND_START` Z unit fix.

## Fork Additions
- **Center to Vehicle** button
- **Clear all** in PLAN tab; clear-path from FlightData
- 2D ↔ 3D map waypoint sync
- Improved mission table UI
- Removed forced switch to PLAN tab when "load waypoints on connect" is enabled

---

# Map (2D) UX

- **Double-click "Fly to Here"** for one-click GUIDED-mode entry, with a persistent enable toggle
- **Tools button** on map for the former "temp" / `Ctrl+F` window
- **Propagation Settings** accessible from the map (`Ctrl+W`)
- **Find Location** added to the PLAN tab
- Custom SITL locations; Woodley default
- Larger fonts on Sats/HDOP labels with new VDOP readout
- Zoom buttons and scale bar on map
- Clean pixel-perfect bottom-bar layout
- Auto-pan on parameter button swap

---

# Reliability, Performance & Threading

- App-wide **GPU acceleration**
- `SuspendLayout`/`ResumeLayout` and async loading on heavy tabs (Tuning, Params, Dashboard) to eliminate UI stalls
- Faster, smoother docking/undocking of flight modes panel
- Increased debouncing on 2D→3D map sync
- Robust GStreamer start/stop handling
- Resilient QuickView state under churn
- Application Insights / log4net wiring kept up to date with the upstream layout

---

# Notable Bug Fixes

- **Map3D paint race**: dedicated lock token replaces locking on a list reference that gets reassigned, fixing intermittent `Destination array was not long enough` paint crashes
- **Duplicate HUD `lowairspeed` binding** post-merge causing `ControlBindingsCollection.CheckDuplicates` on FlightData init
- `ConfigRawParams` crash
- `ConfigFlightModes` not populating `RCX_*` options on load
- Crash when starting a busy camera
- Upstream prefetch bug on the latest Mission Planner
- DotNet zip issues affecting installer and release builds
- Firmware flashing bug tied to DotNet version
- Dashboard items not updating after context-menu edits, panel exceeding tab width
- Dashboard defaults containing duplicate "distance to home"
- 3D-map nav-bearing line now points at the actual waypoint position
- Map sync between PLAN and DATA tabs
- Aborting scripts with no script no longer crashes
- Splash-screen and title-bar version display unified to a single build-stamped version

---

# Auto-Update Pipeline

## CI Workflow
GitHub Actions builds Release and Debug, packages an MSI and zip, computes `checksums.txt` and `version.txt`, and publishes:
- A **`development-build`** prerelease on every push to `development`
- A **`TitanPlanner-<version>`** stable release on every push to `master`

## In-App Updater
Reads `version.txt` from the channel URL (stable or beta) and compares as `System.Version`. On a positive match, streams the differing files directly out of the release zip (`TitanPlanner.zip`) using HTTP range requests, validates each file's MD5 against `checksums.txt`, then hands off to `Updater.exe` for the in-place swap.

## Recent Fixes
- Production channel was passing the directory base URL into `CheckMD5`, causing per-file 404s; it now correctly streams from the release zip exactly as the dev channel does.
- Splash and title bar now show one consistent, build-stamped version (e.g. `1.3.9615.29424`) rather than the stale upstream `1.3.83` next to a different auto-stamped value.

---

# Misc UX

- **Reboot Vehicle** button in the Actions tab; global **`Ctrl+R`** shortcut to reboot autopilot
- "Don't show again" option on the param-missing dialog, info dialogs, the failsafe-mode-change warning, and the ground-station-log dialog
- Param write toast notifications
- Reorganized Actions tab as a flow layout with payload controls merged in
- Revamped Telemetry Log tab
- Logs tab combining Dataflash + Telemetry views
- Custom upgrade-path logic so existing original Mission Planner users transition cleanly to the Titan Dynamics edition (theme, default tab visibility, settings migration)
- Windows console terminal removed from the main exe

---

# Upstream Sync

This fork periodically merges from `upstream/master`. The most recent merge brought in 27+ days of upstream work including the WPOverlay2 mission pipeline, `ThemeManager` ListBox/PropertyGrid theming, auto-pinning of flight modes from `FLTMODE1-6`, `MissionSegmentizer` Arc support, and various GMapMarkerWP / GMapRoute / mavcmd improvements.
