using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGetMcpServer.Models;

namespace NuGetMcpServer.Services;

public class AzureDevOpsPackageService(ILogger<AzureDevOpsPackageService> logger)
{
    public async Task<IReadOnlyList<string>> SearchPackagesAsync(
        HttpClient httpClient,
        NuGetSourceConfiguration source,
        string searchTerm)
    {
        if (!source.IsAzureDevOps || string.IsNullOrEmpty(source.Organization) || string.IsNullOrEmpty(source.FeedId))
        {
            return Array.Empty<string>();
        }

        try
        {
            SetupAuthentication(httpClient, source);

            var packages = await GetPackagesAsync(httpClient, source);
            
            if (source.FilterNativePackagesOnly)
            {
                packages = FilterNativePackages(packages);
            }

            var matchingPackages = packages
                .Where(p => p.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Name)
                .Distinct()
                .ToList();

            logger.LogDebug("Found {Count} matching packages for search term '{SearchTerm}' in Azure DevOps feed '{FeedName}'", 
                matchingPackages.Count, searchTerm, source.Name);

            return matchingPackages;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error searching packages in Azure DevOps feed '{FeedName}'", source.Name);
            return Array.Empty<string>();
        }
    }

    public async Task<IReadOnlyList<string>> GetPackageVersionsAsync(
        HttpClient httpClient,
        NuGetSourceConfiguration source,
        string packageName)
    {
        if (!source.IsAzureDevOps || string.IsNullOrEmpty(source.Organization) || string.IsNullOrEmpty(source.FeedId))
        {
            return Array.Empty<string>();
        }

        try
        {
            SetupAuthentication(httpClient, source);

            var url = $"https://pkgs.dev.azure.com/{source.Organization}/_apis/packaging/feeds/{source.FeedId}/packages/nuget/{packageName}/versions?api-version=6.0-preview.1&includeUrls=true";
            
            var response = await httpClient.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<AzureDevOpsPackagesResponse>(response);

            if (result?.Value == null)
            {
                return Array.Empty<string>();
            }

            var versions = result.Value
                .SelectMany(p => p.Versions)
                .Where(v => !source.FilterNativePackagesOnly || IsNativeVersion(v))
                .Select(v => v.Version)
                .ToList();

            logger.LogDebug("Found {Count} versions for package '{PackageName}' in Azure DevOps feed '{FeedName}'", 
                versions.Count, packageName, source.Name);

            return versions;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting versions for package '{PackageName}' in Azure DevOps feed '{FeedName}'", 
                packageName, source.Name);
            return Array.Empty<string>();
        }
    }

    public async Task<Stream?> DownloadPackageAsync(
        HttpClient httpClient,
        NuGetSourceConfiguration source,
        string packageName,
        string version)
    {
        if (!source.IsAzureDevOps || string.IsNullOrEmpty(source.Organization) || string.IsNullOrEmpty(source.FeedId))
        {
            return null;
        }

        try
        {
            SetupAuthentication(httpClient, source);

            // First check if this version is native if filtering is enabled
            if (source.FilterNativePackagesOnly)
            {
                var versions = await GetPackageVersionsAsync(httpClient, source, packageName);
                if (!versions.Contains(version))
                {
                    logger.LogDebug("Package '{PackageName}' version '{Version}' is not a native package", packageName, version);
                    return null;
                }
            }

            var url = $"https://pkgs.dev.azure.com/{source.Organization}/_apis/packaging/feeds/{source.FeedId}/nuget/packages/{packageName}/versions/{version}/content?api-version=6.0-preview.1";
            
            var response = await httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStreamAsync();
            }

            logger.LogWarning("Failed to download package '{PackageName}' version '{Version}' from Azure DevOps feed '{FeedName}'. Status: {StatusCode}", 
                packageName, version, source.Name, response.StatusCode);
            
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error downloading package '{PackageName}' version '{Version}' from Azure DevOps feed '{FeedName}'", 
                packageName, version, source.Name);
            return null;
        }
    }

    private async Task<List<AzureDevOpsPackage>> GetPackagesAsync(HttpClient httpClient, NuGetSourceConfiguration source)
    {
        var url = $"https://feeds.dev.azure.com/{source.Organization}//_apis/packaging/Feeds/{source.FeedId}/packages?api-version=6.0-preview.1&directUpstreamId=00000000-0000-0000-0000-000000000000"; //$"https://pkgs.dev.azure.com/{source.Organization}/_apis/packaging/feeds/{source.FeedId}/packages?api-version=6.0-preview.1&includeUrls=true&protocolType=nuget";
        
        var response = await httpClient.GetStringAsync(url);
        var result = JsonSerializer.Deserialize<AzureDevOpsPackagesResponse>(response);
        
        return result?.Value ?? new List<AzureDevOpsPackage>();
    }

    private static List<AzureDevOpsPackage> FilterNativePackages(List<AzureDevOpsPackage> packages)
    {
        return packages
            .Where(p => IsNativePackage(p))
            .ToList();
    }

    private static bool IsNativePackage(AzureDevOpsPackage package)
    {
        // A package is native if it has no source chain or an empty source chain
        return package.SourceChain == null || package.SourceChain.Count == 0;
    }

    private static bool IsNativeVersion(AzureDevOpsPackageVersion version)
    {
        // A version is native if it has no source chain or an empty source chain
        return version.SourceChain == null || version.SourceChain.Count == 0;
    }

    private static void SetupAuthentication(HttpClient httpClient, NuGetSourceConfiguration source)
    {
        // Clear any existing authorization headers
        httpClient.DefaultRequestHeaders.Authorization = null;

        if (!string.IsNullOrWhiteSpace(source.ApiKey))
        {
            // For Azure DevOps, API key goes in Authorization header as Basic auth with empty username
            var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{source.ApiKey}"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        }
        else if (!string.IsNullOrWhiteSpace(source.Username) && !string.IsNullOrWhiteSpace(source.Password))
        {
            var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{source.Username}:{source.Password}"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
        }
    }
}
