using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol.Core.Types;

using NuGetMcpServer.Extensions;
using NuGetMcpServer.Models;

namespace NuGetMcpServer.Services;

public class NuGetPackageService(ILogger<NuGetPackageService> logger, NuGetRepositoryService repositoryService, MetaPackageDetector metaPackageDetector, AzureDevOpsPackageService azureDevOpsPackageService)
{

    public async Task<string> GetLatestVersion(string packageId)
    {
        IReadOnlyList<string> versions = await GetPackageVersions(packageId);
        return versions.Last();
    }

    public async Task<IReadOnlyList<string>> GetPackageVersions(string packageId)
    {
        return await repositoryService.GetPackageVersionsAsync(packageId);
    }

    public async Task<IReadOnlyList<string>> GetLatestVersions(string packageId, int count = 20)
    {
        IReadOnlyList<string> versions = await GetPackageVersions(packageId);
        return versions.TakeLast(count).ToList();
    }

    public async Task<MemoryStream> DownloadPackageAsync(string packageId, string version, IProgressNotifier? progress = null)
    {
        var sources = repositoryService.GetEnabledSources();
        
        foreach (var source in sources)
        {
            try
            {
                // Use NuGet.Protocol for standard feeds
                progress?.ReportMessage($"Starting package download {packageId} v{version} from {source.Name}");

                using var packageStream = await repositoryService.DownloadPackageAsync(packageId, version);
                var memoryStream = new MemoryStream();
                await packageStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                progress?.ReportMessage("Package downloaded successfully");
                return memoryStream;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to download package from source {SourceName}, trying next source", source.Name);
            }
        }
        
        throw new InvalidOperationException($"Failed to download package {packageId} v{version} from all configured sources");
    }

    public IReadOnlyList<string> GetAssemblyTypesWithoutLoading(byte[] assemblyData)
    {
        return ExecuteWithErrorHandling(
            () => ExtractTypesFromMetadata(assemblyData),
            ex => logger.LogWarning(ex, "Failed to extract types from assembly metadata. Assembly size: {Size} bytes", assemblyData.Length),
            () => Array.Empty<string>());
    }

    public IReadOnlyList<string> GetAssemblyClassesWithoutLoading(byte[] assemblyData)
    {
        return ExecuteWithErrorHandling(
            () => ExtractClassesFromMetadata(assemblyData),
            ex => logger.LogWarning(ex, "Failed to extract classes from assembly metadata. Assembly size: {Size} bytes", assemblyData.Length),
            () => Array.Empty<string>());
    }

    private IReadOnlyList<string> ExtractClassesFromMetadata(byte[] assemblyData)
    {
        using var stream = new MemoryStream(assemblyData);
        using var peReader = new PEReader(stream);
        
        if (!peReader.HasMetadata)
        {
            logger.LogWarning("Assembly has no metadata. Size: {Size} bytes", assemblyData.Length);
            return Array.Empty<string>();
        }

        var metadataReader = peReader.GetMetadataReader();
        var classNames = new List<string>();
        
        var totalTypes = metadataReader.TypeDefinitions.Count;
        logger.LogDebug("Found {TotalTypes} type definitions in assembly", totalTypes);

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
            
            var namespaceName = metadataReader.GetString(typeDef.Namespace);
            var typeName = metadataReader.GetString(typeDef.Name);
            var fullName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
            
            logger.LogTrace("Processing type: {FullName}, Attributes: {Attributes}", fullName, typeDef.Attributes);
            
            // Check for public visibility more correctly
            var visibility = typeDef.Attributes & TypeAttributes.VisibilityMask;
            if (visibility != TypeAttributes.Public && visibility != TypeAttributes.NestedPublic)
            {
                logger.LogTrace("Skipping non-public type: {FullName}", fullName);
                continue;
            }

            // Only include classes (not interfaces, enums, delegates, etc.)
            if (typeDef.Attributes.HasFlag(TypeAttributes.Interface))
            {
                logger.LogTrace("Skipping interface: {FullName}", fullName);
                continue;
            }
            
            // Skip compiler-generated types
            if (typeName.StartsWith("<") || typeName.Contains("$"))
            {
                logger.LogTrace("Skipping compiler-generated type: {FullName}", fullName);
                continue;
            }

            logger.LogDebug("Adding class: {FullName}", fullName);
            classNames.Add(fullName);
        }

        logger.LogInformation("Extracted {ClassCount} classes from {TotalTypes} total types", classNames.Count, totalTypes);
        return classNames;
    }

    private IReadOnlyList<string> ExtractTypesFromMetadata(byte[] assemblyData)
    {
        using var stream = new MemoryStream(assemblyData);
        using var peReader = new PEReader(stream);
        
        if (!peReader.HasMetadata)
            return Array.Empty<string>();

        var metadataReader = peReader.GetMetadataReader();
        var typeNames = new List<string>();

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
            
            // Check for public visibility more correctly
            var visibility = typeDef.Attributes & TypeAttributes.VisibilityMask;
            if (visibility != TypeAttributes.Public && visibility != TypeAttributes.NestedPublic)
                continue;

            var namespaceName = metadataReader.GetString(typeDef.Namespace);
            var typeName = metadataReader.GetString(typeDef.Name);
            
            // Skip compiler-generated types
            if (typeName.StartsWith("<") || typeName.Contains("$"))
                continue;

            var fullName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
            typeNames.Add(fullName);
        }

        return typeNames;
    }

    public IReadOnlyList<string> GetPackageTypesWithoutLoading(Stream packageStream)
    {
        return ExecuteWithErrorHandling(
            () => ExtractTypesFromPackage(packageStream),
            ex => logger.LogWarning(ex, "Failed to extract types from package"),
            () => Array.Empty<string>());
    }

    public IReadOnlyList<string> GetPackageClassesWithoutLoading(Stream packageStream)
    {
        return ExecuteWithErrorHandling(
            () => ExtractClassesFromPackage(packageStream),
            ex => logger.LogWarning(ex, "Failed to extract classes from package"),
            () => Array.Empty<string>());
    }

    private IReadOnlyList<string> ExtractClassesFromPackage(Stream packageStream)
    {
        packageStream.Position = 0;
        using var reader = new PackageArchiveReader(packageStream, leaveStreamOpen: true);
        
        var libFiles = reader.GetLibItems()
            .SelectMany(lib => lib.Items)
            .Where(file => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();

        logger.LogDebug("Found {DllCount} DLL files in package: {Files}", libFiles.Count, string.Join(", ", libFiles));

        if (!libFiles.Any())
        {
            logger.LogWarning("No DLL files found in package");
            return Array.Empty<string>();
        }

        var allClasses = new List<string>();
        
        foreach (var libFile in libFiles)
        {
            logger.LogDebug("Processing DLL: {LibFile}", libFile);
            
            using var dllStream = reader.GetStream(libFile);
            using var memoryStream = new MemoryStream();
            dllStream.CopyTo(memoryStream);
            
            logger.LogDebug("DLL size: {Size} bytes", memoryStream.Length);
            
            var classes = GetAssemblyClassesWithoutLoading(memoryStream.ToArray());
            logger.LogDebug("Found {ClassCount} classes in {LibFile}", classes.Count, libFile);
            
            allClasses.AddRange(classes);
        }

        var distinctClasses = allClasses.Distinct().ToList();
        logger.LogInformation("Total classes extracted: {TotalClasses} (distinct: {DistinctClasses})", allClasses.Count, distinctClasses.Count);
        
        return distinctClasses;
    }

    private IReadOnlyList<string> ExtractTypesFromPackage(Stream packageStream)
    {
        packageStream.Position = 0;
        using var reader = new PackageArchiveReader(packageStream, leaveStreamOpen: true);
        
        var libFiles = reader.GetLibItems()
            .SelectMany(lib => lib.Items)
            .Where(file => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!libFiles.Any())
            return Array.Empty<string>();

        var allTypes = new List<string>();
        
        foreach (var libFile in libFiles)
        {
            using var dllStream = reader.GetStream(libFile);
            using var memoryStream = new MemoryStream();
            dllStream.CopyTo(memoryStream);
            
            var types = GetAssemblyTypesWithoutLoading(memoryStream.ToArray());
            allTypes.AddRange(types);
        }

        return allTypes.Distinct().ToList();
    }

    /// <summary>
    /// Legacy method for loading assemblies when reflection is needed.
    /// Consider using GetAssemblyTypesWithoutLoading for type names only.
    /// </summary>
    public (Assembly? assembly, Type[] types) LoadAssemblyFromMemoryWithTypes(byte[] assemblyData)
    {
        return ExecuteWithErrorHandling(
            () => LoadAssemblyWithReflection(assemblyData),
            ex => logger.LogWarning(ex, "Failed to load assembly from memory. Assembly size: {Size} bytes", assemblyData.Length),
            () => (null, Array.Empty<Type>()));
    }

    private (Assembly? assembly, Type[] types) LoadAssemblyWithReflection(byte[] assemblyData)
    {
        var assembly = Assembly.Load(assemblyData);

        try
        {
            var types = assembly.GetTypes();
            return (assembly, types);
        }
        catch (ReflectionTypeLoadException ex)
        {
            logger.LogWarning("Some types could not be loaded from assembly due to missing dependencies. Loaded {LoadedCount} out of {TotalCount} types",
                ex.Types.Count(t => t != null), ex.Types.Length);

            if (ex.LoaderExceptions != null)
            {
                foreach (var loaderEx in ex.LoaderExceptions.Where(e => e != null))
                {
                    logger.LogDebug("Loader exception: {Exception}", loaderEx!.Message);
                }
            }

            var loadedTypes = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            return (assembly, loadedTypes);
        }
    }

    private T ExecuteWithErrorHandling<T>(
        Func<T> operation, 
        Action<Exception> onError, 
        Func<T> fallback)
    {
        try
        {
            return operation();
        }
        catch (Exception ex)
        {
            onError(ex);
            return fallback();
        }
    }

    public List<PackageDependency> GetPackageDependencies(Stream packageStream)
    {
        try
        {
            packageStream.Position = 0;
            using var reader = new PackageArchiveReader(packageStream, leaveStreamOpen: true);
            using var nuspecStream = reader.GetNuspec();
            var nuspecReader = new NuspecReader(nuspecStream);
            var dependencyGroups = nuspecReader.GetDependencyGroups();

            var dependencies = dependencyGroups
                .SelectMany(group => group.Packages.Select(package => new PackageDependency
                {
                    Id = package.Id,
                    Version = package.VersionRange?.ToString() ?? "latest"
                }))
                .DistinctBy(d => d.Id)
                .ToList();

            return dependencies;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error extracting package dependencies using NuGet API, falling back to manual parsing");
            return [];
        }
    }

    public async Task<IReadOnlyCollection<PackageInfo>> SearchPackagesAsync(string query, int take = 20)
    {
        var results = new List<PackageInfo>();
        var sources = repositoryService.GetEnabledSources();

        foreach (var source in sources)
        {
            try
            {
                var repository = repositoryService.GetRepository(source.Name);
                var searchResource = await repository.GetResourceAsync<PackageSearchResource>();
                
                var searchFilter = new SearchFilter(includePrerelease: false)
                {
                    SupportedFrameworks = []
                };

                var searchResults = await searchResource.SearchAsync(
                    query,
                    searchFilter,
                    skip: 0,
                    take: take,
                    new NuGet.Common.NullLogger(),
                    cancellationToken: default);

                foreach (var result in searchResults)
                {
                    var packageInfo = new PackageInfo
                    {
                        PackageId = result.Identity.Id,
                        Version = result.Identity.Version.ToString(),
                        Description = result.Description ?? string.Empty,
                        Authors = result.Authors?.Split(',').Select(a => a.Trim()).ToList() ?? [],
                        Tags = result.Tags?.Split(' ', ',').Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [],
                        ProjectUrl = result.ProjectUrl?.ToString() ?? string.Empty,
                        LicenseUrl = result.LicenseUrl?.ToString() ?? string.Empty,
                        DownloadCount = result.DownloadCount ?? 0,
                        IsMetaPackage = false, // Will be determined later if needed
                        Dependencies = []
                    };

                    results.Add(packageInfo);
                }

                if (results.Any())
                {
                    logger.LogInformation("Found {ResultCount} packages for query '{Query}' from {SourceName}", 
                        results.Count, query, source.Name);
                    break; // Use first successful source
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to search packages from source {SourceName}, trying next source", source.Name);
            }
        }

        return results.OrderByDescending(p => p.DownloadCount).ToList();
    }

    public PackageInfo GetPackageInfoAsync(Stream packageStream, string packageId, string version)
    {
        try
        {
            var isMetaPackage = metaPackageDetector.IsMetaPackage(packageStream, packageId);
            var dependencies = GetPackageDependencies(packageStream);

            packageStream.Position = 0;
            using var reader = new PackageArchiveReader(packageStream, leaveStreamOpen: true);
            using var nuspecStream = reader.GetNuspec();
            var nuspecReader = new NuspecReader(nuspecStream);

            var authors = nuspecReader.GetAuthors()?.Split(',').Select(a => a.Trim()).ToList() ?? [];
            var tags = nuspecReader.GetTags()?.Split(' ', ',').Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];

            return new PackageInfo
            {
                PackageId = packageId,
                Version = version,
                Description = nuspecReader.GetDescription() ?? string.Empty,
                Authors = authors,
                Tags = tags,
                ProjectUrl = nuspecReader.GetProjectUrl()?.ToString() ?? string.Empty,
                LicenseUrl = nuspecReader.GetLicenseUrl()?.ToString() ?? string.Empty,
                IsMetaPackage = isMetaPackage,
                Dependencies = dependencies
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting package info for {PackageId} v{Version}", packageId, version);
            return new PackageInfo
            {
                PackageId = packageId,
                Version = version,
                Description = "Error retrieving package information",
                IsMetaPackage = false,
                Dependencies = []
            };
        }
    }
}
