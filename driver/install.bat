@echo off
net session >nul 2>&1
if errorlevel 1 (
  echo запускай от администратора
  pause
  exit /b 1
)
bcdedit /set testsigning on
echo тестовая подпись включена. если только что включил — перезагрузись, потом снова этот файл.
cd /d "%~dp0IddSampleDriver"
if not exist IddSampleDriver.dll if exist x64\Release\IddSampleDriver.dll copy /y x64\Release\IddSampleDriver.dll . >nul
if not exist IddSampleDriver.dll (
  echo нет IddSampleDriver.dll — собери проект в Visual Studio + WDK, см. INSTALL.md
  pause
  exit /b 1
)
pnputil /add-driver "%~dp0IddSampleDriver\IddSampleDriver.inf" /install
echo дальше открой GlassCast.exe от администратора и нажми «драйвер дисплея»
pause
