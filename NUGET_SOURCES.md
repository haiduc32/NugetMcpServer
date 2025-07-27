# NuGet Sources Configuration

This document explains how to configure custom NuGet sources for the NuGetMcpServer, including private feeds that require authentication.

## Configuration File

The server uses `appsettings.json` for configuration. Create or modify this file in the same directory as the executable.

### Basic Configuration

```json
{
  "NuGet": {
    "Sources": [
      {
        "Name": "nuget.org",
        "Url": "https://api.nuget.org/v3-flatcontainer/",
        "IsEnabled": true,
        "Priority": 100
      }
    ],
    "DefaultTimeoutSeconds": 30,
    "MaxRetryAttempts": 3
  }
}
```

### Advanced Configuration with Multiple Sources

```json
{
  "NuGet": {
    "Sources": [
      {
        "Name": "nuget.org",
        "Url": "https://api.nuget.org/v3-flatcontainer/",
        "IsEnabled": true,
        "Priority": 100
      },
      {
        "Name": "private-company-feed",
        "Url": "https://pkgs.dev.azure.com/company/_packaging/internal/nuget/v3/index.json",
        "Username": "user@company.com",
        "Password": "your-personal-access-token",
        "IsEnabled": true,
        "Priority": 80
      },
      {
        "Name": "github-packages",
        "Url": "https://nuget.pkg.github.com/organization/index.json",
        "ApiKey": "ghp_your-github-personal-access-token",
        "IsEnabled": true,
        "Priority": 70
      }
    ],
    "DefaultTimeoutSeconds": 60,
    "MaxRetryAttempts": 3
  }
}
```

## Source Properties

### Required Properties

- **Name**: Unique identifier for the source
- **Url**: NuGet API endpoint URL (typically ends with `/v3/index.json` or `/v3-flatcontainer/`)
- **IsEnabled**: Whether this source should be used
- **Priority**: Higher numbers have higher priority (sources are tried in order of priority)

### Authentication Properties

- **Username** + **Password**: For basic authentication
- **ApiKey**: For API key authentication

### Optional Properties

- **DefaultTimeoutSeconds**: Override default timeout for HTTP requests
- **MaxRetryAttempts**: Override default retry attempts

## Authentication Methods

### 1. Basic Authentication (Username/Password)

Used for Azure DevOps, TeamCity, and other feeds that support basic auth:

```json
{
  "Name": "azure-devops",
  "Url": "https://pkgs.dev.azure.com/organization/_packaging/feed/nuget/v3/index.json",
  "Username": "your-email@company.com",
  "Password": "your-personal-access-token",
  "IsEnabled": true,
  "Priority": 80
}
```

### 2. API Key Authentication

Used for GitHub Packages, MyGet, and other services:

```json
{
  "Name": "github-packages",
  "Url": "https://nuget.pkg.github.com/organization/index.json",
  "ApiKey": "ghp_your-personal-access-token",
  "IsEnabled": true,
  "Priority": 70
}
```

## Common Feed Examples

### Azure DevOps

```json
{
  "Name": "azure-devops",
  "Url": "https://pkgs.dev.azure.com/{organization}/_packaging/{feed}/nuget/v3/index.json",
  "Username": "{username}",
  "Password": "{personal-access-token}",
  "IsEnabled": true,
  "Priority": 80
}
```

### GitHub Packages

```json
{
  "Name": "github-packages",
  "Url": "https://nuget.pkg.github.com/{organization}/index.json",
  "ApiKey": "ghp_{token}",
  "IsEnabled": true,
  "Priority": 70
}
```

### MyGet

```json
{
  "Name": "myget",
  "Url": "https://www.myget.org/F/{feed}/api/v3/index.json",
  "ApiKey": "{api-key}",
  "IsEnabled": true,
  "Priority": 60
}
```

### JetBrains Space

```json
{
  "Name": "jetbrains-space",
  "Url": "https://{instance}.jetbrains.space/p/{project}/packages/nuget/v3/index.json",
  "Username": "{username}",
  "Password": "{password}",
  "IsEnabled": true,
  "Priority": 50
}
```

## Environment-Specific Configuration

You can use different configuration files for different environments:

- `appsettings.json` - Base configuration
- `appsettings.Development.json` - Development overrides
- `appsettings.Production.json` - Production overrides

## Security Considerations

1. **Never commit credentials to source control**
2. **Use environment variables for sensitive data** (configure through environment-specific files)
3. **Use personal access tokens instead of passwords** when available
4. **Limit token permissions** to only what's needed for NuGet access
5. **Regularly rotate authentication tokens**

## Environment Variables

You can also configure sources using environment variables:

```bash
NuGet__Sources__0__Name=private-feed
NuGet__Sources__0__Url=https://your-feed.com/v3/index.json
NuGet__Sources__0__ApiKey=your-api-key
NuGet__Sources__0__IsEnabled=true
NuGet__Sources__0__Priority=90
```

## Troubleshooting

### Common Issues

1. **401 Unauthorized**: Check credentials and ensure tokens have proper permissions
2. **404 Not Found**: Verify the feed URL is correct
3. **Timeout**: Increase `DefaultTimeoutSeconds` for slow networks
4. **SSL Issues**: Ensure certificates are valid and trusted

### Debugging

Enable detailed logging by setting the log level in your configuration:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "NuGetMcpServer.Services.NuGetHttpClientService": "Debug",
      "NuGetMcpServer.Services.NuGetPackageService": "Debug"
    }
  }
}
```

This will show detailed information about which sources are being tried and any authentication issues.
