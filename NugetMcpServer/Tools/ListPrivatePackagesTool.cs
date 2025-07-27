using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using NuGetMcpServer.Common;
using NuGetMcpServer.Extensions;
using NuGetMcpServer.Services;

using static NuGetMcpServer.Extensions.ExceptionHandlingExtensions;

namespace NuGetMcpServer.Tools;

[McpServerToolType]
public class ListPrivatePackagesTool(ILogger<ListPrivatePackagesTool> logger, NuGetHttpClientService httpClientService, AzureDevOpsPackageService azureDevOpsPackageService, NuGetPackageService packageService) : McpToolBase<ListPrivatePackagesTool>(logger, null!)
{
    [McpServerTool]
    [Description("Lists packages exclusively from private NuGet feeds (feeds with authentication) and Azure DevOps feeds. This tool only searches private/internal package repositories, not public feeds like nuget.org.")]
    public Task<PrivatePackageListResult> list_private_packages(
        [Description("Optional search term to filter packages by name (default: empty to list all packages)")] string? searchTerm = null,
        [Description("Maximum number of results to return (default: 50, max: 200)")] int maxResults = 50,
        [Description("Include package versions in the results (default: false for better performance)")] bool includeVersions = false,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var progressNotifier = new ProgressNotifier(progress);

        return ExecuteWithLoggingAsync(
            () => ListPrivatePackagesCore(searchTerm ?? "", maxResults, includeVersions, progressNotifier, cancellationToken),
            Logger,
            "Error listing private packages");
    }

    private async Task<PrivatePackageListResult> ListPrivatePackagesCore(
        string searchTerm,
        int maxResults,
        bool includeVersions,
        ProgressNotifier progress,
        CancellationToken cancellationToken)
    {
        maxResults = Math.Min(Math.Max(maxResults, 1), 200);

        Logger.LogInformation("Starting private package listing with search term: '{SearchTerm}', maxResults: {MaxResults}, includeVersions: {IncludeVersions}", 
            searchTerm, maxResults, includeVersions);

        progress.ReportMessage("Getting private feed sources");

        var privateSources = GetPrivateSources();
        if (!privateSources.Any())
        {
            Logger.LogInformation("No private sources configured");
            return new PrivatePackageListResult
            {
                SearchTerm = searchTerm,
                TotalPackages = 0,
                Sources = [],
                Packages = []
            };
        }

        Logger.LogInformation("Found {Count} private sources: {Sources}", 
            privateSources.Count(), string.Join(", ", privateSources.Select(s => s.Name)));

        var allPackages = new List<PrivatePackageInfo>();
        var sourceResults = new List<PrivateSourceResult>();

        foreach (var source in privateSources)
        {
            try
            {
                progress.ReportMessage($"Searching packages in {source.Name}");
                cancellationToken.ThrowIfCancellationRequested();

                var sourcePackages = await GetPackagesFromSource(source, searchTerm, maxResults, includeVersions, cancellationToken);
                
                sourceResults.Add(new PrivateSourceResult
                {
                    SourceName = source.Name,
                    SourceType = GetSourceType(source),
                    PackageCount = sourcePackages.Count,
                    IsAzureDevOps = source.IsAzureDevOps
                });

                allPackages.AddRange(sourcePackages);

                Logger.LogInformation("Found {Count} packages from source '{SourceName}'", sourcePackages.Count, source.Name);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error getting packages from source '{SourceName}'", source.Name);
                
                sourceResults.Add(new PrivateSourceResult
                {
                    SourceName = source.Name,
                    SourceType = GetSourceType(source),
                    PackageCount = 0,
                    IsAzureDevOps = source.IsAzureDevOps,
                    Error = ex.Message
                });
            }
        }

        // Remove duplicates based on package ID and prioritize by source priority
        var uniquePackages = allPackages
            .GroupBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(p => p.SourcePriority).First())
            .OrderBy(p => p.PackageId)
            .Take(maxResults)
            .ToList();

        progress.ReportMessage("Completed private package listing");

        return new PrivatePackageListResult
        {
            SearchTerm = searchTerm,
            TotalPackages = uniquePackages.Count,
            Sources = sourceResults,
            Packages = uniquePackages
        };
    }

    private IEnumerable<Models.NuGetSourceConfiguration> GetPrivateSources()
    {
        return httpClientService.GetEnabledSources()
            .Where(IsPrivateSource)
            .OrderByDescending(s => s.Priority);
    }

    private static bool IsPrivateSource(Models.NuGetSourceConfiguration source)
    {
        // A source is considered private if:
        // 1. It's an Azure DevOps feed, OR
        // 2. It has authentication (username/password or API key), OR  
        // 3. It's not the default public nuget.org feed
        return source.IsAzureDevOps ||
               !string.IsNullOrWhiteSpace(source.Username) ||
               !string.IsNullOrWhiteSpace(source.Password) ||
               !string.IsNullOrWhiteSpace(source.ApiKey) ||
               (!source.Url.Contains("nuget.org", StringComparison.OrdinalIgnoreCase) &&
                !source.Url.Contains("api.nuget.org", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<List<PrivatePackageInfo>> GetPackagesFromSource(
        Models.NuGetSourceConfiguration source,
        string searchTerm,
        int maxResults,
        bool includeVersions,
        CancellationToken cancellationToken)
    {
        if (source.IsAzureDevOps)
        {
            return await GetAzureDevOpsPackages(source, searchTerm, maxResults, includeVersions, cancellationToken);
        }

        return await GetStandardPrivatePackages(source, searchTerm, maxResults, includeVersions, cancellationToken);
    }

    private async Task<List<PrivatePackageInfo>> GetAzureDevOpsPackages(
        Models.NuGetSourceConfiguration source,
        string searchTerm,
        int maxResults,
        bool includeVersions,
        CancellationToken cancellationToken)
    {
        var httpClient = httpClientService.GetHttpClient(source.Name);
        var packageNames = await azureDevOpsPackageService.SearchPackagesAsync(httpClient, source, searchTerm);

        var packages = new List<PrivatePackageInfo>();

        foreach (var packageName in packageNames.Take(maxResults))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var packageInfo = new PrivatePackageInfo
            {
                PackageId = packageName,
                SourceName = source.Name,
                SourcePriority = source.Priority,
                SourceType = "Azure DevOps",
                IsAzureDevOps = true
            };

            if (includeVersions)
            {
                try
                {
                    var versions = await azureDevOpsPackageService.GetPackageVersionsAsync(httpClient, source, packageName);
                    packageInfo.Versions = versions.ToList();
                    packageInfo.LatestVersion = versions.LastOrDefault() ?? "unknown";
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to get versions for package '{PackageName}' from Azure DevOps source '{SourceName}'", 
                        packageName, source.Name);
                    packageInfo.LatestVersion = "unknown";
                }
            }

            packages.Add(packageInfo);
        }

        return packages;
    }

    private async Task<List<PrivatePackageInfo>> GetStandardPrivatePackages(
        Models.NuGetSourceConfiguration source,
        string searchTerm,
        int maxResults,
        bool includeVersions,
        CancellationToken cancellationToken)
    {
        // For standard private NuGet feeds, we'll attempt to search using the standard NuGet API
        // However, many private feeds don't support the search API, so this may return empty results
        var packages = new List<PrivatePackageInfo>();

        try
        {
            // Try to use the package service search (this might not work for all private feeds)
            var searchResults = await packageService.SearchPackagesAsync(searchTerm, maxResults);
            
            foreach (var packageInfo in searchResults.Take(maxResults))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var privatePackageInfo = new PrivatePackageInfo
                {
                    PackageId = packageInfo.PackageId,
                    SourceName = source.Name,
                    SourcePriority = source.Priority,
                    SourceType = "Private NuGet Feed",
                    IsAzureDevOps = false,
                    Description = packageInfo.Description,
                    Authors = packageInfo.Authors?.ToList() ?? []
                };

                if (includeVersions)
                {
                    try
                    {
                        var versions = await packageService.GetPackageVersions(packageInfo.PackageId);
                        privatePackageInfo.Versions = versions.ToList();
                        privatePackageInfo.LatestVersion = versions.LastOrDefault() ?? packageInfo.Version;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to get versions for package '{PackageName}' from source '{SourceName}'", 
                            packageInfo.PackageId, source.Name);
                        privatePackageInfo.LatestVersion = packageInfo.Version;
                    }
                }
                else
                {
                    privatePackageInfo.LatestVersion = packageInfo.Version;
                }

                packages.Add(privatePackageInfo);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Search not supported or failed for private source '{SourceName}'. This is normal for many private feeds.", source.Name);
            // For private feeds that don't support search, we can't list packages without knowing their names
            // This is a limitation of many private NuGet feeds
        }

        return packages;
    }

    private static string GetSourceType(Models.NuGetSourceConfiguration source)
    {
        if (source.IsAzureDevOps)
            return "Azure DevOps";
        
        if (!string.IsNullOrWhiteSpace(source.ApiKey))
            return "Private NuGet Feed (API Key)";
        
        if (!string.IsNullOrWhiteSpace(source.Username))
            return "Private NuGet Feed (Basic Auth)";
        
        return "Private NuGet Feed";
    }
}

public class PrivatePackageListResult
{
    public string SearchTerm { get; set; } = string.Empty;
    public int TotalPackages { get; set; }
    public List<PrivateSourceResult> Sources { get; set; } = [];
    public List<PrivatePackageInfo> Packages { get; set; } = [];
}

public class PrivateSourceResult
{
    public string SourceName { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public int PackageCount { get; set; }
    public bool IsAzureDevOps { get; set; }
    public string? Error { get; set; }
}

public class PrivatePackageInfo
{
    public string PackageId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public int SourcePriority { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public bool IsAzureDevOps { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public List<string> Versions { get; set; } = [];
    public string? Description { get; set; }
    public List<string> Authors { get; set; } = [];
}
