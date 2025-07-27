using System.Collections.Generic;

namespace NuGetMcpServer.Models;

public class NuGetConfiguration
{
    public List<NuGetSourceConfiguration> Sources { get; set; } = [];
    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int MaxRetryAttempts { get; set; } = 3;
}
