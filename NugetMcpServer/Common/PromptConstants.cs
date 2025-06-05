namespace NuGetMcpServer.Common;

public static class PromptConstants
{
    public const string PackageSearchPrompt = @"
Given the request: '{originalQuery}', list exactly {packageCount} likely NuGet package names that would satisfy it.
Use typical .NET naming patterns (suffixes such as Generator, Builder, Client, Service, Helper, Manager, Processor, Handler, Framework).
Return exactly {packageCount} lines, one package name per line, no extra text.";

    public const string AlternativePackageSearchPrompt = @"
Given the request: '{originalQuery}', list exactly {packageCount} likely NuGet package names that would satisfy it.
Use typical .NET naming patterns (suffixes such as Generator, Builder, Client, Service, Helper, Manager, Processor, Handler, Framework).
Return exactly {packageCount} lines, one package name per line, no extra text.";
}
