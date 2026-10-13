namespace Dirdrift.Core;

/// <summary>
/// Describes a file's slash-separated relative path, byte length, and SHA-256 hash.
/// </summary>
/// <param name="Path">The file path relative to the scan root.</param>
/// <param name="Size">The number of bytes in the file.</param>
/// <param name="Sha256">The lowercase hexadecimal SHA-256 digest.</param>
public sealed record FileEntry(string Path, long Size, string Sha256);
