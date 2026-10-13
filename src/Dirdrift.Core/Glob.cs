namespace Dirdrift.Core;

/// <summary>
/// Matches ignore patterns against slash-separated relative paths.
/// </summary>
public static class Glob
{
    /// <summary>
    /// Determines whether a single path matches an ignore pattern. This does not
    /// check parent directories; use <see cref="IsIgnored"/> for ignore semantics.
    /// </summary>
    public static bool Matches(string pattern, string relativePath, bool isDirectory = false)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(relativePath);

        return MatchesPattern(Parse(pattern), SplitPath(relativePath), isDirectory);
    }

    /// <summary>
    /// Determines whether a path or one of its parent directories matches any
    /// of the supplied ignore patterns.
    /// </summary>
    public static bool IsIgnored(string relativePath, IEnumerable<string> patterns, bool isDirectory = false)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(patterns);

        var pathSegments = SplitPath(relativePath);
        if (pathSegments.Length == 0)
        {
            return false;
        }

        var parsedPatterns = patterns.Select(Parse).ToArray();
        for (var prefixLength = 1; prefixLength <= pathSegments.Length; prefixLength++)
        {
            var prefix = pathSegments[..prefixLength];
            var prefixIsDirectory = isDirectory || prefixLength < pathSegments.Length;

            foreach (var pattern in parsedPatterns)
            {
                if (MatchesPattern(pattern, prefix, prefixIsDirectory))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ParsedPattern Parse(string pattern)
    {
        var directoriesOnly = pattern.EndsWith("/", StringComparison.Ordinal);
        if (directoriesOnly)
        {
            pattern = pattern[..^1];
        }

        var anchored = pattern.StartsWith("/", StringComparison.Ordinal);
        if (anchored)
        {
            pattern = pattern[1..];
        }

        var segments = pattern.Length == 0 ? Array.Empty<string>() : pattern.Split('/');
        return new ParsedPattern(anchored, directoriesOnly, segments);
    }

    private static string[] SplitPath(string relativePath) =>
        relativePath.Length == 0 ? Array.Empty<string>() : relativePath.Split('/');

    private static bool MatchesPattern(ParsedPattern pattern, string[] pathSegments, bool isDirectory)
    {
        if (pattern.Segments.Length == 0 || (pattern.DirectoriesOnly && !isDirectory))
        {
            return false;
        }

        if (!pattern.Anchored && pattern.Segments.Length == 1)
        {
            foreach (var segment in pathSegments)
            {
                if (MatchesSegments(pattern.Segments, [segment]))
                {
                    return true;
                }
            }

            return false;
        }

        return MatchesSegments(pattern.Segments, pathSegments);
    }

    private static bool MatchesSegments(string[] patternSegments, string[] pathSegments)
    {
        var memo = new sbyte[patternSegments.Length + 1, pathSegments.Length + 1];
        for (var patternIndex = 0; patternIndex <= patternSegments.Length; patternIndex++)
        {
            for (var pathIndex = 0; pathIndex <= pathSegments.Length; pathIndex++)
            {
                memo[patternIndex, pathIndex] = -1;
            }
        }

        return Match(0, 0);

        bool Match(int patternIndex, int pathIndex)
        {
            var known = memo[patternIndex, pathIndex];
            if (known >= 0)
            {
                return known == 1;
            }

            bool result;
            if (patternIndex == patternSegments.Length)
            {
                result = pathIndex == pathSegments.Length;
            }
            else if (patternSegments[patternIndex] == "**")
            {
                if (patternIndex == patternSegments.Length - 1)
                {
                    result = pathIndex < pathSegments.Length;
                }
                else
                {
                    result = false;
                    for (var nextPathIndex = pathIndex; nextPathIndex <= pathSegments.Length; nextPathIndex++)
                    {
                        if (Match(patternIndex + 1, nextPathIndex))
                        {
                            result = true;
                            break;
                        }
                    }
                }
            }
            else
            {
                result = pathIndex < pathSegments.Length
                    && MatchesSegment(patternSegments[patternIndex], pathSegments[pathIndex])
                    && Match(patternIndex + 1, pathIndex + 1);
            }

            memo[patternIndex, pathIndex] = result ? (sbyte)1 : (sbyte)0;
            return result;
        }
    }

    private static bool MatchesSegment(string pattern, string value)
    {
        var previous = new bool[value.Length + 1];
        previous[0] = true;

        foreach (var patternCharacter in pattern)
        {
            var current = new bool[value.Length + 1];
            if (patternCharacter == '*')
            {
                current[0] = previous[0];
                for (var valueIndex = 1; valueIndex <= value.Length; valueIndex++)
                {
                    current[valueIndex] = previous[valueIndex] || current[valueIndex - 1];
                }
            }
            else
            {
                for (var valueIndex = 1; valueIndex <= value.Length; valueIndex++)
                {
                    current[valueIndex] = previous[valueIndex - 1]
                        && (patternCharacter == '?' || patternCharacter == value[valueIndex - 1]);
                }
            }

            previous = current;
        }

        return previous[value.Length];
    }

    private sealed record ParsedPattern(bool Anchored, bool DirectoriesOnly, string[] Segments);
}
