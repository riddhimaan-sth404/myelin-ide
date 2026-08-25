@echo off
setlocal

cd /d "%~dp0"

if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"
if exist "%USERPROFILE%\.cargo\bin\cargo.exe" set "PATH=%USERPROFILE%\.cargo\bin;%PATH%"

if not exist "target\debug\myelin_ffi.dll" (
    echo [Myelin] Compiling native Rust core...
    cargo build
)

echo [Myelin] Launching Myelin IDE...
dotnet run --project "apps\desktop\src\Myelin.UI\Myelin.UI.csproj"

if %ERRORLEVEL% NEQ 0 (
    echo [Error] Process exited with code %ERRORLEVEL%
    pause
)
