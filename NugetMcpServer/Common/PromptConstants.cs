namespace NuGetMcpServer.Common;

public static class PromptConstants
{
    public const string PackageSearchPrompt = @"**Task:**
Generate up to 5 keywords for NuGet package search string based on the UserRequest.

** Keyword Rules: **
Keywords must be short, 1 word  (noun) or (noun + space + verb) not preferable but possible.
You can use the UserRequest nouns close synonyms too.

** Restrictions: **
Do not generate very wide terms which could be far from initial Request (like Algorithm, Solver, Grid).
If wide, common word, is strictly needed - add qualifier word (Like Genetic Algorithm)

** Output format: **
Only keywords.
Each keyword on a new line.
No extra text.

**UserRequest:**
""Maze generation""";
}
