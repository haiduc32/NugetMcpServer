@echo off
REM filepath: c:\Private\NugetMcpServer\build-and-package.bat

echo Building and publishing NugetMcpServer...
dotnet publish NugetMcpServer/NugetMcpServer.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -p:PublishTrimmed=false -o .\publish

echo Creating versioned release archive...
powershell -ExecutionPolicy Bypass -File .\package-release.ps1

echo Done!