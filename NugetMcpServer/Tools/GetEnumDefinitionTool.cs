using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using NuGetMcpServer.Common;
using NuGetMcpServer.Extensions;
using NuGetMcpServer.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using static NuGetMcpServer.Extensions.ExceptionHandlingExtensions;

namespace NuGetMcpServer.Tools;

[McpServerToolType]
public class GetEnumDefinitionTool(
    ILogger<GetEnumDefinitionTool> logger,
    NuGetPackageService packageService,
    EnumFormattingService formattingService) : McpToolBase<GetEnumDefinitionTool>(logger, packageService)
{

    /// <summary>
    /// Extracts and returns the C# enum definition from a specified NuGet package.
    /// </summary>
    /// <param name="packageId">
    ///   The NuGet package ID (exactly as on nuget.org).
    /// </param>
    /// <param name="enumName">
    ///   Enum name without namespace.
    ///   If not specified, will search for all enums in the assembly.
    /// </param>
    /// <param name="version">
    ///   (Optional) Version of the package. If not specified, the latest version will be used.
    /// </param>
    [McpServerTool]
    [Description(
       "Extracts and returns the C# enum definition from a specified NuGet package. " +
       "Parameters: " +
       "packageId — NuGet package ID; " +
       "version (optional) — package version (defaults to latest); " +
       "enumName — short enum name without namespace."
    )]
    public Task<string> GetEnumDefinition(
        string packageId,
        string enumName,
        string? version = null)
    {
        return ExecuteWithLoggingAsync(
            () => GetEnumDefinitionCore(packageId, enumName, version),
            Logger,
            "Error fetching enum definition");
    }

    private async Task<string> GetEnumDefinitionCore(
        string packageId,
        string enumName,
        string? version)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentNullException(nameof(packageId));
        }

        if (string.IsNullOrWhiteSpace(enumName))
        {
            throw new ArgumentNullException(nameof(enumName));
        }

        if (version.IsNullOrEmptyOrNullString())
        {
            version = await PackageService.GetLatestVersion(packageId);
        }

        packageId = packageId ?? string.Empty;
        version = version ?? string.Empty;

        Logger.LogInformation("Fetching enum {EnumName} from package {PackageId} version {Version}",
            enumName, packageId, version);

        using var packageStream = await PackageService.DownloadPackageAsync(packageId, version);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);

        // Search in each DLL in the archive
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var definition = await TryGetEnumFromEntry(entry, enumName);
            if (definition != null)
            {
                return definition;
            }
        }

        return $"Enum '{enumName}' not found in package {packageId}.";
    }
    private async Task<string?> TryGetEnumFromEntry(ZipArchiveEntry entry, string enumName)
    {
        try
        {
            var assembly = await LoadAssemblyFromEntryAsync(entry);

            if (assembly == null)
            {
                return null;
            }

            var enumType = assembly.GetTypes()
                .FirstOrDefault(t => t.IsEnum && t.Name == enumName);

            if (enumType == null)
            {
                return null;
            }

            var assemblyName = Path.GetFileName(entry.FullName);
            return $"/* C# ENUM FROM {assemblyName} */\r\n" + formattingService.FormatEnumDefinition(enumType);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Error processing archive entry {EntryName}", entry.FullName);
            return null;
        }
    }
}
