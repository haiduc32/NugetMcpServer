using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using NuGetMcpServer.Common;
using NuGetMcpServer.Extensions;
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
        "Uses a two-phase approach: direct search first, then AI-enhanced keyword search if needed. " +
        "Returns up to 20 most popular packages with details including download counts."
    )]
    public Task<PackageSearchResult> SearchPackages(
        IMcpServer thisServer,
        [Description("Description of the functionality you're looking for")] string query,
        [Description("Maximum number of results to return (default: 20)")] int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithLoggingAsync(
            () => SearchPackagesCore(thisServer, query, maxResults, cancellationToken),
            Logger,
            "Error searching packages");
    }

    private async Task<PackageSearchResult> SearchPackagesCore(
        IMcpServer thisServer, 
        string query, 
        int maxResults, 
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be empty", nameof(query));

        if (maxResults <= 0 || maxResults > 50)
            maxResults = 20;

        Logger.LogInformation("Starting package search for query: {Query}", query);

        // Phase 1: Direct search
        var directResults = await PackageService.SearchPackagesAsync(query, maxResults);
        
        if (directResults.Count > 0)
        {
            Logger.LogInformation("Direct search found {Count} packages", directResults.Count);
            return new PackageSearchResult
            {
                Query = query,
                TotalCount = directResults.Count,
                Packages = directResults.Take(maxResults).ToList(),
                UsedAiKeywords = false
            };
        }

        Logger.LogInformation("Direct search found no results, trying AI-enhanced search");

        // Phase 2: AI-enhanced keyword search
        var aiKeywords = await GenerateSearchKeywordsAsync(thisServer, query, cancellationToken);
        
        if (string.IsNullOrWhiteSpace(aiKeywords))
        {
            Logger.LogWarning("AI keyword generation failed or returned empty result");
            return new PackageSearchResult
            {
                Query = query,
                TotalCount = 0,
                Packages = [],
                UsedAiKeywords = false
            };
        }

        Logger.LogInformation("Generated AI keywords: {Keywords}", aiKeywords);

        var aiResults = await PackageService.SearchPackagesAsync(aiKeywords, maxResults);
        
        return new PackageSearchResult
        {
            Query = query,
            TotalCount = aiResults.Count,
            Packages = aiResults.Take(maxResults).ToList(),
            UsedAiKeywords = true,
            AiKeywords = aiKeywords
        };
    }

    private async Task<string> GenerateSearchKeywordsAsync(
        IMcpServer thisServer, 
        string originalQuery, 
        CancellationToken cancellationToken)
    {
        var messages = new[]
        {
            new ChatMessage(
                role: ChatRole.User,
                content: $"Generate NuGet package search keywords for this request: \"{originalQuery}\". " +
                        "Return only the most relevant technical terms and library names that could match NuGet packages. " +
                        "Separate keywords with spaces. Focus on: framework names, technology terms, common library patterns."
            )
        };

        var options = new ChatOptions
        {
            MaxOutputTokens = 100,
            Temperature = 0.3f
        };

        try
        {
            var response = await thisServer
                .AsSamplingChatClient()
                .GetResponseAsync(messages, options, cancellationToken);

            return response.ToString().Trim();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to generate AI keywords for query: {Query}", originalQuery);
            return string.Empty;
        }
    }
}
