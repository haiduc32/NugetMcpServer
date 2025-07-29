using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using NuGetMcpServer.Models;
using NuGetMcpServer.Services;
using NuGetMcpServer.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace NuGetMcpServer.Tests.Integration;

public class AuthenticationIntegrationTests : TestBase
{
    public AuthenticationIntegrationTests(ITestOutputHelper testOutput) : base(testOutput)
    {
    }
    [Fact]
    public void NuGetHttpClientService_WithBasicAuthentication_SetsCorrectHeaders()
    {
        // Arrange
        var configuration = new NuGetConfiguration
        {
            Sources = new List<NuGetSourceConfiguration>
            {
                new()
                {
                    Name = "private-source",
                    Url = "https://private.nuget.com/",
                    IsEnabled = true,
                    Priority = 100,
                    Username = "testuser",
                    Password = "testpass"
                }
            }
        };

        var options = Options.Create(configuration);
        var httpClientService = new NuGetHttpClientService(
            NullLogger<NuGetHttpClientService>.Instance,
            options);

        // Act
        var httpClient = httpClientService.GetHttpClient("private-source");

        // Assert
        Assert.NotNull(httpClient);
        Assert.True(httpClient.DefaultRequestHeaders.Authorization != null);
        Assert.Equal("Basic", httpClient.DefaultRequestHeaders.Authorization.Scheme);
        
        // Verify the authorization header value (base64 encoded username:password)
        var expectedAuth = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("testuser:testpass"));
        Assert.Equal(expectedAuth, httpClient.DefaultRequestHeaders.Authorization.Parameter);
    }

    [Fact]
    public void NuGetHttpClientService_WithApiKeyAuthentication_SetsCorrectHeaders()
    {
        // Arrange
        var configuration = new NuGetConfiguration
        {
            Sources = new List<NuGetSourceConfiguration>
            {
                new()
                {
                    Name = "api-source",
                    Url = "https://api.nuget.com/",
                    IsEnabled = true,
                    Priority = 100,
                    ApiKey = "secret-api-key-12345"
                }
            }
        };

        var options = Options.Create(configuration);
        var httpClientService = new NuGetHttpClientService(
            NullLogger<NuGetHttpClientService>.Instance,
            options);

        // Act
        var httpClient = httpClientService.GetHttpClient("api-source");

        // Assert
        Assert.NotNull(httpClient);
        Assert.True(httpClient.DefaultRequestHeaders.Contains("X-NuGet-ApiKey"));
        
        var apiKeyValues = httpClient.DefaultRequestHeaders.GetValues("X-NuGet-ApiKey");
        Assert.Contains("secret-api-key-12345", apiKeyValues);
    }

    [Fact]
    public async Task NuGetPackageService_WithAuthentication_HandlesUnauthorizedResponse()
    {
        // Arrange
        var mockHttpClientService = new Mock<NuGetHttpClientService>(
            NullLogger<NuGetHttpClientService>.Instance,
            Options.Create(new NuGetConfiguration()));
        
        var mockMetaPackageDetector = new Mock<MetaPackageDetector>(
            NullLogger<MetaPackageDetector>.Instance);

        var sources = new List<NuGetSourceConfiguration>
        {
            new() { Name = "auth-source", Url = "https://auth.nuget.com/", IsEnabled = true, Priority = 100 }
        };

        var unauthorizedHttpClient = CreateMockHttpClientWithUnauthorized();

        mockHttpClientService.Setup(x => x.GetEnabledSources()).Returns(sources);
        mockHttpClientService.Setup(x => x.GetHttpClient("auth-source")).Returns(unauthorizedHttpClient);

        var repositoryService = CreateNuGetRepositoryService();
        var packageService = new NuGetPackageService(
            NullLogger<NuGetPackageService>.Instance,
            repositoryService,
            mockMetaPackageDetector.Object,
            new AzureDevOpsPackageService(NullLogger<AzureDevOpsPackageService>.Instance));

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(
            () => packageService.GetPackageVersions("TestPackage"));
    }

    [Fact]
    public async Task NuGetPackageService_MultiSourceWithAuthentication_FallsBackCorrectly()
    {
        // Arrange
        var mockHttpClientService = new Mock<NuGetHttpClientService>(
            NullLogger<NuGetHttpClientService>.Instance,
            Options.Create(new NuGetConfiguration()));
        
        var mockMetaPackageDetector = new Mock<MetaPackageDetector>(
            NullLogger<MetaPackageDetector>.Instance);

        var sources = new List<NuGetSourceConfiguration>
        {
            new() { Name = "private-source", Url = "https://private.nuget.com/", IsEnabled = true, Priority = 100 },
            new() { Name = "public-source", Url = "https://api.nuget.org/", IsEnabled = true, Priority = 50 }
        };

        var unauthorizedHttpClient = CreateMockHttpClientWithUnauthorized();
        var workingHttpClient = CreateMockHttpClient("""
            {
                "versions": ["1.0.0", "1.1.0"]
            }
            """);

        mockHttpClientService.Setup(x => x.GetEnabledSources()).Returns(sources);
        mockHttpClientService.Setup(x => x.GetHttpClient("private-source")).Returns(unauthorizedHttpClient);
        mockHttpClientService.Setup(x => x.GetHttpClient("public-source")).Returns(workingHttpClient);

        var repositoryService = CreateNuGetRepositoryService();
        var packageService = new NuGetPackageService(
            NullLogger<NuGetPackageService>.Instance,
            repositoryService,
            mockMetaPackageDetector.Object,
            new AzureDevOpsPackageService(NullLogger<AzureDevOpsPackageService>.Instance));

        // Act
        var versions = await packageService.GetPackageVersions("TestPackage");

        // Assert
        Assert.Equal(2, versions.Count);
        Assert.Contains("1.0.0", versions);
        Assert.Contains("1.1.0", versions);
        
        // Verify fallback occurred
        mockHttpClientService.Verify(x => x.GetHttpClient("private-source"), Times.Once);
        mockHttpClientService.Verify(x => x.GetHttpClient("public-source"), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NuGetHttpClientService_WithInvalidBasicAuthCredentials_ThrowsException(string? invalidCredential)
    {
        // Arrange
        var configuration = new NuGetConfiguration
        {
            Sources = new List<NuGetSourceConfiguration>
            {
                new()
                {
                    Name = "invalid-source",
                    Url = "https://invalid.nuget.com/",
                    IsEnabled = true,
                    Priority = 100,
                    Username = invalidCredential,
                    Password = "password"
                }
            }
        };

        var options = Options.Create(configuration);
        var httpClientService = new NuGetHttpClientService(
            NullLogger<NuGetHttpClientService>.Instance,
            options);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => httpClientService.GetHttpClient("invalid-source"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NuGetHttpClientService_WithInvalidApiKey_ThrowsException(string? invalidApiKey)
    {
        // Arrange
        var configuration = new NuGetConfiguration
        {
            Sources = new List<NuGetSourceConfiguration>
            {
                new()
                {
                    Name = "invalid-api-source",
                    Url = "https://invalid-api.nuget.com/",
                    IsEnabled = true,
                    Priority = 100,
                    ApiKey = invalidApiKey
                }
            }
        };

        var options = Options.Create(configuration);
        var httpClientService = new NuGetHttpClientService(
            NullLogger<NuGetHttpClientService>.Instance,
            options);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => httpClientService.GetHttpClient("invalid-api-source"));
    }

    private static HttpClient CreateMockHttpClient(string responseContent)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseContent)
            });

        return new HttpClient(mockHandler.Object);
    }

    private static HttpClient CreateMockHttpClientWithUnauthorized()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Response status code does not indicate success: 401 (Unauthorized)."));

        return new HttpClient(mockHandler.Object);
    }
}
