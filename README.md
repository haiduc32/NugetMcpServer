# NugetMcpServer

A powerful MCP server for getting accurate interface and enum definitions from NuGet packages. It helps reduce LLM hallucinations by giving precise information about real package APIs.
Certified by [MCPHub](https://mcphub.com/mcp-servers/dimonsmart/nugetmcpserver)

## Overview


This application is not just a demo. It is a useful MCP server that reduces LLM hallucinations by giving accurate, up-to-date information about methods and types in specific NuGet package versions. Instead of using old training data, language models can ask for real package metadata and interface definitions directly from NuGet.

The server uses the Model Context Protocol (MCP). This makes it easy to connect with AI assistants and development tools. It gives real-time access to package information, helping developers and AI systems make better decisions about API usage.

You can use this server with [OllamaChat](https://github.com/DimonSmart/OllamaChat), VS Code with Copilot, and other MCP-compatible clients.

## Features

- Real-time extraction of interfaces and enums from NuGet packages
- **Smart package search with AI-enhanced keyword generation**
- **Two-phase search: direct search + AI fallback for better results**
- **Comma-separated keyword search for fast targeted results with balanced distribution**
- **Package popularity ranking by download count**
- **Dual transport modes: STDIO (default) and HTTP for flexible integration**
- **Azure DevOps feeds support with native package filtering**
- **Private NuGet feeds support with authentication (username/password or API key)**
- **Custom NuGet sources configuration with priority handling**
- Reduces LLM hallucinations by giving accurate API information
- Supports specific package versions or latest version
- Supports generic types with correct C# formatting
- Includes a time utility tool for basic server checks
- Built with .NET 9.0 for good performance
- Easy to integrate with VS Code, OllamaChat, and other MCP clients

## Prerequisites

- .NET 9.0 SDK or higher
- A compatible MCP client for testing (for example, [OllamaChat](https://github.com/DimonSmart/OllamaChat))

## Installation

### Option 1: Install via WinGet (Recommended)


You can install NugetMcpServer using WinGet:

```
winget install DimonSmart.NugetMcpServer
```


After installation, you can add the server to VS Code and other MCP-compatible clients. For VS Code:

1. Install the server using the command above.
2. Open VS Code settings and search for "MCP".
3. Add NugetMcpServer to your MCP servers configuration.
4. The server executable (`NugetMcpServer.exe`) will be in your system PATH.

### Option 2: Build from Source

1. Clone this repository.
2. Build the application:
   ```
   dotnet build
   ```
3. Run the server:
   ```
   dotnet run
   ```

## Usage

NugetMcpServer supports both **STDIO** (standard input/output) and **HTTP** transport modes.

### STDIO Mode (Default)

The default mode uses standard input/output for communication with MCP clients:

```bash
# Run in STDIO mode (default)
dotnet run

# Or explicitly specify STDIO mode
dotnet run -- --stdio
```

This mode is ideal for:
- VS Code with Copilot integration
- Direct integration with MCP-compatible tools
- Local development and testing

### HTTP Mode

You can also run the server in HTTP mode for web-based integrations:

```bash
# Run in HTTP mode on default port 5000
dotnet run -- --http

# Run in HTTP mode on custom port
dotnet run -- --http --port 8080
```

**Command Line Options:**
- `--http` or `-h`: Enable HTTP transport mode
- `--port <port>` or `-p <port>`: Specify custom port (default: 5000)
- `--version` or `-v`: Show version information

HTTP mode is useful for:
- Web-based MCP clients
- Integration with web applications
- Remote access scenarios
- Docker deployments

**Example HTTP URLs:**
- Default: `http://localhost:5000`
- Custom port: `http://localhost:8080`

The HTTP server implements the MCP protocol over HTTP and provides the same functionality as the STDIO mode.

## Configuration

### Custom NuGet Sources

NugetMcpServer supports connecting to custom NuGet feeds including private feeds that require authentication. This is configured through the `appsettings.json` file.

#### Basic Configuration

Create or modify `appsettings.json` in the same directory as the executable:

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

#### Advanced Configuration with Private Feeds

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

**Authentication Methods:**
- **Basic Authentication**: Use `Username` + `Password` for Azure DevOps, TeamCity, etc.
- **API Key**: Use `ApiKey` for GitHub Packages, MyGet, etc.

**Source Properties:**
- **Priority**: Higher numbers have higher priority (sources are tried in order)
- **IsEnabled**: Whether this source should be used
- **DefaultTimeoutSeconds**: Custom timeout for this source
- **MaxRetryAttempts**: Custom retry attempts

For detailed configuration examples and troubleshooting, see [NUGET_SOURCES.md](NUGET_SOURCES.md).

## Project Structure

- `Program.cs` - Main entry point that configures and runs the MCP server
- `TimeTool.cs` - Utility tool for time-related functions (namespace `NugetMcpServer`)
- `Tools/GetInterfaceDefinitionTool.cs` - Extracts interface definitions from NuGet packages
- `Tools/ListInterfacesTool.cs` - Lists all interfaces in NuGet packages
- `Tools/GetEnumDefinitionTool.cs` - Extracts enum definitions from NuGet packages
- `Services/NuGetPackageService.cs` - Works with NuGet packages and manages package sources
- `Services/NuGetHttpClientService.cs` - Manages HTTP clients with authentication for multiple NuGet sources
- `Services/InterfaceFormattingService.cs` - Formats interface definitions
- `Services/EnumFormattingService.cs` - Formats enum definitions
- `Services/InterfaceInfo.cs` - Model for interface information
- `Services/InterfaceListResult.cs` - Response model for interface listing
- `Models/NuGetConfiguration.cs` - Configuration model for NuGet sources
- `Models/NuGetSourceConfiguration.cs` - Configuration model for individual NuGet sources
- `Common/McpToolBase.cs` - Base class for MCP tools
- `Extensions/` - Extension methods for string formatting and exception handling
- `appsettings.json` - Configuration file for NuGet sources and application settings
- `NUGET_SOURCES.md` - Detailed documentation for configuring custom NuGet sources

## Implementation Details


The server uses the .NET Generic Host and includes:

- Console logging set to trace level
- MCP server registered with STDIO transport
- Automatic discovery and registration of tools
- Multi-source NuGet support with authentication
- NuGetHttpClientService for managing authenticated HTTP clients
- NuGetPackageService for package operations across multiple sources
- InterfaceFormattingService and EnumFormattingService for code formatting
- Configuration-based source management with priority handling

## Available Tools


### TimeTool

- `get_current_time()` - Returns the current server time in ISO 8601 format (YYYY-MM-DDThh:mm:ssZ)


### Interface Tools

- `get_interface_definition(packageId, interfaceName?, version?)` - Gets the C# interface definition from a NuGet package. Parameters: packageId (NuGet package ID), interfaceName (optional, short name without namespace), version (optional, defaults to latest)
- `list_interfaces(packageId, version?)` - Lists all public interfaces in a NuGet package. Returns package ID, version, and the list of interfaces


### Enum Tools

- `get_enum_definition(packageId, enumName, version?)` - Gets the C# enum definition from a NuGet package. Parameters: packageId (NuGet package ID), enumName (short name without namespace), version (optional, defaults to latest)

### Class Tools

- `get_class_definition(packageId, className, version?)` - Gets the C# class definition from a NuGet package. Parameters: packageId (NuGet package ID), className (short or full name), version (optional, defaults to latest)
- `list_classes(packageId, version?)` - Lists all public classes in a NuGet package. Returns package ID, version, and the list of classes

### Package Search Tools

- `search_packages(query, maxResults?, fuzzySearch?)` - Searches for NuGet packages by description or functionality.
  - **Standard search mode (fuzzySearch=false, default)**: Performs direct search for the full query and also searches each comma-separated keyword if provided
  - **Fuzzy search mode (fuzzySearch=true)**: Starts with the standard search and additionally tries each individual word and AI-generated package name alternatives
  - AI analyzes user's functional requirements and generates 3 most likely package names (e.g., "maze generation" → "MazeGenerator MazeBuilder MazeCreator")
  - Returns up to 50 most popular packages with details including download counts, descriptions, and project URLs
  - Results are sorted by popularity (download count) for better relevance

- `list_private_packages(searchTerm?, maxResults?, includeVersions?)` - Lists packages exclusively from private NuGet feeds and Azure DevOps feeds.
  - Only searches private/internal package repositories, not public feeds like nuget.org
  - Supports Azure DevOps feeds with native package filtering (filters out proxied packages by default)
  - Supports private NuGet feeds with authentication (username/password or API key)
  - Optional search term to filter packages by name
  - Optional version inclusion for detailed package information
  - Returns package details including source type, latest version, and available versions if requested
  - Maximum results limit (1-200, default 50)

### Package Information Tools

- `get_package_info(packageId, version?)` - Gets comprehensive information about a NuGet package including metadata, dependencies, and meta-package status. Shows clear warnings for meta-packages and guidance on where to find actual implementations.

### Package Dependencies

- `get_package_dependencies(packageId, version?)` - Gets the dependencies of a NuGet package to help understand what other packages contain the actual implementations

## MCP Server Response Examples

Here are examples of server responses from the MCP tools using DimonSmart.MazeGenerator as a sample package:

### GetInterfaceDefinition Example


#### User Query Example
A user might ask:

> "I want to generate mazes in my .NET application. Is there a package for that? Can you show me the interface for DimonSmart.MazeGenerator so I can see how to use it?"

The agent would use the MCP server to get the interface definition:

Request:
```json
{
  "name": "f1e_GetInterfaceDefinition",
  "parameters": {
    "packageId": "DimonSmart.MazeGenerator",
    "interfaceName": "IMazeGenerator",
    "version": ""
  }
}
```

Response (actual MCP server output):
```json
{
  "result": "namespace DimonSmart.MazeGenerator\n{\n    /// <summary>\n    /// Interface for maze generation algorithms\n    /// </summary>\n    public interface IMazeGenerator\n    {\n        /// <summary>\n        /// Creates a new maze with the specified dimensions\n        /// </summary>\n        /// <param name=\"width\">Width of the maze</param>\n        /// <param name=\"height\">Height of the maze</param>\n        /// <returns>A 2D array representing the maze</returns>\n        bool[,] Generate(int width, int height);\n        \n        /// <summary>\n        /// Gets the name of the algorithm used for maze generation\n        /// </summary>\n        string AlgorithmName { get; }\n    }\n}"
}
```


The result is the actual JSON response from the MCP server:
- The interface definition is a single string value
- All newlines are escaped as `\n`
- All double quotes are escaped as `\"`
- The content is inside the `result` property as a JSON string

This is how an agent receives the interface definition from the MCP server. The agent then parses and displays it in a readable format for the user.

### SearchPackages Example

#### User Query Example
A user might ask:

> "I need a library for working with JSON in my .NET application. Can you help me find the most popular packages for JSON handling?"

The agent would use the MCP server to search for packages:

Request:
```json
{
  "name": "SearchPackages",
  "parameters": {
    "query": "JSON serialization library",
    "maxResults": 5,
    "fuzzySearch": false
  }
}
```

#### Fuzzy Search Example

For better results with descriptive queries, enable fuzzy search:

Request:
```json
{
  "name": "SearchPackages",
  "parameters": {
    "query": "I need a library for generating mazes",
    "maxResults": 10,
    "fuzzySearch": true
  }
}
```

#### Russian Language Support Example

The AI can work with queries in Russian as well:

Request:
```json
{
  "name": "SearchPackages",
  "parameters": {
    "query": "Пользователю надо генерировать лабиринт",
    "maxResults": 10,
    "fuzzySearch": true
  }
}
```

Response (formatted result shows combined results):
```
/* NUGET PACKAGE SEARCH RESULTS FOR: Пользователю надо генерировать лабиринт */
/* AI-GENERATED PACKAGE NAMES: MazeGenerator MazeBuilder MazeCreator */
/* FOUND 8 PACKAGES (SHOWING TOP 8) */

## DimonSmart.MazeGenerator v1.0.0
**Downloads**: 12,543
**Description**: Advanced maze generation library with multiple algorithms
**Project URL**: https://github.com/DimonSmart/MazeGenerator

## MazeBuilder v2.1.0
**Downloads**: 8,234
**Description**: Simple maze construction toolkit
```

### ListPrivatePackages Example

#### User Query Example
A user might ask:

> "I need to see what private packages are available in our company's Azure DevOps feed. Can you list the internal packages we have?"

The agent would use the MCP server to list private packages:

Request:
```json
{
  "name": "list_private_packages",
  "parameters": {
    "searchTerm": "",
    "maxResults": 20,
    "includeVersions": false
  }
}
```

Response (showing private packages from Azure DevOps and private feeds):
```json
{
  "result": {
    "searchTerm": "",
    "totalPackages": 5,
    "sources": [
      {
        "sourceName": "CompanyAzureDevOps",
        "sourceType": "Azure DevOps",
        "packageCount": 3,
        "isAzureDevOps": true
      },
      {
        "sourceName": "PrivateNuGet",
        "sourceType": "Private NuGet Feed (API Key)",
        "packageCount": 2,
        "isAzureDevOps": false
      }
    ],
    "packages": [
      {
        "packageId": "Company.Core.Authentication",
        "sourceName": "CompanyAzureDevOps",
        "sourceType": "Azure DevOps",
        "latestVersion": "2.1.0",
        "isAzureDevOps": true
      },
      {
        "packageId": "Company.Utilities.Logging",
        "sourceName": "PrivateNuGet",
        "sourceType": "Private NuGet Feed (API Key)",
        "latestVersion": "1.5.2",
        "isAzureDevOps": false
      }
    ]
  }
}
```

This tool helps developers discover internal packages and distinguish between public and private package sources.

#### How Fuzzy Search Works

The search algorithm works as follows:
- **Standard search mode (fuzzySearch=false)**: Direct search for the whole query plus searches for comma-separated keywords
- **Fuzzy search mode (fuzzySearch=true)**:
  1. Runs the standard search
  2. Searches each individual word from the query
  3. Uses AI to suggest additional package names (e.g., "maze generation" → "MazeGenerator MazeBuilder MazeCreator")
  4. Searches for the AI-generated names
  5. Combines and deduplicates all results sorted by popularity

## Technical Details


The application uses the ModelContextProtocol library (version 0.2.0-preview.1), which helps create MCP-compatible servers. The server uses standard input/output to talk to clients.

Project namespaces:
- `NugetMcpServer` - Main namespace (used in TimeTool.cs)
- `NuGetMcpServer.Tools` - Tool components (interface and enum tools)
- `NuGetMcpServer.Services` - Service components (package and formatting services)
- `NuGetMcpServer.Common` - Shared components
- `NuGetMcpServer.Extensions` - Extension methods

## Integration with Development Tools


You can use this MCP server with different development tools and AI assistants:

- **VS Code**: Integrate through MCP server configuration
- **OllamaChat**: Works with [OllamaChat](https://github.com/DimonSmart/OllamaChat) for local AI conversations
- **GitHub Copilot**: Use as an MCP server to get accurate package information
- **Other MCP Clients**: Any tool that supports the Model Context Protocol

By using this server, developers and AI systems get real-time, accurate information about NuGet package interfaces and enums. This reduces the chance of outdated or wrong API suggestions.

## Benefits for AI Development

### Reducing LLM Hallucinations


This MCP server helps solve a big problem in AI-assisted development: **LLM hallucinations about package APIs**. Common issues are:

- **Outdated Information**: LLMs trained on old data may suggest removed methods or interfaces
- **Non-existent APIs**: Models may generate method signatures that do not exist
- **Version Mismatches**: Suggestions may be for a different package version than the one used
- **Incomplete Information**: Missing important parameters, return types, or constraints


### How NugetMcpServer Helps

- **Real-time Data**: Gets current interface definitions directly from NuGet packages
- **Version-specific Information**: Lets you query specific package versions for accurate compatibility
- **Complete Interface Definitions**: Gives full method signatures, properties, and generic constraints
- **Accurate Type Information**: Ensures correct parameter types, return types, and namespace information


### Use Cases

- **Package Discovery**: Search for packages by functionality description and see popularity metrics
- **API Discovery**: See what interfaces are in a package before using it
- **Version Migration**: Compare interfaces between package versions when upgrading
- **Code Generation**: Generate accurate code from real package interfaces
- **Documentation**: Get up-to-date interface documentation for development
- **Technology Research**: Find the most popular packages for specific technologies or patterns

## About the MCP Protocol


Model Context Protocol (MCP) is a protocol that standardizes communication between language models and external tools. It lets models call functions, get information, and interact with external systems through one interface.

## License

Unlicense - This is free and unencumbered software released into the public domain.

### Meta-Package Support

All tools that analyze package content (class definitions, interface definitions, enum definitions, and listing tools) now automatically detect meta-packages and show clear warnings. Meta-packages are NuGet packages that group other packages together without containing actual implementation code. When a meta-package is detected, the tools will:

- Display a prominent warning that it's a meta-package
- List the dependencies that contain the actual implementations
- Provide guidance on which packages to analyze instead


