using NugetMcpServer.Tests.Helpers;

using NuGetMcpServer.Services;
using NuGetMcpServer.Tools;

using Xunit.Abstractions;

namespace NugetMcpServer.Tests.Tools;

public class SearchPackagesToolTests : TestBase
{
    private readonly TestLogger<NuGetPackageService> _packageLogger;
    private readonly TestLogger<SearchPackagesTool> _toolLogger;
    private readonly NuGetPackageService _packageService;
    private readonly SearchPackagesTool _tool;

    public SearchPackagesToolTests(ITestOutputHelper testOutput) : base(testOutput)
    {
        _packageLogger = new TestLogger<NuGetPackageService>(TestOutput);
        _toolLogger = new TestLogger<SearchPackagesTool>(TestOutput);
        _packageService = new NuGetPackageService(_packageLogger, HttpClient);
        _tool = new SearchPackagesTool(_toolLogger, _packageService);
    }
    [Fact]
    public async Task SearchPackages_WithPopularQuery_ReturnsResults()
    {
        // Note: This test might require AI functionality, skipping the AI part for now
        // We can test just the package service functionality
        var query = "json";

        // Test the underlying package service directly
        var results = await _packageService.SearchPackagesAsync(query, 5);

        // Assert
        Assert.NotNull(results);
        Assert.NotEmpty(results);
        Assert.True(results.Count <= 5);

        foreach (var package in results)
        {
            Assert.NotEmpty(package.Id);
            Assert.NotEmpty(package.Version);
            Assert.True(package.DownloadCount >= 0);
        }

        TestOutput.WriteLine($"Found {results.Count} packages for query '{query}':");
        foreach (var package in results.Take(3))
        {
            TestOutput.WriteLine($"- {package.Id} v{package.Version} ({package.DownloadCount:N0} downloads)");
            if (!string.IsNullOrEmpty(package.Description))
            {
                TestOutput.WriteLine($"  Description: {package.Description}");
            }
        }
    }

    [Fact]
    public void PackageSearchResult_FormatsCorrectly()
    {
        // Arrange
        var result = new PackageSearchResult
        {
            Query = "json library",
            TotalCount = 2,
            UsedAiKeywords = false,
            Packages = new List<PackageInfo>
            {
                new()
                {
                    Id = "Newtonsoft.Json",
                    Version = "13.0.3",
                    DownloadCount = 1000000,
                    Description = "Popular JSON framework for .NET",
                    ProjectUrl = "https://www.newtonsoft.com/json",
                    Tags = new List<string> { "json", "serialization" }
                }
            }
        };

        // Act
        var formatted = result.ToFormattedString();

        // Assert
        Assert.Contains("json library", formatted);
        Assert.Contains("Newtonsoft.Json", formatted);
        Assert.Contains("13.0.3", formatted);
        Assert.Contains("1,000,000", formatted);
        Assert.Contains("Popular JSON framework", formatted);
        Assert.Contains("https://www.newtonsoft.com/json", formatted);
        Assert.Contains("json, serialization", formatted);

        TestOutput.WriteLine("Formatted result:");
        TestOutput.WriteLine(formatted);
    }

    [Fact]
    public void PackageSearchResult_WithAiKeywords_ShowsAiInfo()
    {
        // Arrange
        var result = new PackageSearchResult
        {
            Query = "need math library",
            TotalCount = 1,
            UsedAiKeywords = true,
            AiKeywords = "math mathematics numerics calculation",
            Packages = new List<PackageInfo>
            {
                new()
                {
                    Id = "MathNet.Numerics",
                    Version = "5.0.0",
                    DownloadCount = 500000,
                    Description = "Math.NET Numerics"
                }
            }
        };

        // Act
        var formatted = result.ToFormattedString();        // Assert
        Assert.Contains("need math library", formatted);
        Assert.Contains("AI-GENERATED PACKAGE NAMES", formatted);
        Assert.Contains("math mathematics numerics calculation", formatted);
        Assert.Contains("MathNet.Numerics", formatted);

        TestOutput.WriteLine("Formatted result with AI keywords:");
        TestOutput.WriteLine(formatted);
    }

    [Fact]
    public void PackageSearchResult_WithFuzzySearch_ShowsCombinedResults()
    {
        // Arrange
        var result = new PackageSearchResult
        {
            Query = "need library for maze generation",
            TotalCount = 2,
            UsedAiKeywords = true,
            AiKeywords = "MazeGenerator MazeBuilder MazeCreator",
            Packages = new List<PackageInfo>
            {
                new()
                {
                    Id = "MazeLib",
                    Version = "1.0.0",
                    DownloadCount = 10000,
                    Description = "Library for maze operations"
                },
                new()
                {
                    Id = "MazeGenerator",
                    Version = "2.1.0",
                    DownloadCount = 50000,
                    Description = "Advanced maze generation toolkit"
                }
            }
        };

        // Act
        var formatted = result.ToFormattedString();        // Assert
        Assert.Contains("need library for maze generation", formatted);
        Assert.Contains("AI-GENERATED PACKAGE NAMES", formatted);
        Assert.Contains("MazeGenerator MazeBuilder MazeCreator", formatted);
        Assert.Contains("MazeLib", formatted);
        Assert.Contains("MazeGenerator", formatted);

        TestOutput.WriteLine("Formatted fuzzy search result:");
        TestOutput.WriteLine(formatted);
    }
}
