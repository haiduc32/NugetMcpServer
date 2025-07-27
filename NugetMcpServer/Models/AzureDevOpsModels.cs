using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NuGetMcpServer.Models;

public class AzureDevOpsPackagesResponse
{
    [JsonPropertyName("value")]
    public List<AzureDevOpsPackage> Value { get; set; } = new();
}

public class AzureDevOpsPackage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("protocolType")]
    public string ProtocolType { get; set; } = string.Empty;
    
    [JsonPropertyName("versions")]
    public List<AzureDevOpsPackageVersion> Versions { get; set; } = new();
    
    [JsonPropertyName("sourceChain")]
    public List<SourceChainItem>? SourceChain { get; set; }
    
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class AzureDevOpsPackageVersion
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
    
    [JsonPropertyName("isLatest")]
    public bool IsLatest { get; set; }
    
    [JsonPropertyName("sourceChain")]
    public List<SourceChainItem>? SourceChain { get; set; }
}

public class SourceChainItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("upstreamSourceType")]
    public string UpstreamSourceType { get; set; } = string.Empty;
}

public class AzureDevOpsSearchResponse
{
    [JsonPropertyName("value")]
    public List<AzureDevOpsSearchResult> Value { get; set; } = new();
}

public class AzureDevOpsSearchResult
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("versions")]
    public List<string> Versions { get; set; } = new();
}
