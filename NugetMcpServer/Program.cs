using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NuGetMcpServer.Services;
using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Any(a => a is "--version" or "-v"))
        {
            var asm = Assembly.GetExecutingAssembly();
            var version =
                asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? asm.GetName().Version?.ToString()
                ?? "unknown";

            Console.WriteLine($"NugetMcpServer {version}");
            return 0;
        }

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
            .WithToolsFromAssembly(typeof(InterfaceLookupService).Assembly);

        // Register HttpClient for InterfaceLookupService
        builder.Services.AddSingleton<HttpClient>();

        await builder.Build().RunAsync();
        return 0;
    }
}