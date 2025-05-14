using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using NuGetMcpServer.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NuGetMcpServer.Services;

[McpServerToolType]
public class InterfaceLookupService(ILogger<InterfaceLookupService> logger, HttpClient httpClient)
{
    // [McpServerTool, Description("Get the latest version of a NuGet package")]
    public async Task<string> GetLatestVersion(string packageId)
    {
        var indexUrl = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLower()}/index.json";
        logger.LogInformation("Fetching latest version for package {PackageId} from {Url}", packageId, indexUrl);

        var json = await httpClient.GetStringAsync(indexUrl);
        using var doc = JsonDocument.Parse(json);
        var versions = doc.RootElement
            .GetProperty("versions")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        return versions.Last();
    }


    /// <summary>
    /// Helper method to download a NuGet package and return it as a memory stream
    /// </summary>
    /// <param name="packageId">NuGet package ID</param>
    /// <param name="version">Package version</param>
    /// <returns>MemoryStream containing the package data</returns>
    private async Task<MemoryStream> DownloadPackageAsync(string packageId, string version)
    {
        // Download .nupkg
        var url = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLower()}/{version}/{packageId.ToLower()}.{version}.nupkg";
        logger.LogInformation("Downloading package from {Url}", url);
        
        var response = await httpClient.GetByteArrayAsync(url);
        return new MemoryStream(response);
    }

    /// <summary>
    /// Helper method to load and scan assemblies for interfaces directly from memory
    /// </summary>
    /// <param name="assemblyData">Assembly bytes</param>
    /// <returns>Assembly if successfully loaded, null otherwise</returns>
    private Assembly? LoadAssemblyFromMemory(byte[] assemblyData)
    {
        try
        {
            return Assembly.Load(assemblyData);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to load assembly from memory");
            return null;
        }
    }

    /// <summary>
    /// Formats interface definition as a string
    /// </summary>
    /// <param name="interfaceType">Interface type</param>
    /// <param name="assemblyName">Assembly name</param>
    /// <returns>Formatted interface definition</returns>
    private string FormatInterfaceDefinition(Type interfaceType, string assemblyName)
    {
        var sb = new StringBuilder()
            .AppendLine($"// from {assemblyName}")
            .AppendLine($"public interface {interfaceType.Name}")
            .AppendLine("{");

        foreach (var m in interfaceType.GetMethods())
        {
            var ps = string.Join(", ",
                m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            sb.AppendLine($"    {m.ReturnType.Name} {m.Name}({ps});");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }    /// <summary>
         /// Lists all public interfaces from a specified NuGet package
         /// </summary>
         /// <param name="packageId">NuGet package ID</param>
         /// <param name="version">Optional package version (defaults to latest)</param>
         /// <returns>Object containing package information and list of interfaces</returns>
    [McpServerTool,
     Description(
       "Lists all public interfaces available in a specified NuGet package. " +
       "Parameters: " +
       "packageId — NuGet package ID; " +
       "version (optional) — package version (defaults to latest). " +
       "Returns package ID, version and list of interfaces."
     )]
    public async Task<InterfaceListResult> ListInterfaces(
        string packageId,
        string? version = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(packageId))
                throw new ArgumentNullException(nameof(packageId));

            if (version.IsNullOrEmptyOrNullString())
            {
                version = await GetLatestVersion(packageId);
            }

            // Ensure we have non-null values for packageId and version
            packageId = packageId ?? string.Empty;
            version = version ?? string.Empty;

            logger.LogInformation("Listing interfaces from package {PackageId} version {Version}",
                packageId, version);

            var result = new InterfaceListResult
            {
                PackageId = packageId,
                Version = version,
                Interfaces = new List<InterfaceInfo>()
            };

            using var packageStream = await DownloadPackageAsync(packageId, version);
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
            
            // Scan each DLL in the package
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                
                try
                {
                    // Read the DLL into memory
                    using var entryStream = entry.Open();
                    using var ms = new MemoryStream();
                    await entryStream.CopyToAsync(ms);
                    
                    var assemblyData = ms.ToArray();
                    var assembly = LoadAssemblyFromMemory(assemblyData);
                    
                    if (assembly == null) continue;

                    var assemblyName = Path.GetFileName(entry.FullName);
                    var interfaces = assembly.GetTypes()
                        .Where(t => t.IsInterface && t.IsPublic)
                        .ToList();

                    foreach (var iface in interfaces)
                    {
                        result.Interfaces.Add(new InterfaceInfo
                        {
                            Name = iface.Name,
                            FullName = iface.FullName ?? string.Empty,
                            AssemblyName = assemblyName
                        });
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Error processing archive entry {EntryName}", entry.FullName);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing interfaces");
            throw;
        }
    }

    /// <summary>
    /// Extracts and returns the C# interface definition from a specified NuGet package.
    /// </summary>
    /// <param name="packageId">
    ///   The NuGet package ID (exactly as on nuget.org).
    /// </param>
    /// <param name="interfaceName">
    ///   Interface name without namespace.
    ///   If not specified, will search for all interfaces in the assembly.
    /// </param>
    /// <param name="version">
    ///   (Optional) Version of the package. If not specified, the latest version will be used.
    /// </param>
    [McpServerTool,
     Description(
       "Extracts and returns the C# interface definition from a specified NuGet package. " +
       "Parameters: " +
       "packageId — NuGet package ID; " +
       "version (optional) — package version (defaults to latest); " +
       "interfaceName (optional) — short interface name without namespace."
     )]
    public async Task<string> GetInterfaceDefinition(
        string packageId,
        string interfaceName,
        string? version = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(packageId))
                throw new ArgumentNullException(nameof(packageId));

            if (string.IsNullOrWhiteSpace(interfaceName))
                throw new ArgumentNullException(nameof(interfaceName));

            if (version.IsNullOrEmptyOrNullString())
            {
                version = await GetLatestVersion(packageId);
            }

            packageId = packageId ?? string.Empty;
            version = version ?? string.Empty;

            logger.LogInformation("Fetching interface {InterfaceName} from package {PackageId} version {Version}",
                interfaceName, packageId, version);

            using var packageStream = await DownloadPackageAsync(packageId, version);
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
            
            // Search in each DLL in the archive
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                
                try
                {
                    // Read the DLL into memory
                    using var entryStream = entry.Open();
                    using var ms = new MemoryStream();
                    await entryStream.CopyToAsync(ms);
                    
                    var assemblyData = ms.ToArray();
                    var assembly = LoadAssemblyFromMemory(assemblyData);
                    
                    if (assembly == null) continue;

                    var iface = assembly.GetTypes()
                        .FirstOrDefault(t => t.IsInterface && t.Name == interfaceName);

                    if (iface == null)
                        continue;

                    return FormatInterfaceDefinition(iface, Path.GetFileName(entry.FullName));
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Error processing archive entry {EntryName}", entry.FullName);
                }
            }

            return $"Interface '{interfaceName}' not found in package {packageId}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching interface definition");
            throw;
        }
    }
}
