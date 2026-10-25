@echo off
rem SPDX-License-Identifier: GPL-3.0-or-later
setlocal
set "arch=%~1"
if "%arch%"=="x64" (set "vcarch=amd64") else if "%arch%"=="arm64" (set "vcarch=amd64_arm64") else (echo Usage: build.cmd x64^|arm64 & exit /b 2)
set "source=%~dp0"
set "out=%~dp0..\..\build\artifacts\rnnoise\%arch%"
for /f "usebackq tokens=*" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "vs=%%i"
if not defined vs exit /b 3
call "%vs%\VC\Auxiliary\Build\vcvarsall.bat" %vcarch%
if errorlevel 1 exit /b 4
if not exist "%out%" mkdir "%out%"
pushd "%out%"
cl /nologo /O2 /MT /DNDEBUG /D_USE_MATH_DEFINES /D_CRT_SECURE_NO_WARNINGS /I "%source%include" /I "%source%src" /LD "%source%src\denoise.c" "%source%src\rnn.c" "%source%src\rnn_data.c" "%source%src\rnn_reader.c" "%source%src\pitch.c" "%source%src\kiss_fft.c" "%source%src\celt_lpc.c" /Fe:rnnoise.dll /link /DEF:"%source%rnnoise.def"
if errorlevel 1 (popd & exit /b 5)
cl /nologo /W4 /WX /O2 /MT "%source%smoke.c" /Fe:rnnoise-smoke.exe
if errorlevel 1 (popd & exit /b 6)
dumpbin /dependents rnnoise.dll
popd
exit /b 0