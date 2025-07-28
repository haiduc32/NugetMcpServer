using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NuGetMcpServer.Models;
using NuGetMcpServer.Services;
using NuGetMcpServer.Tools;

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

            Console.WriteLine($"NuGetMcpServer {version}");
            return 0;
        }

        // Check if HTTP transport is requested
        var useHttp = args.Any(a => a is "--http" or "-h");
        var port = GetPortFromArgs(args) ?? 5000;

        if (useHttp)
        {
            await RunHttpServer(args, port);
        }
        else
        {
            await RunStdioServer(args);
        }

        return 0;
    }

    private static int? GetPortFromArgs(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--port" or "-p" && int.TryParse(args[i + 1], out var port))
            {
                return port;
            }
        }
        return null;
    }

    private static async Task RunHttpServer(string[] args, int port)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        //builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        var a  = builder.Configuration.GetSection("NuGet");
        // Configure NuGet sources
        builder.Services.Configure<NuGetConfiguration>(
            builder.Configuration.GetSection("NuGet"));

        RegisterServices(builder.Services);

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly(typeof(ListInterfacesTool).Assembly);

        var app = builder.Build();

        app.MapMcp();

        Console.WriteLine($"NuGetMcpServer running on HTTP at http://localhost:{port}");
        app.Urls.Add($"http://localhost:{port}");

        await app.RunAsync();
    }

    private static async Task RunStdioServer(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        // Configure NuGet sources
        builder.Services.Configure<NuGetConfiguration>(
            builder.Configuration.GetSection("NuGet"));

        RegisterServices(builder.Services);

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(ListInterfacesTool).Assembly);

        await builder.Build().RunAsync();
    }

    private static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<NuGetHttpClientService>();
        services.AddSingleton<MetaPackageDetector>();
        services.AddSingleton<AzureDevOpsPackageService>();
        services.AddSingleton<NuGetPackageService>();
        services.AddSingleton<PackageSearchService>();
        services.AddSingleton<ArchiveProcessingService>();
        services.AddSingleton<InterfaceFormattingService>();
        services.AddSingleton<EnumFormattingService>();
        services.AddSingleton<ClassFormattingService>();
    }
}
