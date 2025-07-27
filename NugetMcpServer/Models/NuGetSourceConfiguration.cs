namespace NuGetMcpServer.Models;

public class NuGetSourceConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? ApiKey { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; } = 0;
}
