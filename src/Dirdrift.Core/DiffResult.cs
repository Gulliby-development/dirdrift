namespace Dirdrift.Core;

/// <summary>
/// Describes a file moved from one relative path to another.
/// </summary>
/// <param name="From">The path in the original snapshot.</param>
/// <param name="To">The path in the current scan.</param>
public sealed record FileRename(string From, string To);

/// <summary>
/// Contains the deterministically ordered differences between two file collections.
/// </summary>
/// <param name="Renamed">Renames ordered by their original path.</param>
/// <param name="Modified">Modified paths ordered ordinally.</param>
/// <param name="Added">Added paths ordered ordinally.</param>
/// <param name="Removed">Removed paths ordered ordinally.</param>
public sealed record DiffResult(
    IReadOnlyList<FileRename> Renamed,
    IReadOnlyList<string> Modified,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed)
{
    /// <summary>
    /// Gets the number of changes, counting each rename as one change.
    /// </summary>
    public int ChangeCount => Renamed.Count + Modified.Count + Added.Count + Removed.Count;
}
