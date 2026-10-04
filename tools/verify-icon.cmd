@echo off
REM ===========================================================================
REM  verify-icon.cmd - rebuild the icon and score it against the reference frame.
REM  Prints the overlap so a change can be judged by a number instead of by eye.
REM ===========================================================================
setlocal
set REPO=%~dp0..
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OBJ=%REPO%\obj
set REF=%TEMP%\ms_icons\frame_256x256.png

"%CSC%" /nologo /target:exe /platform:anycpu /optimize+ /out:"%OBJ%\GenIcon.exe" /reference:System.dll /reference:System.Drawing.dll "%REPO%\tools\GenIcon.cs" || exit /b 1
"%OBJ%\GenIcon.exe" "%REPO%\assets\app.ico" || exit /b 1
"%OBJ%\OverlayIcons.exe" "%REF%" "%REPO%\assets\app.ico" "%OBJ%\overlay.png" "%OBJ%\diff.png"
