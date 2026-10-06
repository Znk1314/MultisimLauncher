# Multisim Launcher 1.0.1

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

## What's new in 1.0.1

**Silent start — the window only appears once it works.**

Previously every failed attempt put its splash and then its main window on
screen before the launcher discarded it, so you watched each failure go past.
Now each attempt is made entirely off screen:

1. the process is started and its windows are hidden as soon as they appear, by
   a background thread repeating every 12 ms;
2. the databases are checked as before — lock files present, main frame built,
   no error box;
3. **only a confirmed-healthy instance is revealed and brought to the front.**

A failed attempt is closed without ever having been visible. Measured on the
reference machine: **0 visible samples out of 727** during a forced failure,
against 13–20 before, and the main frame never stayed on screen for a single
frame in either case.

A **Silent start** switch in the sidebar turns it off.

**Also fixed**

* `ShowWindowAsync` has no effect on another process's window — the frame stayed
  visible for the entire attempt. Only the synchronous `ShowWindow` hides it.
* the splash screen is itself a dialog (`#32770`, title `Multisim`), the same
  class *and* title as the database error box, so the two can only be told apart
  by their message text. Both are now hidden.
* the health test required the main window to be **visible**, which can never be
  true while the launcher is deliberately hiding it. Every attempt therefore ran
  to its timeout and was discarded with the databases open for nothing.

Two things were tried and rejected, and are recorded here because both look like
improvements:

* `STARTF_USESHOWWINDOW` with `SW_HIDE` suppresses the splash neatly — and then
  Multisim never opens its databases at all. No lock files for 50+ seconds,
  against 8 seconds when started normally.
* closing the window to the tray, and a preheat-at-login option, were built,
  measured as not what was wanted, and reverted in full.

---

## Download

**`MultisimLauncher-Setup.exe`** (2.7 MB) — the only file you need.
It installs for the current user, so **no administrator rights are required**,
and it creates a desktop shortcut, a Start menu entry and an uninstall entry.

`MultisimLauncher.exe` (679 KB) is the same program as a single portable file,
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
