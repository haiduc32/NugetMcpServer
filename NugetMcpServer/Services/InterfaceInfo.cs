namespace NuGetMcpServer.Services;

/// <summary>
/// Model for interface information
/// </summary>
public class InterfaceInfo
{
    /// <summary>
    /// Interface name (without namespace)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Full interface name with namespace
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Assembly name where interface is defined
    /// </summary>
    public string AssemblyName { get; set; } = string.Empty;
}
