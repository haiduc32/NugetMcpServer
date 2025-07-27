using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NuGetMcpServer.Models;
using NuGetMcpServer.Services;

using Xunit.Abstractions;

namespace NuGetMcpServer.Tests.Helpers;

public abstract class TestBase(ITestOutputHelper testOutput)
{
    protected readonly ITestOutputHelper TestOutput = testOutput;

    protected MetaPackageDetector CreateMetaPackageDetector()
    {
        return new MetaPackageDetector(NullLogger<MetaPackageDetector>.Instance);
    }

    protected NuGetHttpClientService CreateNuGetHttpClientService()
    {
        var configuration = new NuGetConfiguration
        {
            Sources =
            [
                new NuGetSourceConfiguration
                {
                    Name = "nuget.org",
                    Url = "https://api.nuget.org/v3-flatcontainer/",
                    IsEnabled = true,
                    Priority = 100
                }
            ],
            DefaultTimeoutSeconds = 30,
            MaxRetryAttempts = 3
        };
        
        var options = Options.Create(configuration);
        return new NuGetHttpClientService(NullLogger<NuGetHttpClientService>.Instance, options);
    }

    protected NuGetPackageService CreateNuGetPackageService()
    {
        var httpClientService = CreateNuGetHttpClientService();
        var metaPackageDetector = CreateMetaPackageDetector();
        var azureDevOpsService = new AzureDevOpsPackageService(NullLogger<AzureDevOpsPackageService>.Instance);
        return new NuGetPackageService(NullLogger<NuGetPackageService>.Instance, httpClientService, metaPackageDetector, azureDevOpsService);
    }

    protected ArchiveProcessingService CreateArchiveProcessingService()
    {
        var packageService = CreateNuGetPackageService();
        return new ArchiveProcessingService(NullLogger<ArchiveProcessingService>.Instance, packageService);
    }

    protected static async Task ExecuteWithCleanupAsync(Func<Task> operation, Action cleanup)
    {
        try
        {
            await operation();
        }
        finally
        {
            cleanup();
        }
    }

    protected static void ExecuteWithErrorHandling(Action action, Action<Exception>? exceptionHandler = null)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            exceptionHandler?.Invoke(ex);
        }
    }
}
