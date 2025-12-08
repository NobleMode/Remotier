@echo off
echo Building Remotier...
dotnet build Remotier.slnx
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b %ERRORLEVEL%
)
echo Build success!
pause
