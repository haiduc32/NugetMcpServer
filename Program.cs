using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http;

var builder = Host.CreateApplicationBuilder(args);

// Console logging (stderr)
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Register MCP server and STDIO transport
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// Register HttpClient for InterfaceLookupService
builder.Services.AddSingleton<HttpClient>();

await builder.Build().RunAsync();
