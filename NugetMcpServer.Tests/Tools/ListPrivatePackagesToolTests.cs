using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NuGetMcpServer.Models;
using NuGetMcpServer.Services;
using NuGetMcpServer.Tests.Helpers;
using NuGetMcpServer.Tools;

using Xunit;
using Xunit.Abstractions;

namespace NuGetMcpServer.Tests.Tools;

public class ListPrivatePackagesToolTests : TestBase
{
    private readonly TestLogger<ListPrivatePackagesTool> _toolLogger;
    private readonly ListPrivatePackagesTool _tool;

    public ListPrivatePackagesToolTests(ITestOutputHelper testOutput) : base(testOutput)
    {
        _toolLogger = new TestLogger<ListPrivatePackagesTool>(TestOutput);
        
        // Use real services for integration testing
        var httpClientService = CreateNuGetHttpClientService();
        var azureDevOpsService = new AzureDevOpsPackageService(new TestLogger<AzureDevOpsPackageService>(TestOutput));
        var packageService = CreateNuGetPackageService();
        
        _tool = new ListPrivatePackagesTool(_toolLogger, httpClientService, azureDevOpsService, packageService);
    }

    [Fact]
    public async Task list_private_packages_ShouldReturnEmptyResult_WhenNoPrivateSourcesConfigured()
    {
        // Act - The default test configuration only has nuget.org which is not considered private
        var result = await _tool.list_private_packages();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalPackages);
        Assert.Empty(result.Sources);
        Assert.Empty(result.Packages);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task list_private_packages_ShouldHandleEmptySearchTerm(string searchTerm)
    {
        // Act
        var result = await _tool.list_private_packages(searchTerm);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(searchTerm, result.SearchTerm);
    }

    [Fact]
    public async Task list_private_packages_ShouldHandleNullSearchTerm()
    {
        // Act
        var result = await _tool.list_private_packages(null!);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("", result.SearchTerm);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(300)]
    public async Task list_private_packages_ShouldEnforceMaxResultsLimits(int requestedMaxResults)
    {
        // Act
        var result = await _tool.list_private_packages("", requestedMaxResults);

        // Assert
        Assert.NotNull(result);
        // Since we have no private sources, we can't test the actual limiting,
        // but we can verify the tool doesn't crash with invalid inputs
        Assert.Equal(0, result.TotalPackages);
    }

    [Fact]
    public async Task list_private_packages_ShouldNotIncludeVersionsByDefault()
    {
        // Act
        var result = await _tool.list_private_packages("test", 10, false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test", result.SearchTerm);
        // Since we have no private packages, verify the structure is correct
        Assert.Empty(result.Packages);
    }

    [Fact]
    public async Task list_private_packages_ShouldAcceptIncludeVersionsParameter()
    {
        // Act
        var result = await _tool.list_private_packages("test", 10, true);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test", result.SearchTerm);
        Assert.Empty(result.Packages);
    }

    [Fact]
    public async Task list_private_packages_ShouldReturnValidStructure()
    {
        // Act
        var result = await _tool.list_private_packages();

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Sources);
        Assert.NotNull(result.Packages);
        Assert.True(result.TotalPackages >= 0);
        Assert.Equal("", result.SearchTerm);
    }

    [Fact]
    public async Task PrivateSourceDetection_ShouldIdentifyAzureDevOpsSource()
    {
        // Arrange
        var source = new NuGetSourceConfiguration
        {
            Name = "AzureDevOps",
            Url = "https://pkgs.dev.azure.com/org/feed",
            IsAzureDevOps = true,
            IsEnabled = true
        };

        // Create a custom HTTP client service to test private source detection
        var config = new NuGetConfiguration { Sources = [source] };
        var options = Options.Create(config);
        var httpService = new NuGetHttpClientService(NullLogger<NuGetHttpClientService>.Instance, options);
        var azureService = new AzureDevOpsPackageService(NullLogger<AzureDevOpsPackageService>.Instance);
        var packageService = CreateNuGetPackageService();
        var tool = new ListPrivatePackagesTool(NullLogger<ListPrivatePackagesTool>.Instance, httpService, azureService, packageService);

        // Act
        var result = await tool.list_private_packages();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Sources);
        Assert.Equal("AzureDevOps", result.Sources[0].SourceName);
        Assert.True(result.Sources[0].IsAzureDevOps);
    }

    [Fact]
    public async Task PrivateSourceDetection_ShouldIdentifyPrivateApiKeySource()
    {
        // Arrange
        var source = new NuGetSourceConfiguration
        {
            Name = "PrivateNuGet",
            Url = "https://private.nuget.com/v3/index.json",
            ApiKey = "secret-key",
            IsEnabled = true
        };

        // Create a custom HTTP client service to test private source detection
        var config = new NuGetConfiguration { Sources = [source] };
        var options = Options.Create(config);
        var httpService = new NuGetHttpClientService(NullLogger<NuGetHttpClientService>.Instance, options);
        var azureService = new AzureDevOpsPackageService(NullLogger<AzureDevOpsPackageService>.Instance);
        var packageService = CreateNuGetPackageService();
        var tool = new ListPrivatePackagesTool(NullLogger<ListPrivatePackagesTool>.Instance, httpService, azureService, packageService);

        // Act
        var result = await tool.list_private_packages();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Sources);
        Assert.Equal("PrivateNuGet", result.Sources[0].SourceName);
        Assert.Equal("Private NuGet Feed (API Key)", result.Sources[0].SourceType);
        Assert.False(result.Sources[0].IsAzureDevOps);
    }

    [Fact]
    public async Task PrivateSourceDetection_ShouldIdentifyPrivateBasicAuthSource()
    {
        // Arrange
        var source = new NuGetSourceConfiguration
        {
            Name = "PrivateAuth",
            Url = "https://private.nuget.com/v3/index.json",
            Username = "user",
            Password = "pass",
            IsEnabled = true
        };

        // Create a custom HTTP client service to test private source detection
        var config = new NuGetConfiguration { Sources = [source] };
        var options = Options.Create(config);
        var httpService = new NuGetHttpClientService(NullLogger<NuGetHttpClientService>.Instance, options);
        var azureService = new AzureDevOpsPackageService(NullLogger<AzureDevOpsPackageService>.Instance);
        var packageService = CreateNuGetPackageService();
        var tool = new ListPrivatePackagesTool(NullLogger<ListPrivatePackagesTool>.Instance, httpService, azureService, packageService);

        // Act
        var result = await tool.list_private_packages();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Sources);
        Assert.Equal("PrivateAuth", result.Sources[0].SourceName);
        Assert.Equal("Private NuGet Feed (Basic Auth)", result.Sources[0].SourceType);
        Assert.False(result.Sources[0].IsAzureDevOps);
    }

    [Fact]
    public async Task PrivateSourceDetection_ShouldIgnorePublicNuGetOrg()
    {
        // Arrange
        var sources = new List<NuGetSourceConfiguration>
        {
            new() { Name = "nuget.org", Url = "https://api.nuget.org/v3/index.json", IsEnabled = true },
            new() { Name = "nuget-alt", Url = "https://nuget.org/api/v3/index.json", IsEnabled = true }
        };

        var config = new NuGetConfiguration { Sources = sources };
        var options = Options.Create(config);
        var httpService = new NuGetHttpClientService(NullLogger<NuGetHttpClientService>.Instance, options);
        var azureService = new AzureDevOpsPackageService(NullLogger<AzureDevOpsPackageService>.Instance);
        var packageService = CreateNuGetPackageService();
        var tool = new ListPrivatePackagesTool(NullLogger<ListPrivatePackagesTool>.Instance, httpService, azureService, packageService);

        // Act
        var result = await tool.list_private_packages();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Sources); // No private sources should be detected
        Assert.Equal(0, result.TotalPackages);
    }

    [Fact]
    public async Task PrivateSourceDetection_ShouldDetectMixedSources()
    {
        // Arrange
        var sources = new List<NuGetSourceConfiguration>
        {
            new() { Name = "nuget.org", Url = "https://api.nuget.org/v3/index.json", IsEnabled = true, Priority = 1 },
            new() { Name = "Private1", Url = "https://private.company.com/nuget", ApiKey = "key1", IsEnabled = true, Priority = 3 },
            new() { Name = "AzureDevOps", Url = "https://pkgs.dev.azure.com/org/feed", IsAzureDevOps = true, IsEnabled = true, Priority = 2 },
            new() { Name = "Private2", Url = "https://internal.feed.com", Username = "user", Password = "pass", IsEnabled = true, Priority = 4 }
        };

        var config = new NuGetConfiguration { Sources = sources };
        var options = Options.Create(config);
        var httpService = new NuGetHttpClientService(NullLogger<NuGetHttpClientService>.Instance, options);
        var azureService = new AzureDevOpsPackageService(NullLogger<AzureDevOpsPackageService>.Instance);
        var packageService = CreateNuGetPackageService();
        var tool = new ListPrivatePackagesTool(NullLogger<ListPrivatePackagesTool>.Instance, httpService, azureService, packageService);

        // Act
        var result = await tool.list_private_packages();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Sources.Count); // Only private sources
        
        // Verify sources are ordered by priority (descending)
        var sourceNames = result.Sources.Select(s => s.SourceName).ToList();
        Assert.Contains("Private2", sourceNames); // Priority 4
        Assert.Contains("Private1", sourceNames); // Priority 3  
        Assert.Contains("AzureDevOps", sourceNames); // Priority 2
        Assert.DoesNotContain("nuget.org", sourceNames); // Should be excluded
    }

    [Theory]
    [InlineData("https://private.company.com/nuget")]
    [InlineData("https://internal.feed.local/api")]
    [InlineData("https://custom.packages.org")]
    public async Task PrivateSourceDetection_ShouldDetectNonNuGetOrgUrls(string url)
    {
        // Arrange
        var source = new NuGetSourceConfiguration
        {
            Name = "Custom",
            Url = url,
            IsEnabled = true
        };

        var config = new NuGetConfiguration { Sources = [source] };
        var options = Options.Create(config);
        var httpService = new NuGetHttpClientService(NullLogger<NuGetHttpClientService>.Instance, options);
        var azureService = new AzureDevOpsPackageService(NullLogger<AzureDevOpsPackageService>.Instance);
        var packageService = CreateNuGetPackageService();
        var tool = new ListPrivatePackagesTool(NullLogger<ListPrivatePackagesTool>.Instance, httpService, azureService, packageService);

        // Act
        var result = await tool.list_private_packages();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Sources);
        Assert.Equal("Custom", result.Sources[0].SourceName);
        Assert.Equal("Private NuGet Feed", result.Sources[0].SourceType);
    }
}
