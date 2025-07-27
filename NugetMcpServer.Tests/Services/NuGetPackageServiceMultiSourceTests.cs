using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using NuGetMcpServer.Models;
using NuGetMcpServer.Services;
using Xunit;

namespace NuGetMcpServer.Tests.Services;

public class NuGetPackageServiceMultiSourceTests
{
    private readonly Mock<NuGetHttpClientService> _mockHttpClientService;
    private readonly Mock<MetaPackageDetector> _mockMetaPackageDetector;
    private readonly NuGetPackageService _packageService;

    public NuGetPackageServiceMultiSourceTests()
    {
        _mockHttpClientService = new Mock<NuGetHttpClientService>(
            NullLogger<NuGetHttpClientService>.Instance,
            Options.Create(new NuGetConfiguration()));
        
        _mockMetaPackageDetector = new Mock<MetaPackageDetector>(
            NullLogger<MetaPackageDetector>.Instance);
        
        _packageService = new NuGetPackageService(
            NullLogger<NuGetPackageService>.Instance,
            _mockHttpClientService.Object,
            _mockMetaPackageDetector.Object,
            new AzureDevOpsPackageService(NullLogger<AzureDevOpsPackageService>.Instance));
    }

    [Fact]
    public async Task GetPackageVersions_WithValidPackage_ReturnsVersionsFromFirstSource()
    {
        // Arrange
        var sources = new List<NuGetSourceConfiguration>
        {
            new() { Name = "source1", Url = "https://source1.com/", IsEnabled = true, Priority = 100 },
            new() { Name = "source2", Url = "https://source2.com/", IsEnabled = true, Priority = 50 }
        };

        var mockHttpClient = CreateMockHttpClient("""
            {
                "versions": ["1.0.0", "1.1.0", "2.0.0"]
            }
            """);

        _mockHttpClientService.Setup(x => x.GetEnabledSources()).Returns(sources);
        _mockHttpClientService.Setup(x => x.GetHttpClient("source1")).Returns(mockHttpClient);

        // Act
        var versions = await _packageService.GetPackageVersions("TestPackage");

        // Assert
        Assert.Equal(3, versions.Count);
        Assert.Contains("1.0.0", versions);
        Assert.Contains("1.1.0", versions);
        Assert.Contains("2.0.0", versions);
        
        // Verify only first source was called
        _mockHttpClientService.Verify(x => x.GetHttpClient("source1"), Times.Once);
        _mockHttpClientService.Verify(x => x.GetHttpClient("source2"), Times.Never);
    }

    [Fact]
    public async Task GetPackageVersions_FirstSourceFails_TriesSecondSource()
    {
        // Arrange
        var sources = new List<NuGetSourceConfiguration>
        {
            new() { Name = "source1", Url = "https://source1.com/", IsEnabled = true, Priority = 100 },
            new() { Name = "source2", Url = "https://source2.com/", IsEnabled = true, Priority = 50 }
        };

        var failingHttpClient = CreateMockHttpClient("", HttpStatusCode.NotFound);
        var workingHttpClient = CreateMockHttpClient("""
            {
                "versions": ["1.0.0", "1.1.0"]
            }
            """);

        _mockHttpClientService.Setup(x => x.GetEnabledSources()).Returns(sources);
        _mockHttpClientService.Setup(x => x.GetHttpClient("source1")).Returns(failingHttpClient);
        _mockHttpClientService.Setup(x => x.GetHttpClient("source2")).Returns(workingHttpClient);

        // Act
        var versions = await _packageService.GetPackageVersions("TestPackage");

        // Assert
        Assert.Equal(2, versions.Count);
        Assert.Contains("1.0.0", versions);
        Assert.Contains("1.1.0", versions);
        
        // Verify both sources were called
        _mockHttpClientService.Verify(x => x.GetHttpClient("source1"), Times.Once);
        _mockHttpClientService.Verify(x => x.GetHttpClient("source2"), Times.Once);
    }

    [Fact]
    public async Task GetPackageVersions_AllSourcesFailWithHttpException_ThrowsHttpRequestException()
    {
        // Arrange
        var sources = new List<NuGetSourceConfiguration>
        {
            new() { Name = "source1", Url = "https://source1.com/", IsEnabled = true, Priority = 100 },
            new() { Name = "source2", Url = "https://source2.com/", IsEnabled = true, Priority = 50 }
        };

        var failingHttpClient1 = CreateMockHttpClient("", HttpStatusCode.NotFound);
        var failingHttpClient2 = CreateMockHttpClient("", HttpStatusCode.NotFound);

        _mockHttpClientService.Setup(x => x.GetEnabledSources()).Returns(sources);
        _mockHttpClientService.Setup(x => x.GetHttpClient("source1")).Returns(failingHttpClient1);
        _mockHttpClientService.Setup(x => x.GetHttpClient("source2")).Returns(failingHttpClient2);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => _packageService.GetPackageVersions("NonExistentPackage"));
    }

    [Fact]
    public async Task GetPackageVersions_AllSourcesFailWithMixedExceptions_ThrowsInvalidOperationException()
    {
        // Arrange
        var sources = new List<NuGetSourceConfiguration>
        {
            new() { Name = "source1", Url = "https://source1.com/", IsEnabled = true, Priority = 100 },
            new() { Name = "source2", Url = "https://source2.com/", IsEnabled = true, Priority = 50 }
        };

        var httpFailingClient = CreateMockHttpClient("", HttpStatusCode.NotFound);
        var timeoutFailingClient = CreateMockHttpClientWithTimeout();

        _mockHttpClientService.Setup(x => x.GetEnabledSources()).Returns(sources);
        _mockHttpClientService.Setup(x => x.GetHttpClient("source1")).Returns(httpFailingClient);
        _mockHttpClientService.Setup(x => x.GetHttpClient("source2")).Returns(timeoutFailingClient);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _packageService.GetPackageVersions("TestPackage"));
        
        Assert.Contains("Failed to get package versions for TestPackage from all configured sources", exception.Message);
    }

    [Fact]
    public async Task DownloadPackageAsync_WithValidPackage_ReturnsStreamFromFirstSource()
    {
        // Arrange
        var sources = new List<NuGetSourceConfiguration>
        {
            new() { Name = "source1", Url = "https://source1.com/", IsEnabled = true, Priority = 100 }
        };

        var packageBytes = new byte[] { 1, 2, 3, 4, 5 };
        var mockHttpClient = CreateMockHttpClientForBytes(packageBytes);

        _mockHttpClientService.Setup(x => x.GetEnabledSources()).Returns(sources);
        _mockHttpClientService.Setup(x => x.GetHttpClient("source1")).Returns(mockHttpClient);

        // Act
        using var result = await _packageService.DownloadPackageAsync("TestPackage", "1.0.0");

        // Assert
        Assert.NotNull(result);
        var resultBytes = result.ToArray();
        Assert.Equal(packageBytes, resultBytes);
    }

    [Fact]
    public async Task DownloadPackageAsync_FirstSourceFails_TriesSecondSource()
    {
        // Arrange
        var sources = new List<NuGetSourceConfiguration>
        {
            new() { Name = "source1", Url = "https://source1.com/", IsEnabled = true, Priority = 100 },
            new() { Name = "source2", Url = "https://source2.com/", IsEnabled = true, Priority = 50 }
        };

        var failingHttpClient = CreateMockHttpClient("", HttpStatusCode.NotFound);
        var packageBytes = new byte[] { 1, 2, 3, 4, 5 };
        var workingHttpClient = CreateMockHttpClientForBytes(packageBytes);

        _mockHttpClientService.Setup(x => x.GetEnabledSources()).Returns(sources);
        _mockHttpClientService.Setup(x => x.GetHttpClient("source1")).Returns(failingHttpClient);
        _mockHttpClientService.Setup(x => x.GetHttpClient("source2")).Returns(workingHttpClient);

        // Act
        using var result = await _packageService.DownloadPackageAsync("TestPackage", "1.0.0");

        // Assert
        Assert.NotNull(result);
        var resultBytes = result.ToArray();
        Assert.Equal(packageBytes, resultBytes);
        
        // Verify both sources were called
        _mockHttpClientService.Verify(x => x.GetHttpClient("source1"), Times.Once);
        _mockHttpClientService.Verify(x => x.GetHttpClient("source2"), Times.Once);
    }

    private static HttpClient CreateMockHttpClient(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseContent)
            });

        if (statusCode != HttpStatusCode.OK)
        {
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .ThrowsAsync(new HttpRequestException($"Response status code does not indicate success: {(int)statusCode} ({statusCode})."));
        }

        return new HttpClient(mockHandler.Object);
    }

    private static HttpClient CreateMockHttpClientForBytes(byte[] responseBytes)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new ByteArrayContent(responseBytes)
            });

        return new HttpClient(mockHandler.Object);
    }

    private static HttpClient CreateMockHttpClientWithTimeout()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The operation was canceled."));

        return new HttpClient(mockHandler.Object);
    }
}
