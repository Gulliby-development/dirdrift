namespace Dirdrift.Core;

/// <summary>
/// A source of file paths and readable streams for a directory tree.
/// </summary>
public interface IFileSource
{
    /// <summary>
    /// Lists every regular file below the source root using slash-separated relative paths.
    /// </summary>
    IEnumerable<string> ListFiles();

    /// <summary>
    /// Opens a listed relative path for reading.
    /// </summary>
    Stream Open(string path);
}

/// <summary>
/// Identifies a failure while listing a source directory and the relative path
/// at which the failure occurred.
/// </summary>
public sealed class FileSourceException : IOException
{
    public FileSourceException(string path, Exception innerException)
        : base("Unable to list source files.", innerException)
    {
        Path = path;
    }

    public string Path { get; }
}

/// <summary>
/// Reads regular files from a directory tree without following reparse points.
/// </summary>
public sealed class DiskFileSource : IFileSource
{
    private readonly string _rootDirectory;

    public DiskFileSource(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = System.IO.Path.GetFullPath(rootDirectory);
    }

    public IEnumerable<string> ListFiles()
    {
        var files = new List<string>();
        var pendingDirectories = new Stack<(string FullPath, string RelativePath)>();

        FileAttributes rootAttributes;
        try
        {
            rootAttributes = File.GetAttributes(_rootDirectory);
        }
        catch (Exception exception) when (IsFileSystemException(exception))
        {
            throw new FileSourceException(string.Empty, exception);
        }

        if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
        {
            return files;
        }

        if ((rootAttributes & FileAttributes.Directory) == 0)
        {
            throw new FileSourceException(string.Empty, new IOException("The source root is not a directory."));
        }

        pendingDirectories.Push((_rootDirectory, string.Empty));

        while (pendingDirectories.Count > 0)
        {
            var (directoryPath, relativeDirectoryPath) = pendingDirectories.Pop();
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(directoryPath);
            }
            catch (Exception exception) when (IsFileSystemException(exception))
            {
                throw new FileSourceException(relativeDirectoryPath, exception);
            }

            Array.Sort(entries, StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                var relativePath = ToRelativePath(entry);
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (IsFileSystemException(exception))
                {
                    throw new FileSourceException(relativePath, exception);
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pendingDirectories.Push((entry, relativePath));
                }
                else
                {
                    files.Add(relativePath);
                }
            }
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    public Stream Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var relativePath = path.Replace('/', System.IO.Path.DirectorySeparatorChar);
        var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(_rootDirectory, relativePath));
        var pathFromRoot = System.IO.Path.GetRelativePath(_rootDirectory, fullPath);
        if (System.IO.Path.IsPathRooted(pathFromRoot)
            || pathFromRoot == ".."
            || pathFromRoot.StartsWith($"..{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new ArgumentException("Path must be relative to the source root.", nameof(path));
        }

        return new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
    }

    private string ToRelativePath(string fullPath)
    {
        var relativePath = System.IO.Path.GetRelativePath(_rootDirectory, fullPath);
        return relativePath
            .Replace(System.IO.Path.DirectorySeparatorChar, '/')
            .Replace(System.IO.Path.AltDirectorySeparatorChar, '/');
    }

    private static bool IsFileSystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
