using System;
using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NuGetMcpServer.Models;
using NuGetMcpServer.Services;
using Xunit;

namespace NuGetMcpServer.Tests.Services;

public class NuGetHttpClientServiceTests : IDisposable
{
    private readonly NuGetHttpClientService _service;
    private readonly NuGetConfiguration _configuration;

    public NuGetHttpClientServiceTests()
    {
        _configuration = new NuGetConfiguration
        {
            Sources =
            [
                new NuGetSourceConfiguration
                {
                    Name = "nuget.org",
                    Url = "https://api.nuget.org/v3-flatcontainer/",
                    IsEnabled = true,
                    Priority = 100
                },
                new NuGetSourceConfiguration
                {
                    Name = "private-basic-auth",
                    Url = "https://private.example.com/v3-flatcontainer/",
                    Username = "testuser",
                    Password = "testpass",
                    IsEnabled = true,
                    Priority = 80
                },
                new NuGetSourceConfiguration
                {
                    Name = "private-api-key",
                    Url = "https://apikey.example.com/v3-flatcontainer/",
                    ApiKey = "test-api-key-123",
                    IsEnabled = true,
                    Priority = 70
                },
                new NuGetSourceConfiguration
                {
                    Name = "disabled-source",
                    Url = "https://disabled.example.com/v3-flatcontainer/",
                    IsEnabled = false,
                    Priority = 60
                }
            ],
            DefaultTimeoutSeconds = 30,
            MaxRetryAttempts = 3
        };

        var options = Options.Create(_configuration);
        _service = new NuGetHttpClientService(NullLogger<NuGetHttpClientService>.Instance, options);
    }

    [Fact]
    public void GetHttpClient_WithValidSourceName_ReturnsHttpClient()
    {
        // Act
        var httpClient = _service.GetHttpClient("nuget.org");

        // Assert
        Assert.NotNull(httpClient);
        Assert.Equal(TimeSpan.FromSeconds(30), httpClient.Timeout);
    }

    [Fact]
    public void GetHttpClient_WithBasicAuth_ConfiguresAuthorizationHeader()
    {
        // Act
        var httpClient = _service.GetHttpClient("private-basic-auth");

        // Assert
        Assert.NotNull(httpClient);
        Assert.True(httpClient.DefaultRequestHeaders.Contains("Authorization"));
        var authHeader = httpClient.DefaultRequestHeaders.Authorization;
        Assert.NotNull(authHeader);
        Assert.Equal("Basic", authHeader.Scheme);
    }

    [Fact]
    public void GetHttpClient_WithApiKey_ConfiguresApiKeyHeader()
    {
        // Act
        var httpClient = _service.GetHttpClient("private-api-key");

        // Assert
        Assert.NotNull(httpClient);
        Assert.True(httpClient.DefaultRequestHeaders.Contains("X-NuGet-ApiKey"));
        var apiKeyHeader = httpClient.DefaultRequestHeaders.GetValues("X-NuGet-ApiKey");
        Assert.Contains("test-api-key-123", apiKeyHeader);
    }

    [Fact]
    public void GetHttpClient_WithInvalidSourceName_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _service.GetHttpClient("nonexistent"));
        Assert.Contains("Source 'nonexistent' not found in configuration", exception.Message);
    }

    [Fact]
    public void GetHttpClient_SameSourceTwice_ReturnsSameInstance()
    {
        // Act
        var httpClient1 = _service.GetHttpClient("nuget.org");
        var httpClient2 = _service.GetHttpClient("nuget.org");

        // Assert
        Assert.Same(httpClient1, httpClient2);
    }

    [Fact]
    public void GetEnabledSources_ReturnsOnlyEnabledSources()
    {
        // Act
        var enabledSources = _service.GetEnabledSources().ToList();

        // Assert
        Assert.Equal(3, enabledSources.Count);
        Assert.DoesNotContain(enabledSources, s => s.Name == "disabled-source");
        Assert.Contains(enabledSources, s => s.Name == "nuget.org");
        Assert.Contains(enabledSources, s => s.Name == "private-basic-auth");
        Assert.Contains(enabledSources, s => s.Name == "private-api-key");
    }

    [Fact]
    public void GetEnabledSources_ReturnsSourcesInPriorityOrder()
    {
        // Act
        var enabledSources = _service.GetEnabledSources().ToList();

        // Assert
        Assert.Equal("nuget.org", enabledSources[0].Name); // Priority 100
        Assert.Equal("private-basic-auth", enabledSources[1].Name); // Priority 80
        Assert.Equal("private-api-key", enabledSources[2].Name); // Priority 70
    }

    [Fact]
    public void GetPrimarySource_ReturnsHighestPriorityEnabledSource()
    {
        // Act
        var primarySource = _service.GetPrimarySource();

        // Assert
        Assert.Equal("nuget.org", primarySource.Name);
        Assert.Equal(100, primarySource.Priority);
    }

    [Fact]
    public void GetPrimarySource_WithNoEnabledSources_ThrowsInvalidOperationException()
    {
        // Arrange
        var emptyConfiguration = new NuGetConfiguration { Sources = [] };
        var options = Options.Create(emptyConfiguration);
        var emptyService = new NuGetHttpClientService(NullLogger<NuGetHttpClientService>.Instance, options);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => emptyService.GetPrimarySource());
        Assert.Contains("No enabled NuGet sources configured", exception.Message);
    }

    public void Dispose()
    {
        _service?.Dispose();
    }
}
