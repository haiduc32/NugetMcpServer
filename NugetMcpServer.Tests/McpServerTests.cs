using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGetMcpServer.Services;
using System.Net.Http;
using Xunit;
using Xunit.Abstractions;

namespace NugetMcpServer.Tests
{
    public class McpServerTests
    {
        private readonly ITestOutputHelper _testOutput;

        public McpServerTests(ITestOutputHelper testOutput)
        {
            _testOutput = testOutput;
        }

        [Fact]
        public async Task CanListInterfacesFromMazeGeneratorPackage()
        {
            // Create the service manually
            var httpClient = new HttpClient();
            var logger = new TestLogger<InterfaceLookupService>(_testOutput);
            var service = new InterfaceLookupService(logger, httpClient);
            
            _testOutput.WriteLine("Calling ListInterfaces on DimonSmart.MazeGenerator package...");
              try
            {
                // Call the service directly to list interfaces from the DimonSmart.MazeGenerator package
                var result = await service.ListInterfaces("DimonSmart.MazeGenerator");
                
                // Validate the response
                Assert.NotNull(result);
                Assert.Equal("DimonSmart.MazeGenerator", result.PackageId);
                Assert.NotEmpty(result.Version);
                Assert.NotEmpty(result.Interfaces);
                
                _testOutput.WriteLine($"Found {result.Interfaces.Count} interfaces in {result.PackageId} version {result.Version}");
                
                // Output details about found interfaces
                foreach (var iface in result.Interfaces)
                {
                    _testOutput.WriteLine($"- {iface.FullName} ({iface.AssemblyName})");
                }
                
                // Verify we found expected interfaces
                // Note: We're checking for the presence of any interface with "IMaze" prefix
                // as this is a common naming convention for maze generator interfaces
                Assert.Contains(result.Interfaces, i => i.Name.StartsWith("IMaze") || i.FullName.Contains(".IMaze"));
                
                // Get a specific interface definition if available
                var mazeInterface = result.Interfaces.FirstOrDefault(i => i.Name.StartsWith("IMaze"));
                if (mazeInterface != null)
                {
                    _testOutput.WriteLine($"\nFetching interface definition for {mazeInterface.Name}");
                    var definition = await service.GetInterfaceDefinition(
                        "DimonSmart.MazeGenerator", 
                        mazeInterface.Name, 
                        result.Version);
                    
                    _testOutput.WriteLine($"\nInterface definition:\n{definition}");
                    
                    // Verify we got a valid interface definition
                    Assert.Contains("interface", definition);
                    Assert.Contains(mazeInterface.Name, definition);
                }
            }
            catch (Exception ex) 
            {
                _testOutput.WriteLine($"Error occurred: {ex.Message}");
                _testOutput.WriteLine(ex.StackTrace);
                throw;
            }
        }
    }

    // Simple test logger implementation
    public class TestLogger<T> : ILogger<T>
    {
        private readonly ITestOutputHelper _output;
        
        public TestLogger(ITestOutputHelper output)
        {
            _output = output;
        }
        
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }
        
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }
        
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
            
            if (exception != null)
            {
                _output.WriteLine($"Exception: {exception.Message}");
                _output.WriteLine(exception.StackTrace);
            }
        }
    }
}
