using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

namespace NuGetMcpServer.Extensions;

/// <summary>
/// Extension methods for exception handling
/// </summary>
public static class ExceptionHandlingExtensions
{
    /// <summary>
    /// Executes an async action with exception handling and logging
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="action">The async function to execute</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="errorMessage">Error message to log</param>
    /// <param name="rethrow">Whether to rethrow the exception</param>
    /// <returns>Result of the action</returns>
    public static async Task<T> ExecuteWithLoggingAsync<T>(Func<Task<T>> action, ILogger logger, string errorMessage, bool rethrow = true)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, errorMessage);
            if (rethrow)
            {
                throw;
            }

            return default!;
        }
    }

    /// <summary>
    /// Handles exceptions from multi-source operations and determines the appropriate exception to throw
    /// </summary>
    /// <typeparam name="T">Return type</typeparam>
    /// <param name="exceptions">List of exceptions encountered</param>
    /// <param name="packageId">Package ID for error context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Default value or throws appropriate exception</returns>
    public static T HandleMultiSourceExceptions<T>(this List<Exception> exceptions, string packageId, ILogger logger)
    {
        if (!exceptions.Any())
        {
            throw new InvalidOperationException($"No sources available for package {packageId}");
        }

        // If all exceptions are HttpRequestExceptions, throw the first one to preserve the original behavior
        if (exceptions.All(ex => ex is HttpRequestException))
        {
            throw exceptions.First();
        }
        
        throw new InvalidOperationException($"Failed to process package {packageId} from all configured sources");
    }
}
