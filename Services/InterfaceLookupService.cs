using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace LocalMcpServer.Services;

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
    /// Extracts and returns the C# interface definition from a specified NuGet package.
    /// </summary>
    /// <param name="packageId">
    ///   The NuGet package ID (exactly as on nuget.org).
    /// </param>
    /// <param name="interfaceName">
    ///   Interface name without namespace.
    ///   If not specified, will search for all interfaces in the assembly.
    /// </param>
    /// /// <param name="version">
    ///   (Optional) Version of the package. If not specified, the latest version will be used.
    /// </param>
    [McpServerTool,
     Description(
       "Extracts and returns the C# interface definition from a specified NuGet package. " +
       "Parameters: " +
       "packageId � NuGet package ID; " +
       "version (optional) � package version (defaults to latest); " +
       "interfaceName (optional) � short interface name without namespace."
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

            // Create temporary paths with GUID to ensure uniqueness
            var runId = Guid.NewGuid().ToString("N");
            var tmpFolder = Path.Combine(Path.GetTempPath(), $"{packageId}-{version}-{runId}");
            var nupkgFile = Path.Combine(Path.GetTempPath(), $"{packageId}-{version}-{runId}.nupkg");
            Directory.CreateDirectory(tmpFolder);

            try
            {
                // 1. Download .nupkg
                var url = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLower()}/{version}/{packageId.ToLower()}.{version}.nupkg";
                using (var stream = await httpClient.GetStreamAsync(url))
                using (var fs = File.Create(nupkgFile))
                await stream.CopyToAsync(fs);

                // 2. Extract package
                ZipFile.ExtractToDirectory(nupkgFile, tmpFolder);

                // 3. Search in each DLL
                foreach (var dll in Directory.EnumerateFiles(tmpFolder, "*.dll", SearchOption.AllDirectories))
                {
                    byte[] raw;
                    try
                    {
                        raw = File.ReadAllBytes(dll);
                    }
                    catch
                    {
                        continue;
                    }

                    Assembly asm;
                    try
                    {
                        asm = Assembly.Load(raw);
                    }
                    catch
                    {
                        continue;
                    }

                    var iface = asm.GetTypes()
                                   .FirstOrDefault(t => t.IsInterface && t.Name == interfaceName);
                    if (iface == null)
                        continue;

                    // Build interface signature
                    var sb = new StringBuilder()
                        .AppendLine($"// from {Path.GetFileName(dll)}")
                        .AppendLine($"public interface {iface.Name}")
                        .AppendLine("{");

                    foreach (var m in iface.GetMethods())
                    {
                        var ps = string.Join(", ",
                            m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                        sb.AppendLine($"    {m.ReturnType.Name} {m.Name}({ps});");
                    }

                    sb.AppendLine("}");
                    return sb.ToString();
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
