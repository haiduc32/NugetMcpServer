using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

using NuGetMcpServer.Extensions;
using NuGetMcpServer.Models;

using NuGetLogger = NuGet.Common.ILogger;
using NuGetLogLevel = NuGet.Common.LogLevel;

namespace NuGetMcpServer.Services;

public class NuGetRepositoryService : IDisposable
{
    private readonly NuGetConfiguration _configuration;
    private readonly ILogger<NuGetRepositoryService> _logger;
    private readonly Dictionary<string, SourceRepository> _repositories = new();
    private readonly Dictionary<string, PackageSource> _packageSources = new();
    private readonly SourceCacheContext _sourceCacheContext;
    private readonly NuGetLogger _nugetLogger;

    public NuGetRepositoryService(ILogger<NuGetRepositoryService> logger, IOptions<NuGetConfiguration> configuration)
    {
        _configuration = configuration.Value;
        _logger = logger;
        _nugetLogger = new NuGetLoggerAdapter(_logger);
        _sourceCacheContext = new SourceCacheContext();
        
        InitializeRepositories();
    }

    private void InitializeRepositories()
    {
        foreach (var sourceConfig in _configuration.Sources.Where(s => s.IsEnabled))
        {
            try
            {
                var packageSource = CreatePackageSource(sourceConfig);
                _packageSources[sourceConfig.Name] = packageSource;

                var repository = Repository.Factory.GetCoreV3(packageSource);
                _repositories[sourceConfig.Name] = repository;

                _logger.LogInformation("Initialized NuGet repository for source '{SourceName}' at '{Url}'", 
                    sourceConfig.Name, sourceConfig.Url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize repository for source '{SourceName}'", sourceConfig.Name);
            }
        }
    }

    private PackageSource CreatePackageSource(NuGetSourceConfiguration sourceConfig)
    {
        var packageSource = new PackageSource(sourceConfig.Url, sourceConfig.Name)
        {
            IsEnabled = sourceConfig.IsEnabled
        };

        if (!string.IsNullOrWhiteSpace(sourceConfig.Username) && !string.IsNullOrWhiteSpace(sourceConfig.Password))
        {
            packageSource.Credentials = new PackageSourceCredential(
                sourceConfig.Name,
                sourceConfig.Username,
                sourceConfig.Password,
                isPasswordClearText: true,
                validAuthenticationTypesText: null);
                
            _logger.LogDebug("Configured credentials authentication for source '{SourceName}'", sourceConfig.Name);
        }
        else if (!string.IsNullOrWhiteSpace(sourceConfig.ApiKey))
        {
            packageSource.Credentials = new PackageSourceCredential(
                sourceConfig.Name,
                "AnyUserName", // NuGet doesn't require a specific username for API keys
                sourceConfig.ApiKey,
                isPasswordClearText: true,
                validAuthenticationTypesText: null);
                
            _logger.LogDebug("Configured API key authentication for source '{SourceName}'", sourceConfig.Name);
        }

        return packageSource;
    }

    public virtual IEnumerable<NuGetSourceConfiguration> GetEnabledSources()
    {
        return _configuration.Sources
            .Where(s => s.IsEnabled)
            .OrderByDescending(s => s.Priority);
    }

    public virtual NuGetSourceConfiguration GetPrimarySource()
    {
        return GetEnabledSources().FirstOrDefault() 
               ?? throw new InvalidOperationException("No enabled NuGet sources configured");
    }

    public virtual SourceRepository GetRepository(string sourceName)
    {
        if (_repositories.TryGetValue(sourceName, out var repository))
        {
            return repository;
        }

        throw new ArgumentException($"Repository for source '{sourceName}' not found", nameof(sourceName));
    }

    public virtual IEnumerable<SourceRepository> GetRepositories()
    {
        var enabledSources = GetEnabledSources();
        return enabledSources.Select(source => _repositories[source.Name]);
    }

    public async Task<IReadOnlyList<string>> GetPackageVersionsAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var sources = GetEnabledSources();
        var exceptions = new List<Exception>();

        foreach (var source in sources)
        {
            try
            {
                var repository = GetRepository(source.Name);
                var metadataResource = await repository.GetResourceAsync<PackageMetadataResource>(cancellationToken);
                
                var metadata = await metadataResource.GetMetadataAsync(
                    packageId,
                    includePrerelease: false,
                    includeUnlisted: false,
                    _sourceCacheContext,
                    _nugetLogger,
                    cancellationToken);

                var versions = metadata
                    .OrderBy(m => m.Identity.Version)
                    .Select(m => m.Identity.Version.ToString())
                    .Distinct()
                    .ToList();

                if (versions.Any())
                {
                    _logger.LogInformation("Found {VersionCount} versions for package {PackageId} from {SourceName}", 
                        versions.Count, packageId, source.Name);
                    return versions;
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
                _logger.LogWarning(ex, "Failed to get package versions from source {SourceName}, trying next source", source.Name);
            }
        }

        return exceptions.HandleMultiSourceExceptions<IReadOnlyList<string>>(packageId, _logger);
    }

    public async Task<Stream> DownloadPackageAsync(string packageId, string version, CancellationToken cancellationToken = default)
    {
        var sources = GetEnabledSources();
        
        foreach (var source in sources)
        {
            try
            {
                var repository = GetRepository(source.Name);
                var downloadResource = await repository.GetResourceAsync<DownloadResource>(cancellationToken);
                
                var packageVersion = NuGetVersion.Parse(version);
                var packageIdentity = new NuGet.Packaging.Core.PackageIdentity(packageId, packageVersion);

                var downloadResult = await downloadResource.GetDownloadResourceResultAsync(
                    packageIdentity,
                    new PackageDownloadContext(_sourceCacheContext),
                    SettingsUtility.GetGlobalPackagesFolder(NullSettings.Instance),
                    _nugetLogger,
                    cancellationToken);

                if (downloadResult.Status == DownloadResourceResultStatus.Available && downloadResult.PackageStream != null)
                {
                    _logger.LogInformation("Downloaded package {PackageId} v{Version} from {SourceName}", 
                        packageId, version, source.Name);
                    
                    var memoryStream = new MemoryStream();
                    await downloadResult.PackageStream.CopyToAsync(memoryStream, cancellationToken);
                    memoryStream.Position = 0;
                    return memoryStream;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download package from source {SourceName}, trying next source", source.Name);
            }
        }
        
        throw new InvalidOperationException($"Failed to download package {packageId} v{version} from all configured sources");
    }

    public void Dispose()
    {
        _sourceCacheContext?.Dispose();
        
        // SourceRepository doesn't implement IDisposable directly
        _repositories.Clear();
        _packageSources.Clear();
    }

    private class NuGetLoggerAdapter : NuGetLogger
    {
        private readonly ILogger<NuGetRepositoryService> _logger;

        public NuGetLoggerAdapter(ILogger<NuGetRepositoryService> logger)
        {
            _logger = logger;
        }

        public void LogDebug(string data) => _logger.LogDebug("{Data}", data);
        public void LogVerbose(string data) => _logger.LogTrace("{Data}", data);
        public void LogInformation(string data) => _logger.LogInformation("{Data}", data);
        public void LogMinimal(string data) => _logger.LogInformation("{Data}", data);
        public void LogWarning(string data) => _logger.LogWarning("{Data}", data);
        public void LogError(string data) => _logger.LogError("{Data}", data);
        public void LogInformationSummary(string data) => _logger.LogInformation("{Data}", data);

        public Task LogAsync(NuGetLogLevel level, string data)
        {
            Log(level, data);
            return Task.CompletedTask;
        }

        public void Log(NuGetLogLevel level, string data)
        {
            switch (level)
            {
                case NuGetLogLevel.Debug:
                    LogDebug(data);
                    break;
                case NuGetLogLevel.Verbose:
                    LogVerbose(data);
                    break;
                case NuGetLogLevel.Information:
                    LogInformation(data);
                    break;
                case NuGetLogLevel.Minimal:
                    LogMinimal(data);
                    break;
                case NuGetLogLevel.Warning:
                    LogWarning(data);
                    break;
                case NuGetLogLevel.Error:
                    LogError(data);
                    break;
                default:
                    LogInformation(data);
                    break;
            }
        }

        public Task LogAsync(ILogMessage message) => LogAsync(message.Level, message.Message);
        public void Log(ILogMessage message) => Log(message.Level, message.Message);
    }
}
