using System.Collections.Generic;
using System.Text;

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
    public List<InterfaceInfo> Interfaces { get; set; } = [];    /// <summary>
    /// Returns a formatted string representation of the interface list
    /// </summary>
    /// <returns>Formatted list of interfaces</returns>
    public string ToFormattedString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"/* INTERFACES FROM {PackageId} v{Version} */");
        sb.AppendLine();
        
        foreach (var iface in Interfaces)
        {
            var formattedName = iface.GetFormattedFullName();
            sb.AppendLine($"- {formattedName} ({iface.AssemblyName})");
        }
        
        return sb.ToString();
    }
}
