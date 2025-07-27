using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using NuGet.Packaging;

using NuGetMcpServer.Extensions;
using NuGetMcpServer.Models;

namespace NuGetMcpServer.Services;

public class NuGetPackageService(ILogger<NuGetPackageService> logger, NuGetHttpClientService httpClientService, MetaPackageDetector metaPackageDetector)
{

    public async Task<string> GetLatestVersion(string packageId)
    {
        IReadOnlyList<string> versions = await GetPackageVersions(packageId);
        return versions.Last();
    }

    public async Task<IReadOnlyList<string>> GetPackageVersions(string packageId)
    {
        var sources = httpClientService.GetEnabledSources();
        
        foreach (var source in sources)
        {
            try
            {
                string indexUrl = $"{source.Url.TrimEnd('/')}/{packageId.ToLower()}/index.json";
                logger.LogInformation("Fetching versions for package {PackageId} from {SourceName} at {Url}", packageId, source.Name, indexUrl);
                
                var httpClient = httpClientService.GetHttpClient(source.Name);
                string json = await httpClient.GetStringAsync(indexUrl);
                using JsonDocument doc = JsonDocument.Parse(json);

                JsonElement versionsArray = doc.RootElement.GetProperty("versions");
                var versions = new List<string>();

                foreach (JsonElement element in versionsArray.EnumerateArray())
                {
                    string? version = element.GetString();
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        versions.Add(version);
                    }
                }

                logger.LogInformation("Found {VersionCount} versions for package {PackageId} from {SourceName}", versions.Count, packageId, source.Name);
                return versions;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get package versions from source {SourceName}, trying next source", source.Name);
            }
        }
        
        throw new InvalidOperationException($"Failed to get package versions for {packageId} from all configured sources");
    }

    public async Task<IReadOnlyList<string>> GetLatestVersions(string packageId, int count = 20)
    {
        IReadOnlyList<string> versions = await GetPackageVersions(packageId);
        return versions.TakeLast(count).ToList();
    }

    public async Task<MemoryStream> DownloadPackageAsync(string packageId, string version, IProgressNotifier? progress = null)
    {
        var sources = httpClientService.GetEnabledSources();
        
        foreach (var source in sources)
        {
            try
            {
                string url = $"{source.Url.TrimEnd('/')}/{packageId.ToLower()}/{version}/{packageId.ToLower()}.{version}.nupkg";
                logger.LogInformation("Downloading package from {SourceName} at {Url}", source.Name, url);

                progress?.ReportMessage($"Starting package download {packageId} v{version} from {source.Name}");

                var httpClient = httpClientService.GetHttpClient(source.Name);
                byte[] response = await httpClient.GetByteArrayAsync(url);

                progress?.ReportMessage("Package downloaded successfully");

                return new MemoryStream(response);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to download package from source {SourceName}, trying next source", source.Name);
            }
        }
        
        throw new InvalidOperationException($"Failed to download package {packageId} v{version} from all configured sources");
    }

    public (Assembly? assembly, Type[] types) LoadAssemblyFromMemoryWithTypes(byte[] assemblyData)
    {
        try
        {
            var assembly = Assembly.Load(assemblyData);

            try
            {
                var types = assembly.GetTypes();
                return (assembly, types);
            }
            catch (ReflectionTypeLoadException ex)
            {
                logger.LogWarning("Some types could not be loaded from assembly due to missing dependencies. Loaded {LoadedCount} out of {TotalCount} types",
                    ex.Types.Count(t => t != null), ex.Types.Length);

                var loadedTypes = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
                return (assembly, loadedTypes);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load assembly from memory. Assembly size: {Size} bytes", assemblyData.Length);
            return (null, Array.Empty<Type>());
        }
    }


    public Assembly? LoadAssemblyFromMemory(byte[] assemblyData)
    {
        try
        {
            return Assembly.Load(assemblyData);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load assembly from memory. Assembly size: {Size} bytes", assemblyData.Length);
            return null;
        }
    }


    public List<PackageDependency> GetPackageDependencies(Stream packageStream)
    {
        try
        {
            packageStream.Position = 0;
            using var reader = new PackageArchiveReader(packageStream, leaveStreamOpen: true);
            using var nuspecStream = reader.GetNuspec();
            var nuspecReader = new NuspecReader(nuspecStream);
            var dependencyGroups = nuspecReader.GetDependencyGroups();

            var dependencies = dependencyGroups
                .SelectMany(group => group.Packages.Select(package => new PackageDependency
                {
                    Id = package.Id,
                    Version = package.VersionRange?.ToString() ?? "latest"
                }))
                .DistinctBy(d => d.Id)
                .ToList();

            return dependencies;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error extracting package dependencies using NuGet API, falling back to manual parsing");
            return [];
        }
    }

    public async Task<IReadOnlyCollection<PackageInfo>> SearchPackagesAsync(string query, int take = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        string searchUrl = $"https://azuresearch-usnc.NuGet.org/query" +
                       $"?q={Uri.EscapeDataString(query)}" +
                       $"&take={take}" +
                       $"&sortBy=popularity-desc";

        logger.LogInformation("Searching packages with query '{Query}' from {Url}", query, searchUrl);

        var primarySource = httpClientService.GetPrimarySource();
        var httpClient = httpClientService.GetHttpClient(primarySource.Name);
        var json = await httpClient.GetStringAsync(searchUrl);
        using JsonDocument doc = JsonDocument.Parse(json);
        List<PackageInfo> packages = [];
        JsonElement dataArray = doc.RootElement.GetProperty("data");

        foreach (JsonElement packageElement in dataArray.EnumerateArray())
        {
            PackageInfo packageInfo = new()
            {
                PackageId = packageElement.GetProperty("id").GetString() ?? string.Empty,
                Version = packageElement.GetProperty("version").GetString() ?? string.Empty,
                Description = packageElement.TryGetProperty("description", out JsonElement desc) ? desc.GetString() : null,
                DownloadCount = packageElement.TryGetProperty("totalDownloads", out JsonElement downloads) ? downloads.GetInt64() : 0,
                ProjectUrl = packageElement.TryGetProperty("projectUrl", out JsonElement projectUrl) ? projectUrl.GetString() : null
            };

            // Extract tags
            if (packageElement.TryGetProperty("tags", out JsonElement tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
            {
                packageInfo.Tags = tagsElement.EnumerateArray()
                    .Where(t => t.ValueKind == JsonValueKind.String)
                    .Select(t => t.GetString()!)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();
            }

            // Extract authors
            if (packageElement.TryGetProperty("authors", out JsonElement authorsElement) && authorsElement.ValueKind == JsonValueKind.Array)
            {
                packageInfo.Authors = authorsElement.EnumerateArray()
                    .Where(a => a.ValueKind == JsonValueKind.String)
                    .Select(a => a.GetString()!)
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList();
            }
            packages.Add(packageInfo);
        }

        return packages.OrderByDescending(p => p.DownloadCount).ToList();
    }

    public PackageInfo GetPackageInfoAsync(Stream packageStream, string packageId, string version)
    {
        try
        {
            var isMetaPackage = metaPackageDetector.IsMetaPackage(packageStream, packageId);
            var dependencies = GetPackageDependencies(packageStream);

            packageStream.Position = 0;
            using var reader = new PackageArchiveReader(packageStream, leaveStreamOpen: true);
            using var nuspecStream = reader.GetNuspec();
            var nuspecReader = new NuspecReader(nuspecStream);

            var authors = nuspecReader.GetAuthors()?.Split(',').Select(a => a.Trim()).ToList() ?? [];
            var tags = nuspecReader.GetTags()?.Split(' ', ',').Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];

            return new PackageInfo
            {
                PackageId = packageId,
                Version = version,
                Description = nuspecReader.GetDescription() ?? string.Empty,
                Authors = authors,
                Tags = tags,
                ProjectUrl = nuspecReader.GetProjectUrl()?.ToString() ?? string.Empty,
                LicenseUrl = nuspecReader.GetLicenseUrl()?.ToString() ?? string.Empty,
                IsMetaPackage = isMetaPackage,
                Dependencies = dependencies
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting package info for {PackageId} v{Version}", packageId, version);
            return new PackageInfo
            {
                PackageId = packageId,
                Version = version,
                Description = "Error retrieving package information",
                IsMetaPackage = false,
                Dependencies = []
            };
        }
    }
}
