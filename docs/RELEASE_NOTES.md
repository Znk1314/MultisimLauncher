# Multisim Launcher 1.0.2

A small Windows launcher that keeps starting NI Multisim until the component
library actually loads, working around the well known Windows 11 startup
problem where Multisim intermittently comes up with

```
Problem accessing the database.
The Master Database cannot be accessed.
```

and an empty part library.

Press **Start** and it retries quietly until one instance is healthy; the
button then becomes **Stop**.

---

## What's new in 1.0.2

**Tray, reuse, and preheat — built on one discovery: `multisim.exe` is not
single instance.** Starting it while it is already running produces a second
full copy. Measured on the reference machine: two starts in a row left two
processes, each with its own main window and about 150 MB of memory.

* **It will not start a second Multisim.** If a usable instance is already
  running it is brought to the front instead. Detection is by window class, not
  window title, because the title contains the open file name and the UI
  language.
  This also fixed a latent bug: the previous version closed *every*
  `multisim.exe` on the machine at the start of each attempt, which would have
  discarded a running session on a second click.

* **Closing the window keeps it running.** The window moves to the notification
  area so a retry already in progress can finish. The tray menu restores it or
  exits for real. Because the window is hidden rather than minimised, no
  taskbar button is left behind. `Quit` in the sidebar still exits outright.

* **Preheat at login, off by default.** Tick it and Multisim is started in the
  background at sign-in, retrying silently until the library loads, and then
  the launcher exits completely — no window, no tray icon, nothing resident.
  By the time you click the desktop shortcut, Multisim is already waiting.
  Load time of the ~238 MB database is the slow part, and this is the only way
  to stop it being *your* wait.
  Written to `HKCU`, so no elevation and nothing you cannot remove. If the
  launcher is later moved, the checkbox reports **off** rather than showing a
  tick that does nothing.

**Icon.** Redrawn to follow the geometry of the real Multisim icon — four white
runs with a bracket joining two of them, brass runs and terminals, three stems
along the bottom — in a deep navy palette. Positions were measured off the
256px frame inside `multisim.exe` rather than eyeballed; an eyeballed version
was overlapping the reference by only 25%. The mark is now clipped to the
rounded tile, so nothing shows outside the icon.

**Installer fix.** Shortcuts are no longer gated behind an install-time task.
Silent installs have no task page, so a task-gated shortcut was silently
skipped while the install still reported success — and `checkedonce` meant that
unticking it once removed the shortcut from every later install.

**Self-tests in the shipped executable.** `--selftest-preheat` and
`--selftest-tray` exercise those paths without a mouse and return the failure
count as the exit code. Synthetic clicks proved to be a poor test: they need
DPI-correct coordinates and a window genuinely on screen.

---

## Download

**`MultisimLauncher-Setup.exe`** (2.7 MB) — the only file you need.
It installs for the current user, so **no administrator rights are required**,
and it creates a desktop shortcut, a Start menu entry and an uninstall entry.

`MultisimLauncher.exe` (687 KB) is the same program as a single portable file,
if you would rather not install anything.

---

## ⚠️ Windows will warn you the first time — this is expected

These binaries are **not code-signed**, so Windows SmartScreen will show

```
Windows protected your PC
Microsoft Defender SmartScreen prevented an unrecognised app from starting.
```

**To continue: click `More info`, then `Run anyway`.**

This is not a sign that anything is wrong with the download. It is what Windows
shows for *any* unsigned executable that came from the internet — signing
certificates cost money and this is a free project.

### Verify the download instead

Rather than trusting the warning, you can check that the file is exactly the
one built here. `SHA256SUMS.txt` is attached to this release:

```
263A5A365D0157042835C4F1B0111D5D049A31750E22E3ED99F4395006FD852E  MultisimLauncher.exe
DFBB24C724149789CB2F01F6E3BF06BD353D69FE0697F6378AD6F173A0A0519A  MultisimLauncher-Setup.exe
```

PowerShell:

```powershell
Get-FileHash .\MultisimLauncher-Setup.exe -Algorithm SHA256
```

If the hash matches, you have the exact artifact built from this repository's
source. Or build it yourself:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -MakeInstaller
```

---

## Requirements

* Windows 10 or 11 (x64)
* **NI Multisim 14.x already installed** — this launcher cannot install Multisim
* .NET Framework 4.x, already present on every supported Windows

## What is verified about this build

| | |
|---|---|
| Runs without administrator rights | yes, `PrivilegesRequired=lowest` |
| Creates a desktop shortcut | verified |
| Creates a Start menu entry | verified |
| Appears in Settings > Apps and uninstalls cleanly | verified — removes program files, both shortcuts and its registry entry |
| Shows a console window | **no** — built as a GUI subsystem binary |
| Touches Multisim databases or Windows settings | **no** |
| Install size | about 4.9 MB |

The launcher does not modify, patch or crack Multisim. It only starts and
closes programs already on your computer.

## Known limitations

* The root cause of the Multisim failure is **not** fixed, because it has not
  been identified. On the machine this was developed on only about 25–50 % of
  cold starts succeed, so several attempts are often needed. That is precisely
  what this tool automates. See `docs/BACKGROUND.md` for the full investigation
  and everything that was measured and ruled out.
* If Multisim is installed somewhere unusual and the launcher reports
  "not found", please open an issue with the path.

## Reporting a problem

Attach `%LOCALAPPDATA%\MultisimLauncher\launcher.log`, which records every launch
attempt.
