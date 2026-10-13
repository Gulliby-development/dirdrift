using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Dirdrift.Core;

/// <summary>
/// Reports the relative path that could not be read while scanning a source.
/// </summary>
public sealed class ScanException : IOException
{
    public ScanException(string path, Exception innerException)
        : base("Unable to read a source path.", innerException)
    {
        Path = path;
    }

    public string Path { get; }
}

/// <summary>
/// Resolves ignore rules and hashes files from a source with bounded concurrency.
/// </summary>
public sealed class Scanner
{
    private const string IgnoreFileName = ".dirdriftignore";

    /// <summary>
    /// Gets the CLI and root ignore-file patterns resolved by the most recent scan.
    /// </summary>
    public IReadOnlyList<string> EffectiveIgnorePatterns { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Lists the source before filtering, then hashes each unignored file using at
    /// most <paramref name="jobs"/> concurrent workers.
    /// </summary>
    public async Task<IReadOnlyList<FileEntry>> ScanAsync(
        IFileSource source,
        IReadOnlyList<string> ignorePatterns,
        int jobs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ignorePatterns);
        ArgumentOutOfRangeException.ThrowIfLessThan(jobs, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(jobs, 32);

        string[] listedFiles;
        try
        {
            listedFiles = source.ListFiles().ToArray();
        }
        catch (FileSourceException exception)
        {
            throw new ScanException(exception.Path, exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ScanException(string.Empty, exception);
        }

        Array.Sort(listedFiles, StringComparer.Ordinal);
        var failures = new ConcurrentBag<(string Path, Exception Exception)>();
        var effectivePatterns = ignorePatterns.ToList();

        if (Array.IndexOf(listedFiles, IgnoreFileName) >= 0)
        {
            try
            {
                effectivePatterns.AddRange(await ReadIgnorePatternsAsync(source, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add((IgnoreFileName, exception));
            }
        }

        EffectiveIgnorePatterns = Array.AsReadOnly(effectivePatterns.ToArray());
        var filesToHash = listedFiles
            .Where(path => !Glob.IsIgnored(path, EffectiveIgnorePatterns))
            .ToArray();
        var entries = new ConcurrentBag<FileEntry>();

        await Parallel.ForEachAsync(
            filesToHash,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = jobs,
                CancellationToken = cancellationToken
            },
            async (path, token) =>
            {
                try
                {
                    entries.Add(await HashFileAsync(source, path, token).ConfigureAwait(false));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures.Add((path, exception));
                }
            }).ConfigureAwait(false);

        if (!failures.IsEmpty)
        {
            var firstFailure = failures
                .OrderBy(failure => failure.Path, StringComparer.Ordinal)
                .First();
            throw new ScanException(firstFailure.Path, firstFailure.Exception);
        }

        return entries
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<IReadOnlyList<string>> ReadIgnorePatternsAsync(
        IFileSource source,
        CancellationToken cancellationToken)
    {
        await using var stream = source.Open(IgnoreFileName);
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true);

        var patterns = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            var pattern = line.Trim(' ', '\t', '\r');
            if (pattern.Length == 0 || pattern.StartsWith('#'))
            {
                continue;
            }

            if (pattern.Trim('/') is { Length: 0 })
            {
                continue;
            }

            patterns.Add(pattern);
        }

        return patterns;
    }

    private static async Task<FileEntry> HashFileAsync(
        IFileSource source,
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = source.Open(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long size = 0;

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, bytesRead);
            size = checked(size + bytesRead);
        }

        var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        return new FileEntry(path, size, digest);
    }
}
