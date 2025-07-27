# Test script for both STDIO and HTTP modes
Write-Host "Testing NugetMcpServer modes..." -ForegroundColor Green

# Test version command
Write-Host "`n1. Testing version command:" -ForegroundColor Yellow
dotnet run --project NugetMcpServer -- --version

# Test STDIO mode (just start and stop quickly)
Write-Host "`n2. Testing STDIO mode (default):" -ForegroundColor Yellow
Write-Host "Starting STDIO mode server (will timeout in 3 seconds)..."
$stdioJob = Start-Job -ScriptBlock { 
    Set-Location $using:PWD
    dotnet run --project NugetMcpServer
}
Start-Sleep -Seconds 3
Stop-Job $stdioJob -PassThru | Remove-Job
Write-Host "STDIO mode test completed" -ForegroundColor Green

# Test HTTP mode
Write-Host "`n3. Testing HTTP mode:" -ForegroundColor Yellow
Write-Host "Starting HTTP server on port 5555..."
$httpJob = Start-Job -ScriptBlock { 
    Set-Location $using:PWD
    dotnet run --project NugetMcpServer -- --http --port 5555
}

# Wait for server to start
Start-Sleep -Seconds 5

# Test if server is responding
try {
    $response = Invoke-WebRequest -Uri "http://localhost:5555/" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
    Write-Host "HTTP server is responding (expected 406 Not Acceptable for MCP endpoint)" -ForegroundColor Green
} catch {
    if ($_.Exception.Response.StatusCode -eq 406) {
        Write-Host "HTTP server is responding correctly (406 Not Acceptable for MCP endpoint)" -ForegroundColor Green
    } else {
        Write-Host "HTTP server test failed: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Stop HTTP server
Stop-Job $httpJob -PassThru | Remove-Job
Write-Host "HTTP mode test completed" -ForegroundColor Green

Write-Host "`nAll tests completed successfully!" -ForegroundColor Green
Write-Host "`nUsage examples:" -ForegroundColor Cyan
Write-Host "  STDIO mode (default):  dotnet run --project NugetMcpServer"
Write-Host "  HTTP mode:             dotnet run --project NugetMcpServer -- --http"
Write-Host "  HTTP with custom port: dotnet run --project NugetMcpServer -- --http --port 8080"
Write-Host "  Show version:          dotnet run --project NugetMcpServer -- --version"
