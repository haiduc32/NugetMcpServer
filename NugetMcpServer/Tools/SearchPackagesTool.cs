using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Server;

using NuGetMcpServer.Common;
using NuGetMcpServer.Services;

using static NuGetMcpServer.Extensions.ExceptionHandlingExtensions;

namespace NuGetMcpServer.Tools;

[McpServerToolType]
public class SearchPackagesTool : McpToolBase<SearchPackagesTool>
{
    public SearchPackagesTool(ILogger<SearchPackagesTool> logger, NuGetPackageService packageService)
        : base(logger, packageService)
    {
    }
    [McpServerTool]
    [Description(
        "Searches for NuGet packages by description or functionality. " +
        "Standard search uses only the original query. " +
        "Fuzzy search enhances results by combining standard search with AI-generated package name alternatives " +
        "based on the described functionality. Returns up to 50 most popular packages with details."
    )]
    public Task<PackageSearchResult> SearchPackages(
        IMcpServer thisServer,
        [Description("Description of the functionality you're looking for")] string query,
        [Description("Maximum number of results to return (default: 20)")] int maxResults = 20,
        [Description("Enable fuzzy search to include AI-generated package name alternatives (default: false)")] bool fuzzySearch = false,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithLoggingAsync(
            () => SearchPackagesCore(thisServer, query, maxResults, fuzzySearch, cancellationToken),
            Logger,
            "Error searching packages");
    }
    private async Task<PackageSearchResult> SearchPackagesCore(
        IMcpServer thisServer,
        string query,
        int maxResults,
        bool fuzzySearch,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        if (maxResults <= 0 || maxResults > 50)
            maxResults = 20; Logger.LogInformation("Starting package search for query: {Query}, fuzzy: {FuzzySearch}", query, fuzzySearch);

        // Phase 1: Standard search with original query
        var directResults = await PackageService.SearchPackagesAsync(query, maxResults);
        Logger.LogInformation("Standard search found {Count} packages", directResults.Count);

        if (!fuzzySearch)
        {
            return new PackageSearchResult
            {
                Query = query,
                TotalCount = directResults.Count,
                Packages = directResults.Take(maxResults).ToList(),
                UsedAiKeywords = false
            };
        }        // Phase 2: Fuzzy search - enhance with AI-generated package name alternatives
        var aiPackageNames = await GeneratePackageNamesAsync(thisServer, query, 10, cancellationToken);

        if (aiPackageNames == null || !aiPackageNames.Any())
        {
            Logger.LogWarning("AI package name generation failed or returned empty result");
            return new PackageSearchResult
            {
                Query = query,
                TotalCount = directResults.Count,
                Packages = directResults.Take(maxResults).ToList(),
                UsedAiKeywords = false
            };
        }

        Logger.LogInformation("Generated AI package names: {PackageNames}", string.Join(", ", aiPackageNames));

        var allAiResults = new List<Services.PackageInfo>();
        foreach (var packageName in aiPackageNames)
        {
            var searchResults = await PackageService.SearchPackagesAsync(packageName, maxResults);
            allAiResults.AddRange(searchResults);
        }
        Logger.LogInformation("AI-enhanced fuzzy search found {Count} additional packages", allAiResults.Count);

        // Combine results, removing duplicates by package ID and sorting by popularity
        var combinedResults = directResults
            .Concat(allAiResults)
            .GroupBy(p => p.Id.ToLowerInvariant())
            .Select(g => g.First())
            .OrderByDescending(p => p.DownloadCount)
            .Take(maxResults)
            .ToList(); return new PackageSearchResult
            {
                Query = query,
                TotalCount = combinedResults.Count,
                Packages = combinedResults,
                UsedAiKeywords = true,
                AiKeywords = string.Join(", ", aiPackageNames)
            };
    }

    private async Task<IReadOnlyCollection<string>> GeneratePackageNamesAsync(
    IMcpServer thisServer,
    string originalQuery,
    int packageCount,
    CancellationToken cancellationToken)
    {
        // Build a very small‑LLM‑friendly prompt
        var prompt =
            $@"Given the request: '{originalQuery}', list exactly {packageCount} likely NuGet package names that would satisfy it. \n" +
            "Use typical .NET naming patterns (suffixes such as Generator, Builder, Client, Service, Helper, Manager, Processor, Handler, Framework). \n" +
            "Return exactly {packageCount} lines, one package name per line, no extra text.";

        var messages = new[]
        {
            new ChatMessage(ChatRole.User, prompt)
        };

        var options = new ChatOptions
        {
            MaxOutputTokens = 40,
            Temperature = 0.2f
        };

        try
        {
            var response = await thisServer
                .AsSamplingChatClient()
                .GetResponseAsync(messages, options, cancellationToken);

            var names = response.ToString()
                .Split(["\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(packageCount)
                .ToList();

            return names.AsReadOnly();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to generate NuGet package names for query: {Query}", originalQuery);
            return [];
        }
    }

}
