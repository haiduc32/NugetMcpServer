using System;

namespace NuGetMcpServer.Extensions;

/// <summary>
/// Extension methods for string operations
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Checks if the string is null, empty or equals to "null" (case insensitive)
    /// </summary>
    /// <param name="value">String to check</param>
    /// <returns>True if string is null, empty or equals to "null" (case insensitive)</returns>
    public static bool IsNullOrEmptyOrNullString(this string? value) => string.IsNullOrEmpty(value) || value.EqualsIgnoreCase("null");

    /// <summary>
    /// Compares two strings ignoring case
    /// </summary>
    /// <param name="value">String to compare</param>
    /// <param name="compareTo">String to compare with</param>
    /// <returns>True if strings are equal ignoring case</returns>
    public static bool EqualsIgnoreCase(this string? value, string compareTo) => value?.Equals(compareTo, StringComparison.OrdinalIgnoreCase) == true;
}
