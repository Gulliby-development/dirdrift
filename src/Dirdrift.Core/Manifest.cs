namespace Dirdrift.Core;

/// <summary>
/// A versioned snapshot of files beneath a directory.
/// </summary>
/// <param name="Version">The manifest format version.</param>
/// <param name="Files">The files recorded in the snapshot.</param>
public sealed record Manifest(int Version, IReadOnlyList<FileEntry> Files)
{
    /// <summary>
    /// Gets the current manifest format version.
    /// </summary>
    public const int CurrentVersion = 1;
}

/// <summary>
/// Reports a failure to read or validate a manifest.
/// </summary>
public sealed class ManifestException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ManifestException"/> class.
    /// </summary>
    /// <param name="reason">The stable manifest failure reason.</param>
    public ManifestException(string reason)
        : base(reason)
    {
        Reason = reason;
    }

    /// <summary>
    /// Gets the stable reason to include in a user-facing error.
    /// </summary>
    public string Reason { get; }
}
