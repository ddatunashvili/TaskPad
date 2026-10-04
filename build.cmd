@echo off
dotnet build -c Release || exit /b 1
if not exist dist mkdir dist
copy /y bin\Release\net48\TaskPad.exe dist\TaskPad.exe >nul || (echo Close TaskPad first, the exe is in use. & exit /b 1)
echo Built dist\TaskPad.exe
