# LocalMcpServer

A demonstration implementation of a server supporting the Model Context Protocol (MCP).

## Overview

This application demonstrates how to create a simple MCP server using the ModelContextProtocol library. The server provides tools that can be used by MCP clients, showcasing the interaction between client and server using STDIO transport.

This demo can be used in conjunction with the [OllamaChat](https://github.com/DimonSmart/OllamaChat) application, a local chat client for OLLAMA.

## Features

- Implementation of a basic MCP server
- Uses STDIO for client communication
- Includes a sample tool (`TimeTool`) that returns the current server time
- Built with .NET 9.0

## Prerequisites

- .NET 9.0 SDK or higher
- A compatible MCP client for testing (for example, [OllamaChat](https://github.com/DimonSmart/OllamaChat))

## Getting Started

1. Clone this repository
2. Build the application:
   ```
   dotnet build
   ```
3. Run the server:
   ```
   dotnet run
   ```

## Project Structure

- `Program.cs` - The main entry point that configures and runs the MCP server
- `TimeTool.cs` - A sample tool implementation that provides time-related functionality

## Implementation Details

The server is configured using the .NET Generic Host with the following components:

- Console logging configured to trace level
- Registration of the MCP server with STDIO transport
- Automatic discovery and registration of tools from the assembly

## Available Tools

### TimeTool

- `GetCurrentTime()` - Returns the current server time in ISO 8601 format (YYYY-MM-DDThh:mm:ssZ)

### InterfaceLookupService

- `GetInterfaceDefinition(packageId, interfaceName, version?)` - Extracts and returns the C# interface definition from a specified NuGet package
- `ListInterfaces(packageId, version?)` - Lists all public interfaces available in a specified NuGet package

## Technical Details

The application uses the ModelContextProtocol library (version 0.1.0-preview.9), which provides infrastructure for creating MCP-compatible servers. The server is configured to use standard input/output for communication with clients.

## Integration with OllamaChat

This demo server can be integrated with the [OllamaChat](https://github.com/DimonSmart/OllamaChat) application, which is a local chat client for OLLAMA. By connecting this MCP server to OllamaChat, you can demonstrate the capabilities of tool-using language models in a local environment.

## About the MCP Protocol

Model Context Protocol (MCP) is a protocol designed to standardize communication between language models and external tools. It allows models to call functions, retrieve information, and interact with external systems through a unified interface.

## License

See the LICENSE file for license information.
