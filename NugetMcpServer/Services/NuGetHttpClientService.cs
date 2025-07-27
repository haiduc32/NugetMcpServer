using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NuGetMcpServer.Models;

namespace NuGetMcpServer.Services;

public class NuGetHttpClientService(ILogger<NuGetHttpClientService> logger, IOptions<NuGetConfiguration> configuration)
{
    private readonly NuGetConfiguration _configuration = configuration.Value;
    private readonly Dictionary<string, HttpClient> _httpClients = new();

    public virtual HttpClient GetHttpClient(string sourceName)
    {
        if (_httpClients.TryGetValue(sourceName, out var existingClient))
        {
            return existingClient;
        }

        var source = _configuration.Sources.FirstOrDefault(s => s.Name == sourceName);
        if (source == null)
        {
            throw new ArgumentException($"Source '{sourceName}' not found in configuration", nameof(sourceName));
        }

        var httpClient = CreateHttpClientForSource(source);
        _httpClients[sourceName] = httpClient;
        return httpClient;
    }

    public virtual IEnumerable<NuGetSourceConfiguration> GetEnabledSources()
    {
        return _configuration.Sources.Where(s => s.IsEnabled).OrderByDescending(s => s.Priority);
    }

    public virtual NuGetSourceConfiguration GetPrimarySource()
    {
        return GetEnabledSources().FirstOrDefault() 
               ?? throw new InvalidOperationException("No enabled NuGet sources configured");
    }

    private HttpClient CreateHttpClientForSource(NuGetSourceConfiguration source)
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_configuration.DefaultTimeoutSeconds)
        };

        // Validate authentication configuration - catch empty strings and whitespace
        if (source.ApiKey == "" || (source.ApiKey != null && string.IsNullOrWhiteSpace(source.ApiKey)))
        {
            throw new InvalidOperationException($"API key cannot be empty or whitespace for source '{source.Name}'");
        }

        if (source.Username == "" || (source.Username != null && string.IsNullOrWhiteSpace(source.Username)))
        {
            throw new InvalidOperationException($"Username cannot be empty or whitespace for source '{source.Name}'");
        }

        if (!string.IsNullOrWhiteSpace(source.ApiKey))
        {
            httpClient.DefaultRequestHeaders.Add("X-NuGet-ApiKey", source.ApiKey);
            logger.LogDebug("Configured API key authentication for source '{SourceName}'", source.Name);
        }
        else if (!string.IsNullOrWhiteSpace(source.Username) && !string.IsNullOrWhiteSpace(source.Password))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{source.Username}:{source.Password}"));
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {credentials}");
            logger.LogDebug("Configured basic authentication for source '{SourceName}'", source.Name);
        }

        logger.LogInformation("Created HTTP client for NuGet source '{SourceName}' at '{Url}'", source.Name, source.Url);
        return httpClient;
    }

    public void Dispose()
    {
        foreach (var client in _httpClients.Values)
        {
            client.Dispose();
        }
        _httpClients.Clear();
    }
}
