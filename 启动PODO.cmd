@echo off
chcp 65001 >nul
setlocal
set "ROOT=%~dp0"
set "DOTNET=%ROOT%..\.tools\dotnet\dotnet.exe"
set "NUGET_PACKAGES=%USERPROFILE%\.nuget\packages"
set "EXE=%ROOT%src\WitchDrawer.App\bin\Release\net10.0-windows\PODO.exe"

if not exist "%DOTNET%" (
  echo .NET SDK not found: %DOTNET%
  pause
  exit /b 1
)

tasklist /FI "IMAGENAME eq PODO.exe" /NH | find /I "PODO.exe" >nul
if not errorlevel 1 (
  powershell.exe -NoProfile -STA -Command "Add-Type -AssemblyName PresentationFramework; $choice = [System.Windows.MessageBox]::Show('检测到正在运行的 PODO。要关闭当前版本、重新生成并打开最新版本吗？', '更新 PODO', [System.Windows.MessageBoxButton]::YesNo, [System.Windows.MessageBoxImage]::Question); if ($choice -eq [System.Windows.MessageBoxResult]::Yes) { exit 0 } exit 1" >nul 2>&1
  if errorlevel 1 exit /b 0

  powershell.exe -NoProfile -Command "$processes = Get-Process -Name PODO -ErrorAction SilentlyContinue; if ($processes) { $processes | Stop-Process -Force; $processes | Wait-Process -Timeout 5 -ErrorAction SilentlyContinue }" >nul 2>&1
  tasklist /FI "IMAGENAME eq PODO.exe" /NH | find /I "PODO.exe" >nul
  if not errorlevel 1 (
    echo PODO could not be closed. Please exit it from the system tray and try again.
    pause
    exit /b 1
  )
)

echo Preparing the latest PODO build...
"%DOTNET%" restore "%ROOT%WitchDrawer.sln" --configfile "%ROOT%NuGet.Config" --ignore-failed-sources
if errorlevel 1 (
  echo Restore failed.
  pause
  exit /b 1
)

"%DOTNET%" build "%ROOT%WitchDrawer.sln" --configuration Release --no-restore
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)

start "PODO" "%EXE%"
