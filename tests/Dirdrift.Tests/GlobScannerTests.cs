using System.Security.Cryptography;
using System.Text;
using Dirdrift.Core;

namespace Dirdrift.Tests;

public class GlobScannerTests
{
    [Theory]
    [MemberData(nameof(IgnorePatternCases))]
    public void Ignore_patterns_match_the_documented_examples(
        string pattern,
        string path,
        bool isDirectory,
        bool expected)
    {
        Assert.Equal(expected, Glob.IsIgnored(path, [pattern], isDirectory));
    }

    public static IEnumerable<object[]> IgnorePatternCases =>
    [
        ["*.log", "a.log", false, true],
        ["*.log", "dir/b.log", false, true],
        ["*.log", "a.logx", false, false],
        ["*.log", "dir/a.log/c.txt", false, true],
        ["build/", "build/x.o", false, true],
        ["build/", "src/build/x.o", false, true],
        ["build/", "build", true, true],
        ["build/", "build", false, false],
        ["node_modules", "node_modules/a.js", false, true],
        ["node_modules", "src/node_modules/a/b.js", false, true],
        ["docs/*.md", "docs/a.md", false, true],
        ["docs/*.md", "docs/x/a.md", false, false],
        ["docs/*.md", "a/docs/a.md", false, false],
        ["/out", "out/a", false, true],
        ["/out", "out2/a", false, false],
        ["/out", "src/out/a", false, false],
        ["src/**/gen", "src/gen/a", false, true],
        ["src/**/gen", "src/a/gen/b", false, true],
        ["src/**/gen", "src/a/b/gen", true, true],
        ["src/**/gen", "src/x", false, false],
        ["**/gen", "gen/a", false, true],
        ["**/gen", "a/b/gen/c", false, true],
        ["**/gen", "genx/a", false, false],
        ["a/**", "a", true, false],
        ["a/**", "a/b", false, true],
        ["a/**", "a/b/c", false, true],
        ["a?c.txt", "abc.txt", false, true],
        ["a?c.txt", "ac.txt", false, false],
        ["a?c.txt", "abbc.txt", false, false],
        ["a?c.txt", "a/c.txt", false, false],
        ["README.md", "README.md", false, true],
        ["README.md", "readme.md", false, false],
        ["README.md", "sub/README.md", false, true],
        ["a**b", "axxb", false, true],
        ["a**b", "a/b", false, false],
        ["docs/old/", "docs/old/x", false, true],
        ["docs/old/", "docs/old", true, true],
        ["docs/old/", "docs/old", false, false],
        ["docs/old/", "docs/x/old/y", false, false],
        ["*", "folder/file.txt", false, true],
        ["file?.txt", "file1.txt", false, true],
        ["file?.txt", "file10.txt", false, false],
        ["dir/*", "dir", true, false],
        ["dir/*", "dir/child", true, true],
        ["/root/**/cache", "root/cache/a.tmp", false, true],
        ["**/cache/**", "a/b/cache/item", false, true]
    ];

    [Fact]
    public async Task Disk_source_and_scanner_list_nested_files_and_apply_both_ignore_sources()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            WriteFile(root, ".dirdriftignore", "# ignored build outputs\n\nbuild/\n*.log \n");
            WriteFile(root, "keep.txt", "keep");
            WriteFile(root, ".hidden", "dot file");
            WriteFile(root, "keep.log.txt", "not a .log file");
            WriteFile(root, "build/deep/compiled.bin", "ignored by root rules");
            WriteFile(root, "src/build/nested.bin", "ignored parent directory");
            WriteFile(root, "logs/a.log", "ignored by root rules");
            WriteFile(root, "cli-only/nested/secret.txt", "ignored by CLI rule");
            WriteFile(root, "src/visible.dat", "visible");
            Directory.CreateDirectory(Path.Combine(root, "empty"));

            var source = new DiskFileSource(root);
            var listedFiles = source.ListFiles().ToArray();

            Assert.Contains("build/deep/compiled.bin", listedFiles);
            Assert.Contains("cli-only/nested/secret.txt", listedFiles);
            Assert.DoesNotContain("empty", listedFiles);

            var scanner = new Scanner();
            var entries = await scanner.ScanAsync(source, ["cli-only/"], jobs: 4);

            Assert.Equal(
                [".dirdriftignore", ".hidden", "keep.log.txt", "keep.txt", "src/visible.dat"],
                entries.Select(entry => entry.Path));
            Assert.Contains("build/", scanner.EffectiveIgnorePatterns);
            Assert.Contains("*.log", scanner.EffectiveIgnorePatterns);
            Assert.Contains("cli-only/", scanner.EffectiveIgnorePatterns);
            Assert.DoesNotContain(entries, entry => entry.Path.StartsWith("build/", StringComparison.Ordinal));
            Assert.DoesNotContain(entries, entry => entry.Path.StartsWith("cli-only/", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Scanner_is_deterministic_and_never_exceeds_the_jobs_limit()
    {
        var files = Enumerable.Range(0, 200)
            .ToDictionary(
                index => $"generated/file-{index:D3}.dat",
                index => Encoding.UTF8.GetBytes($"content-{index:D3}"),
                StringComparer.Ordinal);
        var expected = files
            .OrderBy(file => file.Key, StringComparer.Ordinal)
            .Select(file => new FileEntry(
                file.Key,
                file.Value.Length,
                Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant()))
            .ToArray();

        foreach (var jobs in new[] { 1, 4, 32 })
        {
            var source = new MemoryFileSource(files, readDelayMilliseconds: 5);
            var scanner = new Scanner();

            var entries = await scanner.ScanAsync(source, Array.Empty<string>(), jobs);

            Assert.Equal(expected, entries);
            Assert.InRange(source.PeakOpenStreams, 1, jobs);
        }
    }

    [Fact]
    public async Task Scanner_reports_the_ordinal_first_failure_even_when_a_later_path_fails_first()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["a.txt"] = Encoding.UTF8.GetBytes("a"),
            ["z.txt"] = Encoding.UTF8.GetBytes("z")
        };
        var source = new MemoryFileSource(
            files,
            customOpen: path =>
            {
                if (path == "a.txt")
                {
                    Thread.Sleep(100);
                }

                throw new IOException($"failure for {path}");
            });

        var exception = await Assert.ThrowsAsync<ScanException>(
            () => new Scanner().ScanAsync(source, Array.Empty<string>(), jobs: 2));

        Assert.Equal("a.txt", exception.Path);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dirdrift-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private sealed class MemoryFileSource : IFileSource
    {
        private readonly IReadOnlyDictionary<string, byte[]> _files;
        private readonly int _readDelayMilliseconds;
        private readonly Func<string, Stream>? _customOpen;
        private int _openStreams;
        private int _peakOpenStreams;

        public MemoryFileSource(
            IReadOnlyDictionary<string, byte[]> files,
            int readDelayMilliseconds = 0,
            Func<string, Stream>? customOpen = null)
        {
            _files = files;
            _readDelayMilliseconds = readDelayMilliseconds;
            _customOpen = customOpen;
        }

        public int PeakOpenStreams => Volatile.Read(ref _peakOpenStreams);

        public IEnumerable<string> ListFiles() => _files.Keys.Reverse();

        public Stream Open(string path)
        {
            if (!_files.ContainsKey(path))
            {
                throw new FileNotFoundException(path);
            }

            if (_customOpen is not null)
            {
                return _customOpen(path);
            }

            var openStreams = Interlocked.Increment(ref _openStreams);
            UpdatePeak(openStreams);
            return new DelayedReadStream(
                _files[path],
                _readDelayMilliseconds,
                () => Interlocked.Decrement(ref _openStreams));
        }

        private void UpdatePeak(int openStreams)
        {
            var observedPeak = Volatile.Read(ref _peakOpenStreams);
            while (openStreams > observedPeak)
            {
                var previous = Interlocked.CompareExchange(ref _peakOpenStreams, openStreams, observedPeak);
                if (previous == observedPeak)
                {
                    return;
                }

                observedPeak = previous;
            }
        }
    }

    private sealed class DelayedReadStream : MemoryStream
    {
        private readonly int _readDelayMilliseconds;
        private readonly Action _onDispose;
        private int _disposed;

        public DelayedReadStream(byte[] contents, int readDelayMilliseconds, Action onDispose)
            : base(contents, writable: false)
        {
            _readDelayMilliseconds = readDelayMilliseconds;
            _onDispose = onDispose;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_readDelayMilliseconds > 0)
            {
                await Task.Delay(_readDelayMilliseconds, cancellationToken);
            }

            return await base.ReadAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _onDispose();
            }

            base.Dispose(disposing);
        }
    }
}
