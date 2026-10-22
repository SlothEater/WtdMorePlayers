@echo off
rem Drag a clip pack (zip or folder) onto this file to add it to the game's CustomClips folder.
rem Needs Python (and ffmpeg to convert videos); offers to install them with winget if missing.
setlocal
if "%~1"=="" (
  echo Drag a clip pack zip or folder onto this file.
  pause
  exit /b 1
)

set "PY="
python -c "import sys" >nul 2>nul && set "PY=python"
if not defined PY py -3 -c "import sys" >nul 2>nul && set "PY=py -3"
if not defined PY (
  echo Python is needed to add clip packs.
  call :install Python.Python.3.12 Python || goto :manual
  py -3 -c "import sys" >nul 2>nul && set "PY=py -3"
  if not defined PY (
    echo Python installed. Drag the pack onto this file again.
    pause
    exit /b 0
  )
)

where ffmpeg >nul 2>nul
if errorlevel 1 if not exist "%LOCALAPPDATA%\Microsoft\WinGet\Links\ffmpeg.exe" (
  echo ffmpeg converts videos the game can't play. Without it, videos are copied as-is.
  call :install Gyan.FFmpeg ffmpeg
)

%PY% -I "%~dp0wtd_pack.py" add "%~1" --force
pause
exit /b 0

:install
where winget >nul 2>nul || exit /b 1
set "ANS="
set /p "ANS=Install %2 now with winget? [Y/n] "
if /i "%ANS%"=="n" exit /b 1
winget install -e --id %1 --accept-package-agreements --accept-source-agreements
exit /b %errorlevel%

:manual
echo Get Python from https://www.python.org/downloads/ and try again,
echo or unzip the pack into the game's CustomClips folder yourself.
pause
exit /b 1
