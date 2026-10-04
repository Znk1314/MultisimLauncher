Multisim Launcher - ready to run
===============================

Two files. You almost certainly want the first one.

  MultisimLauncher-Setup.exe     installer, about 2 MB
  MultisimLauncher.exe           portable single file, about 29 KB


MultisimLauncher-Setup.exe
--------------------------
Double-click it. It installs for the current user only, so:

  * no administrator rights are needed
  * nothing is written outside your own profile
  * a desktop shortcut and a Start menu entry are created
  * an uninstall entry appears in Settings > Apps

Files land in:  %LOCALAPPDATA%\Programs\MultisimLauncher


MultisimLauncher.exe
--------------------
The same program as a single portable file. No installer, no registry
entries, no dependencies. Copy it anywhere and run it. If you use this one,
create your own shortcut if you want one.


What it does
------------
On Windows 11, NI Multisim 14.x intermittently starts with

    Problem accessing the database.
    The Master Database cannot be accessed.

and then the component library is empty, so no parts can be placed. Measured
on one machine, only about a quarter to a half of cold starts came up usable,
and the failures arrive in runs of several in a row.

Press Start and the launcher keeps launching Multisim, silently closing the
attempts that came up with a broken library, until one instance is healthy.
The button then reads Stop, and pressing it closes Multisim again.

There is no console window, and failed attempts are not shown to you - only a
status line changes.

The launcher does NOT modify Multisim, its databases, or any Windows setting.
It only starts and closes programs already on your computer.


Requirements
------------
  * Windows 10 or 11 (x64)
  * NI Multisim 14.x already installed - this cannot install Multisim
  * .NET Framework 4.x, which is already present on every supported Windows

If Multisim is installed somewhere unusual and the launcher reports
"Multisim not found", please open an issue with the path.


Verifying these files
---------------------
Every file here is built from the source in this repository by build.ps1.
If you would rather not trust a binary, build it yourself:

    powershell -ExecutionPolicy Bypass -File build.ps1 -MakeInstaller

That needs nothing but the C# compiler that ships inside Windows.


Uninstalling
------------
Settings > Apps > Installed apps > "Multisim Launcher" > Uninstall.
A log of the launcher's own runs is kept at
%LOCALAPPDATA%\MultisimLauncher\launcher.log and is removed on uninstall.
