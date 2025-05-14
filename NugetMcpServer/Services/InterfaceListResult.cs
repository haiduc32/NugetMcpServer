using System.Collections.Generic;

namespace NuGetMcpServer.Services;

/// <summary>
/// Response model for interface listing including package version information
/// </summary>
public class InterfaceListResult
{
    /// <summary>
    /// NuGet package ID
    /// </summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>
    /// Package version
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// List of interfaces found in the package
    /// </summary>
    public List<InterfaceInfo> Interfaces { get; set; } = [];
}
