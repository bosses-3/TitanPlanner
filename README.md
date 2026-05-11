# Mission Planner - Titan Dynamics Edition

<p align="center">
   <img width="300" height="300" alt="icon" src="https://github.com/user-attachments/assets/b3e67430-0296-4f09-ada2-d01a03e684ae"/><br><br>
   A customized fork of <a href="https://github.com/ArduPilot/MissionPlanner">Mission Planner</a> with enhanced UI/UX and ease-of-use improvements
   <br><br>
   <img width="2560" alt="image" src="https://github.com/user-attachments/assets/200dddd2-ea26-425a-8dad-b8b8093fb33a" /><br><br>
   <img width="2560" height="1540" alt="image" src="https://github.com/user-attachments/assets/2fbe5217-4a12-43ab-ae10-1fee7a5ada8a" /><br>
</p>

---

## What's Different?

A reworked HUD, a real-time 3D map with vehicle rendering, an overhauled parameter editor and tab layout, a Betaflight-style motor setup, USB auto-connect, and dozens of smaller UX improvements throughout the app.

**See [CHANGES.md](CHANGES.md) for the full list of features and fixes added in this fork.**

---

## Installation

### Windows (Recommended)

Grab the latest installer from [Releases](https://github.com/Titan-Dynamics/TitanPlanner/releases/latest). The app auto-updates from then on.

### Building from Source

Requires Visual Studio 2022.

```bash
git clone https://github.com/Titan-Dynamics/TitanPlanner.git
cd TitanPlanner
git submodule update --init
```

Open `MissionPlanner.sln` in Visual Studio 2022 and Build.

### Linux (Mono)

```bash
sudo apt install mono-complete mono-runtime libmono-system-windows-forms4.0-cil \
    libmono-system-core4.0-cil libmono-winforms4.0-cil libmono-corlib4.0-cil \
    libmono-system-management4.0-cil libmono-system-xml-linq4.0-cil

mono MissionPlanner.exe
```

---

## Platform Support

| Platform | Status |
|----------|--------|
| Windows | ✅ Full Support |
| Linux (Mono) | ⚠️ Partial Support |
| macOS | ⚠️ Experimental |
| Android | ✅ [Play Store](https://play.google.com/store/apps/details?id=com.michaeloborne.MissionPlanner) |
| iOS | ⚠️ Experimental |

---

## Building the Installer

* Set up the WiX toolset
* Switch to Release mode
* Clean Solution → Build a Release version of MissionPlanner → Build solution
* Copy drivers into the `Msi` folder so they live in `\Msi\Drivers`
* Build the `wix` project from the MissionPlanner solution (outputs to the `Msi` folder)
* Go to the `Msi` folder
* Run `installer.bat`
* Run `create.bat`

---

## Upstream

- Upstream repository: https://github.com/ArduPilot/MissionPlanner
- ArduPilot website: http://ardupilot.org/planner/
- Forum: http://discuss.ardupilot.org/c/ground-control-software/mission-planner

## License

See [COPYING.txt](COPYING.txt).
