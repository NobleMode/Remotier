@echo off
echo Building Remotier...
msbuild Remotier.slnx -restore
if %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    pause
    exit /b %ERRORLEVEL%
)
echo Build success!
pause
