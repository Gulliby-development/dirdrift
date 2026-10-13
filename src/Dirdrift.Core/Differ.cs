namespace Dirdrift.Core;

/// <summary>
/// Compares file collections using ordinal, case-sensitive relative paths.
/// </summary>
public static class Differ
{
    /// <summary>
    /// Compares an original snapshot with a current file collection.
    /// </summary>
    /// <param name="originalFiles">Files recorded in the original snapshot.</param>
    /// <param name="currentFiles">Files found by the current scan.</param>
    /// <returns>A stable, ordinally ordered description of the differences.</returns>
    public static DiffResult Compare(IEnumerable<FileEntry> originalFiles, IEnumerable<FileEntry> currentFiles)
    {
        ArgumentNullException.ThrowIfNull(originalFiles);
        ArgumentNullException.ThrowIfNull(currentFiles);

        Dictionary<string, FileEntry> originalByPath = CreatePathLookup(originalFiles);
        Dictionary<string, FileEntry> currentByPath = CreatePathLookup(currentFiles);
        var modified = new List<string>();
        var removedCandidates = new List<FileEntry>();
        var addedCandidates = new List<FileEntry>();

        foreach (KeyValuePair<string, FileEntry> original in originalByPath.OrderBy(
            static pair => pair.Key,
            StringComparer.Ordinal))
        {
            if (currentByPath.TryGetValue(original.Key, out FileEntry? current))
            {
                if (!string.Equals(original.Value.Sha256, current.Sha256, StringComparison.Ordinal))
                {
                    modified.Add(original.Key);
                }
            }
            else
            {
                removedCandidates.Add(original.Value);
            }
        }

        foreach (KeyValuePair<string, FileEntry> current in currentByPath.OrderBy(
            static pair => pair.Key,
            StringComparer.Ordinal))
        {
            if (!originalByPath.ContainsKey(current.Key))
            {
                addedCandidates.Add(current.Value);
            }
        }

        List<FileRename> renamed = PairRenames(removedCandidates, addedCandidates);
        HashSet<string> renamedFrom = renamed.Select(static rename => rename.From).ToHashSet(StringComparer.Ordinal);
        HashSet<string> renamedTo = renamed.Select(static rename => rename.To).ToHashSet(StringComparer.Ordinal);
        string[] removed = removedCandidates
            .Where(file => !renamedFrom.Contains(file.Path))
            .Select(static file => file.Path)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        string[] added = addedCandidates
            .Where(file => !renamedTo.Contains(file.Path))
            .Select(static file => file.Path)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        renamed.Sort(static (left, right) =>
        {
            int fromComparison = string.CompareOrdinal(left.From, right.From);
            return fromComparison != 0 ? fromComparison : string.CompareOrdinal(left.To, right.To);
        });

        return new DiffResult(
            renamed.AsReadOnly(),
            modified.OrderBy(static path => path, StringComparer.Ordinal).ToArray(),
            added,
            removed);
    }

    private static Dictionary<string, FileEntry> CreatePathLookup(IEnumerable<FileEntry> files)
    {
        var result = new Dictionary<string, FileEntry>(StringComparer.Ordinal);
        foreach (FileEntry file in files)
        {
            ArgumentNullException.ThrowIfNull(file);
            if (!result.TryAdd(file.Path, file))
            {
                throw new ArgumentException($"The file path '{file.Path}' occurs more than once.", nameof(files));
            }
        }

        return result;
    }

    private static List<FileRename> PairRenames(
        IReadOnlyCollection<FileEntry> removed,
        IReadOnlyCollection<FileEntry> added)
    {
        Dictionary<string, FileEntry[]> removedByHash = removed
            .Where(static file => file.Size > 0)
            .GroupBy(static file => file.Sha256, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        Dictionary<string, FileEntry[]> addedByHash = added
            .Where(static file => file.Size > 0)
            .GroupBy(static file => file.Sha256, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var result = new List<FileRename>();

        foreach (KeyValuePair<string, FileEntry[]> removedGroup in removedByHash)
        {
            if (!addedByHash.TryGetValue(removedGroup.Key, out FileEntry[]? addedGroup))
            {
                continue;
            }

            int pairCount = Math.Min(removedGroup.Value.Length, addedGroup.Length);
            for (int index = 0; index < pairCount; index++)
            {
                result.Add(new FileRename(removedGroup.Value[index].Path, addedGroup[index].Path));
            }
        }

        return result;
    }
}
