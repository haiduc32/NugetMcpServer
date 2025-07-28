using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using NuGet.Packaging;
using NuGet.Protocol.Core.Types;

using NuGetMcpServer.Extensions;
using NuGetMcpServer.Models;

namespace NuGetMcpServer.Services;

public class NuGetPackageServiceV2(ILogger<NuGetPackageServiceV2> logger, NuGetRepositoryService repositoryService, MetaPackageDetector metaPackageDetector, AzureDevOpsPackageService azureDevOpsPackageService)
{
    public async Task<string> GetLatestVersion(string packageId)
    {
        var versions = await GetPackageVersions(packageId);
        return versions.Last();
    }

    public async Task<IReadOnlyList<string>> GetPackageVersions(string packageId)
    {
        return await repositoryService.GetPackageVersionsAsync(packageId);
    }

    public async Task<IReadOnlyList<string>> GetLatestVersions(string packageId, int count = 20)
    {
        var versions = await GetPackageVersions(packageId);
        return versions.TakeLast(count).ToList();
    }

    public async Task<MemoryStream> DownloadPackageAsync(string packageId, string version, IProgressNotifier? progress = null)
    {
        var sources = repositoryService.GetEnabledSources();
        
        foreach (var source in sources)
        {
            try
            {
                // Use Azure DevOps API if this is an Azure DevOps feed
                if (source.IsAzureDevOps)
                {
                    logger.LogInformation("Downloading package from Azure DevOps feed {SourceName}", source.Name);
                    progress?.ReportMessage($"Starting package download {packageId} v{version} from Azure DevOps feed {source.Name}");

                    // For Azure DevOps, we still need to use the HTTP client approach for now
                    // since NuGet.Protocol doesn't handle Azure DevOps native APIs
                    logger.LogWarning("Azure DevOps support requires HTTP client fallback - this functionality needs to be migrated");
                    continue;
                }
                
                // Use NuGet.Protocol for standard feeds
                progress?.ReportMessage($"Starting package download {packageId} v{version} from {source.Name}");

                using var packageStream = await repositoryService.DownloadPackageAsync(packageId, version);
                var memoryStream = new MemoryStream();
                await packageStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                progress?.ReportMessage("Package downloaded successfully");
                return memoryStream;
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
            Assembly? assembly = LoadAssemblyFromMemory(assemblyData);
            if (assembly == null) return (null, []);

            Type[] types = assembly.GetTypes();
            return (assembly, types);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load assembly and extract types from memory");
            return (null, []);
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
            logger.LogWarning(ex, "Failed to load assembly from memory");
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
        var results = new List<PackageInfo>();
        var sources = repositoryService.GetEnabledSources();

        foreach (var source in sources)
        {
            try
            {
                var repository = repositoryService.GetRepository(source.Name);
                var searchResource = await repository.GetResourceAsync<PackageSearchResource>();
                
                var searchFilter = new SearchFilter(includePrerelease: false)
                {
                    SupportedFrameworks = []
                };

                var searchResults = await searchResource.SearchAsync(
                    query,
                    searchFilter,
                    skip: 0,
                    take: take,
                    new NuGet.Common.NullLogger(),
                    cancellationToken: default);

                foreach (var result in searchResults)
                {
                    var packageInfo = new PackageInfo
                    {
                        PackageId = result.Identity.Id,
                        Version = result.Identity.Version.ToString(),
                        Description = result.Description ?? string.Empty,
                        Authors = result.Authors?.Split(',').Select(a => a.Trim()).ToList() ?? [],
                        Tags = result.Tags?.Split(' ', ',').Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [],
                        ProjectUrl = result.ProjectUrl?.ToString() ?? string.Empty,
                        LicenseUrl = result.LicenseUrl?.ToString() ?? string.Empty,
                        IsMetaPackage = false, // Will be determined later if needed
                        Dependencies = []
                    };

                    results.Add(packageInfo);
                }

                if (results.Any())
                {
                    logger.LogInformation("Found {ResultCount} packages for query '{Query}' from {SourceName}", 
                        results.Count, query, source.Name);
                    break; // Use first successful source
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to search packages from source {SourceName}, trying next source", source.Name);
            }
        }

        return results;
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
                Authors = [],
                Tags = [],
                ProjectUrl = string.Empty,
                LicenseUrl = string.Empty,
                IsMetaPackage = false,
                Dependencies = []
            };
        }
    }
}
