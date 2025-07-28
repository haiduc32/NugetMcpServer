using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using NuGet.Packaging;

using NuGetMcpServer.Common;
using NuGetMcpServer.Extensions;
using NuGetMcpServer.Services;

using static NuGetMcpServer.Extensions.ExceptionHandlingExtensions;

namespace NuGetMcpServer.Tools;

[McpServerToolType]
public class ListClassesTool(ILogger<ListClassesTool> logger, NuGetPackageService packageService) : McpToolBase<ListClassesTool>(logger, packageService)
{
    [McpServerTool]
    [Description("Lists all public classes available in a specified NuGet package.")]
    public Task<ClassListResult> list_classes(
        [Description("NuGet package ID")] string packageId,
        [Description("Package version (optional, defaults to latest)")] string? version = null,
        [Description("Progress notification for long-running operations")] IProgress<ProgressNotificationValue>? progress = null)
    {
        using var progressNotifier = new ProgressNotifier(progress);

        return ExecuteWithLoggingAsync(
            () => ListClassesCore(packageId, version, progressNotifier),
            Logger,
            "Error listing classes");
    }


    private async Task<ClassListResult> ListClassesCore(string packageId, string? version, IProgressNotifier progress)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentNullException(nameof(packageId));

        progress.ReportMessage("Resolving package version");

        if (version.IsNullOrEmptyOrNullString())
        {
            version = await PackageService.GetLatestVersion(packageId);
        }

        Logger.LogInformation("Listing classes from package {PackageId} version {Version}",
            packageId, version!);

        progress.ReportMessage($"Downloading package {packageId} v{version}");

        var result = new ClassListResult
        {
            PackageId = packageId,
            Version = version!,
            Classes = []
        };

        using var packageStream = await PackageService.DownloadPackageAsync(packageId, version!, progress);

        progress.ReportMessage("Extracting package information");
        var packageInfo = PackageService.GetPackageInfoAsync(packageStream, packageId, version!);

        result.IsMetaPackage = packageInfo.IsMetaPackage;
        result.Dependencies = packageInfo.Dependencies;
        result.Description = packageInfo.Description ?? string.Empty;

        progress.ReportMessage("Scanning assemblies for classes");
        packageStream.Position = 0;
        
        // Use the new metadata-only approach to avoid loading assemblies
        var classNames = PackageService.GetPackageClassesWithoutLoading(packageStream);
        
        foreach (var className in classNames)
        {
            // Parse the class information from the metadata
            var lastDotIndex = className.LastIndexOf('.');
            var name = lastDotIndex >= 0 ? className.Substring(lastDotIndex + 1) : className;
            
            result.Classes.Add(new ClassInfo
            {
                Name = name,
                FullName = className,
                AssemblyName = "Unknown", // Assembly name not available from metadata-only approach
                IsStatic = false, // These flags require reflection, not available from metadata-only
                IsAbstract = false,
                IsSealed = false
            });
        }

        progress.ReportMessage($"Class listing completed - Found {result.Classes.Count} classes");

        return result;
    }

}
