@echo off
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /optimize+ /unsafe /target:winexe /platform:x64 /out:"%~dp0GlassCast.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.dll /r:System.Core.dll "%~dp0Capture.cs" "%~dp0DeviceInstall.cs" "%~dp0Native.cs" "%~dp0Ui.cs" "%~dp0Net.cs" "%~dp0Pick.cs" "%~dp0JpegEnc.cs" "%~dp0Encoder.cs" "%~dp0DxDesk.cs" "%~dp0VddCtl.cs"
if errorlevel 1 exit /b 1
"%CSC%" /nologo /optimize+ /target:winexe /platform:x64 /out:"%~dp0VddHost.exe" /r:System.dll /r:System.Core.dll /r:System.ServiceProcess.dll "%~dp0VddHost.cs" "%~dp0VddCtl.cs"
if errorlevel 1 (
  if exist "%~dp0VddHost.exe" (
    echo VddHost.exe is in use, kept the previous one
  ) else exit /b 1
)
echo built %~dp0GlassCast.exe
