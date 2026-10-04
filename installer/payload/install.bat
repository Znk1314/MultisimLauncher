@echo off
REM ===========================================================================
REM  install.bat - per-user install performed by the self-extracting package.
REM  Runs after the payload has been extracted to a temporary folder, so it must
REM  copy everything to its final home and then create the shortcuts.
REM  ASCII only on purpose: the console code page on a Chinese Windows is 936.
REM ===========================================================================
setlocal EnableExtensions

set "APPDIR=%LOCALAPPDATA%\Programs\MultisimLauncher"
set "STARTMENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Multisim Launcher"
set "DESKTOP=%USERPROFILE%\Desktop"
set "HERE=%~dp0"

echo.
echo   Installing Multisim Launcher ...
echo.

REM ---- 1. lay down the files ------------------------------------------------
if not exist "%APPDIR%" mkdir "%APPDIR%" >nul 2>&1
if not exist "%APPDIR%\docs" mkdir "%APPDIR%\docs" >nul 2>&1

copy /Y "%HERE%MultisimLauncher.exe" "%APPDIR%\MultisimLauncher.exe" >nul
if errorlevel 1 goto :fail
if exist "%HERE%README.md"   copy /Y "%HERE%README.md"   "%APPDIR%\README.md"   >nul
if exist "%HERE%LICENSE.txt" copy /Y "%HERE%LICENSE.txt" "%APPDIR%\LICENSE.txt" >nul
if exist "%HERE%install-notes.txt" copy /Y "%HERE%install-notes.txt" "%APPDIR%\docs\install-notes.txt" >nul

REM ---- 2. remove the "downloaded from the internet" mark --------------------
REM Without this Windows may show a SmartScreen/zone warning the first time the
REM exe is started from its installed location.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Get-ChildItem -LiteralPath '%APPDIR%' -Recurse -File | Unblock-File -ErrorAction SilentlyContinue" >nul 2>&1

REM ---- 3. shortcuts ---------------------------------------------------------
if not exist "%STARTMENU%" mkdir "%STARTMENU%" >nul 2>&1

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$w=New-Object -ComObject WScript.Shell;" ^
  "$s=$w.CreateShortcut('%STARTMENU%\Multisim Launcher.lnk');" ^
  "$s.TargetPath='%APPDIR%\MultisimLauncher.exe';" ^
  "$s.WorkingDirectory='%APPDIR%';" ^
  "$s.Description='Start Multisim and retry until the component library loads';" ^
  "$s.Save();" ^
  "$d=$w.CreateShortcut('%DESKTOP%\Multisim Launcher.lnk');" ^
  "$d.TargetPath='%APPDIR%\MultisimLauncher.exe';" ^
  "$d.WorkingDirectory='%APPDIR%';" ^
  "$d.Description='Start Multisim and retry until the component library loads';" ^
  "$d.Save()" >nul 2>&1

REM ---- 4. uninstall entry in Settings > Apps --------------------------------
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MultisimLauncher" /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MultisimLauncher" /v DisplayName     /t REG_SZ /d "Multisim Launcher" /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MultisimLauncher" /v DisplayVersion  /t REG_SZ /d "1.0.0" /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MultisimLauncher" /v Publisher       /t REG_SZ /d "Multisim Launcher contributors" /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MultisimLauncher" /v DisplayIcon     /t REG_SZ /d "%APPDIR%\MultisimLauncher.exe" /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MultisimLauncher" /v InstallLocation /t REG_SZ /d "%APPDIR%" /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MultisimLauncher" /v NoModify        /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MultisimLauncher" /v NoRepair       /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MultisimLauncher" /v UninstallString /t REG_SZ /d "\"%APPDIR%\uninstall.bat\"" /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MultisimLauncher" /v QuietUninstallString /t REG_SZ /d "\"%APPDIR%\uninstall.bat\" /quiet" /f >nul 2>&1

REM ---- 5. done --------------------------------------------------------------
echo.
echo   Installed to: %APPDIR%
echo   A desktop shortcut and a Start menu entry were created.
echo.
echo   NOTE: the launcher starts the Multisim that is already installed.
echo         It cannot install Multisim itself.
echo.

if /I not "%~1"=="/quiet" (
  echo   Starting Multisim Launcher ...
  start "" "%APPDIR%\MultisimLauncher.exe"
  timeout /t 3 /nobreak >nul
)
exit /b 0

:fail
echo.
echo   INSTALL FAILED - could not write to %APPDIR%
echo.
pause
exit /b 1
