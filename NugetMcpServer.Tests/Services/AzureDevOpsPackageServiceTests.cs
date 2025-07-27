using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NuGetMcpServer.Models;
using NuGetMcpServer.Services;
using Xunit;

namespace NuGetMcpServer.Tests.Services;

public class AzureDevOpsPackageServiceTests
{
    private readonly AzureDevOpsPackageService _service;

    public AzureDevOpsPackageServiceTests()
    {
        _service = new AzureDevOpsPackageService(NullLogger<AzureDevOpsPackageService>.Instance);
    }

    [Fact]
    public async Task SearchPackagesAsync_WithNonAzureDevOpsSource_ReturnsEmpty()
    {
        // Arrange
        var source = new NuGetSourceConfiguration
        {
            Name = "regular-source",
            Url = "https://api.nuget.org/v3/index.json",
            IsAzureDevOps = false
        };

        using var httpClient = new HttpClient();

        // Act
        var result = await _service.SearchPackagesAsync(httpClient, source, "test");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchPackagesAsync_WithMissingOrganization_ReturnsEmpty()
    {
        // Arrange
        var source = new NuGetSourceConfiguration
        {
            Name = "azure-source",
            Url = "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json",
            IsAzureDevOps = true,
            FeedId = "feed"
            // Organization is missing
        };

        using var httpClient = new HttpClient();

        // Act
        var result = await _service.SearchPackagesAsync(httpClient, source, "test");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchPackagesAsync_WithMissingFeedId_ReturnsEmpty()
    {
        // Arrange
        var source = new NuGetSourceConfiguration
        {
            Name = "azure-source",
            Url = "https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json",
            IsAzureDevOps = true,
            Organization = "org"
            // FeedId is missing
        };

        using var httpClient = new HttpClient();

        // Act
        var result = await _service.SearchPackagesAsync(httpClient, source, "test");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPackageVersionsAsync_WithNonAzureDevOpsSource_ReturnsEmpty()
    {
        // Arrange
        var source = new NuGetSourceConfiguration
        {
            Name = "regular-source",
            Url = "https://api.nuget.org/v3/index.json",
            IsAzureDevOps = false
        };

        using var httpClient = new HttpClient();

        // Act
        var result = await _service.GetPackageVersionsAsync(httpClient, source, "TestPackage");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task DownloadPackageAsync_WithNonAzureDevOpsSource_ReturnsNull()
    {
        // Arrange
        var source = new NuGetSourceConfiguration
        {
            Name = "regular-source",
            Url = "https://api.nuget.org/v3/index.json",
            IsAzureDevOps = false
        };

        using var httpClient = new HttpClient();

        // Act
        var result = await _service.DownloadPackageAsync(httpClient, source, "TestPackage", "1.0.0");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ValidateFilterNativePackagesOnlyDefaultValue()
    {
        // Arrange & Act
        var source = new NuGetSourceConfiguration
        {
            IsAzureDevOps = true
        };

        // Assert
        Assert.True(source.FilterNativePackagesOnly); // Should default to true
    }
}
