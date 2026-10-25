@echo off
setlocal
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
set APP=%~dp0Capture.cs %~dp0DeviceInstall.cs %~dp0Native.cs %~dp0Ui.cs %~dp0Net.cs %~dp0Pick.cs %~dp0JpegEnc.cs %~dp0Encoder.cs %~dp0DxDesk.cs %~dp0VddCtl.cs %~dp0Lang.cs
set HOST=%~dp0VddHost.cs %~dp0VddCtl.cs %~dp0Lang.cs

if not exist "%~dp0out\ru" mkdir "%~dp0out\ru"
if not exist "%~dp0out\en" mkdir "%~dp0out\en"

"%CSC%" /nologo /optimize+ /unsafe /target:winexe /platform:x64 /out:"%~dp0out\ru\GlassCast.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.dll /r:System.Core.dll %APP%
if errorlevel 1 exit /b 1
"%CSC%" /nologo /optimize+ /target:winexe /platform:x64 /out:"%~dp0out\ru\VddHost.exe" /r:System.dll /r:System.Core.dll /r:System.ServiceProcess.dll %HOST%
if errorlevel 1 exit /b 1

"%CSC%" /nologo /optimize+ /unsafe /target:winexe /platform:x64 /define:EN /out:"%~dp0out\en\GlassCast.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.dll /r:System.Core.dll %APP%
if errorlevel 1 exit /b 1
"%CSC%" /nologo /optimize+ /target:winexe /platform:x64 /define:EN /out:"%~dp0out\en\VddHost.exe" /r:System.dll /r:System.Core.dll /r:System.ServiceProcess.dll %HOST%
if errorlevel 1 exit /b 1

echo built %~dp0out\ru and %~dp0out\en
