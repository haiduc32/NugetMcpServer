namespace NuGetMcpServer.Common;

public static class PromptConstants
{
    public const string PackageSearchPrompt1 = @"**Task:**
Generate up to 5 keywords for task described in the UserRequest.

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

    public const string PackageSearchPrompt = @"**Task:**
Generate up to {0} noun keywords for the UserRequest topic.

**Keyword Rules:**
- Keyword should be short, ideally one word
- If keyword need clarification - keep both, keyword and keyword plus clarification

**Restrictions:**
- Avoid extremely wide keywords like ""Design"", ""Pattern"", ""Algorithm"", ""Generator"". As this keywords are useless without clarification.

**Output format:**
Only keywords, one per line, no extra text, no numbering, no explanations.

**UserRequest:**
'{1}'";

}
