# Get version from executable and create versioned archive
$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(".\publish\NugetMcpServer.exe")
$version = "$($versionInfo.FileMajorPart).$($versionInfo.FileMinorPart).$($versionInfo.FileBuildPart)"
$archiveName = ".\Winget\nuget-mcp-server-win-x64-$version.zip"

# Ensure Winget directory exists
if (-not (Test-Path -Path ".\Winget")) {
    New-Item -ItemType Directory -Path ".\Winget" | Out-Null
}

# Create the archive with version in filename
Compress-Archive -Path .\publish\NugetMcpServer.exe -DestinationPath $archiveName -Force

Write-Host "Created archive: $archiveName with version $version" -ForegroundColor Green
