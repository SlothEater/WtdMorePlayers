@echo off
rem Drag a clip pack (zip or folder) onto this file to add it to the game's CustomClips folder.
if "%~1"=="" (
  echo Drag a clip pack zip or folder onto this file.
  pause
  exit /b 1
)
where python >nul 2>nul
if errorlevel 1 (
  echo Python is needed for this. Get it from https://www.python.org/downloads/
  echo Or unzip the pack into the game's CustomClips folder yourself.
  pause
  exit /b 1
)
python -I "%~dp0wtd_pack.py" add "%~1" --force
pause
