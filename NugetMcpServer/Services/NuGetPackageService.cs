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
using System.Xml.Linq;

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

    public IReadOnlyList<ClassInfo> GetPackageClassesWithDocumentation(Stream packageStream)
    {
        return ExecuteWithErrorHandling(
            () => ExtractClassesWithDocumentationFromPackage(packageStream),
            ex => logger.LogWarning(ex, "Failed to extract classes with documentation from package"),
            () => Array.Empty<ClassInfo>());
    }

    public IReadOnlyList<string> GetPackageInterfacesWithoutLoading(Stream packageStream)
    {
        return ExecuteWithErrorHandling(
            () => ExtractInterfacesFromPackage(packageStream),
            ex => logger.LogWarning(ex, "Failed to extract interfaces from package"),
            () => Array.Empty<string>());
    }

    public IReadOnlyList<InterfaceInfo> GetPackageInterfacesWithDocumentation(Stream packageStream)
    {
        return ExecuteWithErrorHandling(
            () => ExtractInterfacesWithDocumentationFromPackage(packageStream),
            ex => logger.LogWarning(ex, "Failed to extract interfaces with documentation from package"),
            () => Array.Empty<InterfaceInfo>());
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

    private IReadOnlyList<ClassInfo> ExtractClassesWithDocumentationFromPackage(Stream packageStream)
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
            return Array.Empty<ClassInfo>();
        }

        // Extract XML documentation files
        var xmlDocumentation = ExtractXmlDocumentation(reader);

        var allClasses = new List<ClassInfo>();
        
        foreach (var libFile in libFiles)
        {
            logger.LogDebug("Processing DLL: {LibFile}", libFile);
            
            using var dllStream = reader.GetStream(libFile);
            using var memoryStream = new MemoryStream();
            dllStream.CopyTo(memoryStream);
            
            logger.LogDebug("DLL size: {Size} bytes", memoryStream.Length);
            
            var classes = ExtractClassesWithDocumentationFromMetadata(memoryStream.ToArray(), xmlDocumentation);
            logger.LogDebug("Found {ClassCount} classes in {LibFile}", classes.Count, libFile);
            
            allClasses.AddRange(classes);
        }

        var distinctClasses = allClasses
            .GroupBy(c => c.FullName)
            .Select(g => g.First())
            .ToList();
            
        logger.LogInformation("Total classes extracted: {TotalClasses} (distinct: {DistinctClasses})", allClasses.Count, distinctClasses.Count);
        
        return distinctClasses;
    }

    private Dictionary<string, string> ExtractXmlDocumentation(PackageArchiveReader reader)
    {
        var documentation = new Dictionary<string, string>();
        
        try
        {
            var xmlFiles = reader.GetFiles()
                .Where(file => file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && 
                              !file.Contains("/_rels/") && 
                              !file.Contains("/package/"))
                .ToList();

            logger.LogDebug("Found {XmlCount} XML documentation files: {Files}", xmlFiles.Count, string.Join(", ", xmlFiles));

            foreach (var xmlFile in xmlFiles)
            {
                try
                {
                    using var xmlStream = reader.GetStream(xmlFile);
                    var xmlDoc = XDocument.Load(xmlStream);
                    
                    var members = xmlDoc.Descendants("member")
                        .Where(m => m.Attribute("name")?.Value.StartsWith("T:") == true); // Type documentation
                    
                    foreach (var member in members)
                    {
                        var name = member.Attribute("name")?.Value;
                        if (name != null && name.StartsWith("T:"))
                        {
                            var typeName = name.Substring(2); // Remove "T:" prefix
                            var summary = member.Element("summary")?.Value?.Trim();
                            
                            if (!string.IsNullOrWhiteSpace(summary))
                            {
                                documentation[typeName] = summary;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to parse XML documentation file: {XmlFile}", xmlFile);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to extract XML documentation from package");
        }
        
        logger.LogDebug("Extracted documentation for {Count} types", documentation.Count);
        return documentation;
    }

    private IReadOnlyList<ClassInfo> ExtractClassesWithDocumentationFromMetadata(byte[] assemblyData, Dictionary<string, string> xmlDocumentation)
    {
        using var stream = new MemoryStream(assemblyData);
        using var peReader = new PEReader(stream);
        
        if (!peReader.HasMetadata)
        {
            logger.LogWarning("Assembly has no metadata. Size: {Size} bytes", assemblyData.Length);
            return Array.Empty<ClassInfo>();
        }

        var metadataReader = peReader.GetMetadataReader();
        var classes = new List<ClassInfo>();
        
        var totalTypes = metadataReader.TypeDefinitions.Count;
        logger.LogDebug("Found {TotalTypes} type definitions in assembly", totalTypes);

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
            
            var namespaceName = metadataReader.GetString(typeDef.Namespace);
            var typeName = metadataReader.GetString(typeDef.Name);
            var fullName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
            
            logger.LogTrace("Processing type: {FullName}, Attributes: {Attributes}", fullName, typeDef.Attributes);
            
            // Check for public visibility
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

            // Get XML documentation if available
            var documentation = xmlDocumentation.GetValueOrDefault(fullName);

            var classInfo = new ClassInfo
            {
                Name = typeName,
                FullName = fullName,
                AssemblyName = "Unknown",
                IsStatic = typeDef.Attributes.HasFlag(TypeAttributes.Sealed) && typeDef.Attributes.HasFlag(TypeAttributes.Abstract),
                IsAbstract = typeDef.Attributes.HasFlag(TypeAttributes.Abstract) && !typeDef.Attributes.HasFlag(TypeAttributes.Sealed),
                IsSealed = typeDef.Attributes.HasFlag(TypeAttributes.Sealed) && !typeDef.Attributes.HasFlag(TypeAttributes.Abstract),
                XmlDocumentation = documentation
            };

            logger.LogDebug("Adding class: {FullName} (Documentation: {HasDoc})", fullName, !string.IsNullOrWhiteSpace(documentation));
            classes.Add(classInfo);
        }

        logger.LogInformation("Extracted {ClassCount} classes from {TotalTypes} total types", classes.Count, totalTypes);
        return classes;
    }

    private IReadOnlyList<string> ExtractInterfacesFromPackage(Stream packageStream)
    {
        packageStream.Position = 0;
        using var reader = new PackageArchiveReader(packageStream, leaveStreamOpen: true);
        
        var libFiles = reader.GetLibItems()
            .SelectMany(lib => lib.Items)
            .Where(file => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!libFiles.Any())
            return Array.Empty<string>();

        var allInterfaces = new List<string>();
        
        foreach (var libFile in libFiles)
        {
            using var dllStream = reader.GetStream(libFile);
            using var memoryStream = new MemoryStream();
            dllStream.CopyTo(memoryStream);
            
            var interfaces = ExtractInterfacesFromMetadata(memoryStream.ToArray());
            allInterfaces.AddRange(interfaces);
        }

        return allInterfaces.Distinct().ToList();
    }

    private IReadOnlyList<InterfaceInfo> ExtractInterfacesWithDocumentationFromPackage(Stream packageStream)
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
            return Array.Empty<InterfaceInfo>();
        }

        // Extract XML documentation files
        var xmlDocumentation = ExtractXmlDocumentation(reader);

        var allInterfaces = new List<InterfaceInfo>();
        
        foreach (var libFile in libFiles)
        {
            logger.LogDebug("Processing DLL: {LibFile}", libFile);
            
            using var dllStream = reader.GetStream(libFile);
            using var memoryStream = new MemoryStream();
            dllStream.CopyTo(memoryStream);
            
            logger.LogDebug("DLL size: {Size} bytes", memoryStream.Length);
            
            var interfaces = ExtractInterfacesWithDocumentationFromMetadata(memoryStream.ToArray(), xmlDocumentation);
            logger.LogDebug("Found {InterfaceCount} interfaces in {LibFile}", interfaces.Count, libFile);
            
            allInterfaces.AddRange(interfaces);
        }

        var distinctInterfaces = allInterfaces
            .GroupBy(i => i.FullName)
            .Select(g => g.First())
            .ToList();
            
        logger.LogInformation("Total interfaces extracted: {TotalInterfaces} (distinct: {DistinctInterfaces})", allInterfaces.Count, distinctInterfaces.Count);
        
        return distinctInterfaces;
    }

    private IReadOnlyList<string> ExtractInterfacesFromMetadata(byte[] assemblyData)
    {
        using var stream = new MemoryStream(assemblyData);
        using var peReader = new PEReader(stream);
        
        if (!peReader.HasMetadata)
            return Array.Empty<string>();

        var metadataReader = peReader.GetMetadataReader();
        var interfaceNames = new List<string>();

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
            
            // Check for public visibility
            var visibility = typeDef.Attributes & TypeAttributes.VisibilityMask;
            if (visibility != TypeAttributes.Public && visibility != TypeAttributes.NestedPublic)
                continue;

            // Only include interfaces
            if (!typeDef.Attributes.HasFlag(TypeAttributes.Interface))
                continue;

            var namespaceName = metadataReader.GetString(typeDef.Namespace);
            var typeName = metadataReader.GetString(typeDef.Name);
            
            // Skip compiler-generated types
            if (typeName.StartsWith("<") || typeName.Contains("$"))
                continue;

            var fullName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
            interfaceNames.Add(fullName);
        }

        return interfaceNames;
    }

    private IReadOnlyList<InterfaceInfo> ExtractInterfacesWithDocumentationFromMetadata(byte[] assemblyData, Dictionary<string, string> xmlDocumentation)
    {
        using var stream = new MemoryStream(assemblyData);
        using var peReader = new PEReader(stream);
        
        if (!peReader.HasMetadata)
        {
            logger.LogWarning("Assembly has no metadata. Size: {Size} bytes", assemblyData.Length);
            return Array.Empty<InterfaceInfo>();
        }

        var metadataReader = peReader.GetMetadataReader();
        var interfaces = new List<InterfaceInfo>();
        
        var totalTypes = metadataReader.TypeDefinitions.Count;
        logger.LogDebug("Found {TotalTypes} type definitions in assembly", totalTypes);

        foreach (var typeDefHandle in metadataReader.TypeDefinitions)
        {
            var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
            
            var namespaceName = metadataReader.GetString(typeDef.Namespace);
            var typeName = metadataReader.GetString(typeDef.Name);
            var fullName = string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
            
            logger.LogTrace("Processing type: {FullName}, Attributes: {Attributes}", fullName, typeDef.Attributes);
            
            // Check for public visibility
            var visibility = typeDef.Attributes & TypeAttributes.VisibilityMask;
            if (visibility != TypeAttributes.Public && visibility != TypeAttributes.NestedPublic)
            {
                logger.LogTrace("Skipping non-public type: {FullName}", fullName);
                continue;
            }

            // Only include interfaces
            if (!typeDef.Attributes.HasFlag(TypeAttributes.Interface))
            {
                logger.LogTrace("Skipping non-interface: {FullName}", fullName);
                continue;
            }
            
            // Skip compiler-generated types
            if (typeName.StartsWith("<") || typeName.Contains("$"))
            {
                logger.LogTrace("Skipping compiler-generated type: {FullName}", fullName);
                continue;
            }

            // Get XML documentation if available
            var documentation = xmlDocumentation.GetValueOrDefault(fullName);

            var interfaceInfo = new InterfaceInfo
            {
                Name = typeName,
                FullName = fullName,
                AssemblyName = "Unknown",
                XmlDocumentation = documentation
            };

            logger.LogDebug("Adding interface: {FullName} (Documentation: {HasDoc})", fullName, !string.IsNullOrWhiteSpace(documentation));
            interfaces.Add(interfaceInfo);
        }

        logger.LogInformation("Extracted {InterfaceCount} interfaces from {TotalTypes} total types", interfaces.Count, totalTypes);
        return interfaces;
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
        // Return empty results for empty or whitespace-only queries
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<PackageInfo>();
        }

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
