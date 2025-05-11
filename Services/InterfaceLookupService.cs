using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
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

/// <summary>
/// Model for interface information
/// </summary>
public class InterfaceInfo
{
    /// <summary>
    /// Interface name (without namespace)
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Full interface name with namespace
    /// </summary>
    public string FullName { get; set; } = string.Empty;
    
    /// <summary>
    /// Assembly name where interface is defined
    /// </summary>
    public string AssemblyName { get; set; } = string.Empty;
}

/// <summary>
/// Response model for interface listing including package version information
/// </summary>
public class InterfaceListResult
{
    /// <summary>
    /// NuGet package ID
    /// </summary>
    public string PackageId { get; set; } = string.Empty;
    
    /// <summary>
    /// Package version
    /// </summary>
    public string Version { get; set; } = string.Empty;
    
    /// <summary>
    /// List of interfaces found in the package
    /// </summary>
    public List<InterfaceInfo> Interfaces { get; set; } = [];
}

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
    }    /// <summary>
    /// Helper method to download and extract a NuGet package
    /// </summary>
    /// <param name="packageId">NuGet package ID</param>
    /// <param name="version">Package version</param>
    /// <returns>Tuple containing temp folder path and nupkg file path</returns>
    private async Task<(string TempFolder, string NupkgFile)> DownloadAndExtractPackageAsync(string packageId, string version)
    {
        // Create temporary paths with GUID to ensure uniqueness
        var runId = Guid.NewGuid().ToString("N");
        var tmpFolder = Path.Combine(Path.GetTempPath(), $"{packageId}-{version}-{runId}");
        var nupkgFile = Path.Combine(Path.GetTempPath(), $"{packageId}-{version}-{runId}.nupkg");
        Directory.CreateDirectory(tmpFolder);

        // 1. Download .nupkg
        var url = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLower()}/{version}/{packageId.ToLower()}.{version}.nupkg";
        using (var stream = await httpClient.GetStreamAsync(url))
        using (var fs = File.Create(nupkgFile))
            await stream.CopyToAsync(fs);

        // 2. Extract package
        ZipFile.ExtractToDirectory(nupkgFile, tmpFolder);

        return (tmpFolder, nupkgFile);
    }

    /// <summary>
    /// Helper method to load and scan assemblies for interfaces
    /// </summary>
    /// <param name="dllPath">Path to DLL file</param>
    /// <returns>Assembly if successfully loaded, null otherwise</returns>
    private Assembly? LoadAssembly(string dllPath)
    {
        try
        {
            var raw = File.ReadAllBytes(dllPath);
            return Assembly.Load(raw);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to load assembly: {DllPath}", dllPath);
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

            if (string.IsNullOrEmpty(version))
            {
                version = await GetLatestVersion(packageId);
            }

            logger.LogInformation("Listing interfaces from package {PackageId} version {Version}",
                packageId, version);

            var result = new InterfaceListResult
            {
                PackageId = packageId,
                Version = version,
                Interfaces = new List<InterfaceInfo>()
            };
            
            var (tmpFolder, nupkgFile) = await DownloadAndExtractPackageAsync(packageId, version);

            try
            {
                // Scan each DLL in the package
                foreach (var dll in Directory.EnumerateFiles(tmpFolder, "*.dll", SearchOption.AllDirectories))
                {
                    var assembly = LoadAssembly(dll);
                    if (assembly == null) continue;

                    var assemblyName = Path.GetFileName(dll);
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

                return result;
            }
            finally
            {
                // Cleanup: delete folder and .nupkg
                try { Directory.Delete(tmpFolder, true); } catch { /* ignore */ }
                try { File.Delete(nupkgFile); } catch { /* ignore */ }
            }
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

            if (string.IsNullOrEmpty(version))
            {
                version = await GetLatestVersion(packageId);
            }

            logger.LogInformation("Fetching interface {InterfaceName} from package {PackageId} version {Version}",
                interfaceName, packageId, version);

            var (tmpFolder, nupkgFile) = await DownloadAndExtractPackageAsync(packageId, version);

            try
            {
                // Search in each DLL
                foreach (var dll in Directory.EnumerateFiles(tmpFolder, "*.dll", SearchOption.AllDirectories))
                {
                    var assembly = LoadAssembly(dll);
                    if (assembly == null) continue;

                    var iface = assembly.GetTypes()
                        .FirstOrDefault(t => t.IsInterface && t.Name == interfaceName);
                    
                    if (iface == null)
                        continue;

                    return FormatInterfaceDefinition(iface, Path.GetFileName(dll));
                }

                return $"Interface '{interfaceName}' not found in package {packageId}.";
            }
            finally
            {
                // Cleanup: delete folder and .nupkg
                try { Directory.Delete(tmpFolder, true); } catch { /* ignore */ }
                try { File.Delete(nupkgFile); } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching interface definition");
            throw;
        }
    }
}
