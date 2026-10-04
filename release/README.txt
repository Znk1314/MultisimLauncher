Multisim Launcher - ready to run
===============================

Two files. You almost certainly want the first one.

  MultisimLauncher-Setup.exe     installer, about 2.5 MB
  MultisimLauncher.exe           portable single file, about 550 KB


BEFORE YOU RUN IT - Windows will warn you
-----------------------------------------
These files are not code-signed, so the first time you run the installer
Windows SmartScreen shows:

    Windows protected your PC
    Microsoft Defender SmartScreen prevented an unrecognised app from starting.

Click "More info", then "Run anyway".

That warning appears for ANY unsigned program downloaded from the internet.
It does not mean this download is damaged. If you would rather not trust a
binary, either check SHA256SUMS.txt in this folder, or build the program
yourself - see the bottom of this file.

  MultisimLauncher.exe        263A5A365D0157042835C4F1B0111D5D049A31750E22E3ED99F4395006FD852E
  MultisimLauncher-Setup.exe  DFBB24C724149789CB2F01F6E3BF06BD353D69FE0697F6378AD6F173A0A0519A

  PowerShell:  Get-FileHash .\MultisimLauncher-Setup.exe -Algorithm SHA256


MultisimLauncher-Setup.exe
--------------------------
Double-click it. It installs for the current user only, so:

  * NO administrator rights are needed
  * nothing is written outside your own profile
  * a desktop shortcut and a Start menu entry are created
  * "Multisim Launcher 1.0.0" appears in Settings > Apps and uninstalls cleanly

Files land in:  %LOCALAPPDATA%\Programs\MultisimLauncher


MultisimLauncher.exe
--------------------
The same program as a single portable file. No installer, no registry
entries, no dependencies. Copy it anywhere and run it.


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


If Multisim is not found
------------------------
The launcher looks in this order:

  1. the NI registry keys, which record the real database paths and therefore
     work no matter which drive Multisim is installed on
  2. the usual install roots, which also cover D: and E:

If it still reports "not found", please open an issue with your path.


The root cause is not fixed
---------------------------
This tool works around the problem, it does not cure it. The failure is not
caused by a damaged database - the master database matches the installer's
copy byte for byte - and the exact cause has not been identified. See
docs/BACKGROUND.md for the full investigation.


Build it yourself
-----------------
If you prefer not to trust a binary:

    powershell -ExecutionPolicy Bypass -File build.ps1 -MakeInstaller

That needs nothing but the C# compiler that ships inside Windows.


Uninstalling
------------
Settings > Apps > Installed apps > "Multisim Launcher" > Uninstall.
The launcher's own log at %LOCALAPPDATA%\MultisimLauncher\launcher.log is
removed on uninstall. Attach it to a bug report if something goes wrong.
