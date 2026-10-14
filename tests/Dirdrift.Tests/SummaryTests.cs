using System.Text;

namespace Dirdrift.Tests;

public sealed class SummaryTests
{
    [Fact]
    public void SummaryWritesWorkedExampleToStandardOutputWithoutChangingManifest()
    {
        using var temporary = new TemporaryDirectory();
        var site = Path.Combine(temporary.Root, "site");
        CreateWorkedExample(site);

        var withoutSummary = Run(["snapshot", site]);
        var withSummary = Run(["snapshot", site, "--summary"]);

        Assert.Equal(0, withoutSummary.ExitCode);
        Assert.Equal(string.Empty, withoutSummary.Stderr);
        Assert.Equal(0, withSummary.ExitCode);
        Assert.Equal(withoutSummary.Stdout, withSummary.Stdout);
        Assert.Equal("4 files, 16 bytes\n", withSummary.Stderr);
        AssertLf(withSummary.Stdout);
        AssertLf(withSummary.Stderr);
    }

    [Fact]
    public void SummaryWritesWorkedExampleToFileAfterManifestAndCanRepeat()
    {
        using var temporary = new TemporaryDirectory();
        var site = Path.Combine(temporary.Root, "site");
        var output = Path.Combine(temporary.Root, "before.json");
        CreateWorkedExample(site);

        var withoutSummary = Run(["snapshot", site]);
        var withSummary = Run(["snapshot", "--summary", "--out", output, "--summary", site]);

        Assert.Equal(0, withoutSummary.ExitCode);
        Assert.Equal(0, withSummary.ExitCode);
        Assert.Equal(string.Empty, withSummary.Stdout);
        Assert.Equal("4 files, 16 bytes\n", withSummary.Stderr);
        Assert.Equal(Encoding.UTF8.GetBytes(withoutSummary.Stdout), File.ReadAllBytes(output));
        AssertLf(withSummary.Stderr);
    }

    [Fact]
    public void SummaryUsesSingularAndPluralLabelsAtZeroAndOne()
    {
        using var temporary = new TemporaryDirectory();

        var singleFileSite = Path.Combine(temporary.Root, "single");
        Directory.CreateDirectory(singleFileSite);
        WriteFile(singleFileSite, "a.txt", "x");
        var singleFile = Run(["snapshot", "--summary", singleFileSite]);
        AssertSummary(singleFile, "1 file, 1 byte\n");

        var emptySite = Path.Combine(temporary.Root, "empty");
        Directory.CreateDirectory(emptySite);
        var empty = Run(["snapshot", "--summary", emptySite]);
        AssertSummary(empty, "0 files, 0 bytes\n");

        var emptyFilesSite = Path.Combine(temporary.Root, "empty-files");
        Directory.CreateDirectory(emptyFilesSite);
        WriteFile(emptyFilesSite, "a.txt", string.Empty);
        WriteFile(emptyFilesSite, "b.txt", string.Empty);
        var emptyFiles = Run(["snapshot", "--summary", emptyFilesSite]);
        AssertSummary(emptyFiles, "2 files, 0 bytes\n");
    }

    [Fact]
    public void SummaryCountsOnlyFilesKeptByIgnoreOptions()
    {
        using var temporary = new TemporaryDirectory();
        var site = Path.Combine(temporary.Root, "site");
        CreateWorkedExample(site);

        var result = Run(["snapshot", "--ignore", "docs/", "--ignore=*.cs", "--summary", site]);

        AssertSummary(result, "2 files, 6 bytes\n");
    }

    [Fact]
    public void SummaryCountsOnlyFilesKeptByIgnoreFile()
    {
        using var temporary = new TemporaryDirectory();
        var site = Path.Combine(temporary.Root, "site");
        Directory.CreateDirectory(site);
        WriteFile(site, ".dirdriftignore", "ignored.txt\n.dirdriftignore\n");
        WriteFile(site, "ignored.txt", "ignored content");
        WriteFile(site, "visible.txt", "ok");

        var result = Run(["snapshot", "--summary", site]);

        AssertSummary(result, "1 file, 2 bytes\n");
    }

    [Fact]
    public void SummaryIsValuelessAndAvailableOnlyForSnapshots()
    {
        AssertRun(
            ["snapshot", "--summary=1", "site"],
            2,
            string.Empty,
            "dirdrift: unknown option \"--summary=1\"\n");
        AssertRun(
            ["diff", "--summary"],
            2,
            string.Empty,
            "dirdrift: unknown option \"--summary\"\n");
    }

    [Fact]
    public void FailedOutputDoesNotPrintSummary()
    {
        using var temporary = new TemporaryDirectory();
        var site = Path.Combine(temporary.Root, "site");
        Directory.CreateDirectory(site);
        WriteFile(site, "a.txt", "x");
        var output = Path.Combine(temporary.Root, "missing", "before.json");

        var result = Run(["snapshot", "--summary", "--out", output, site]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal($"dirdrift: {output}: cannot write\n", result.Stderr);
        AssertLf(result.Stderr);
    }

    [Fact]
    public void ReadmeDocumentsSnapshotSummary()
    {
        var repositoryRoot = FindRepositoryRoot();
        var readme = NormalizeNewlines(File.ReadAllText(Path.Combine(repositoryRoot, "README.md")));

        Assert.Contains(
            "dirdrift snapshot [--out FILE] [--ignore PATTERN]... [--jobs N] [--summary] DIR",
            readme);
        Assert.Contains("## Snapshot summary", readme);
        Assert.Contains("number of files in the snapshot and their total size in bytes", readme);
        Assert.Contains("dirdrift snapshot --summary --out before.json ./site", readme);
        Assert.Contains("4 files, 16 bytes", readme);
        Assert.Contains("Only files that the ignore patterns keep are counted.", readme);
    }

    private static void CreateWorkedExample(string site)
    {
        Directory.CreateDirectory(site);
        WriteFile(site, "README.md", "r\n");
        WriteFile(site, "docs/old.md", "hello\n");
        WriteFile(site, "gone.txt", "bye\n");
        WriteFile(site, "src/app.cs", "one\n");
    }

    private static void WriteFile(string root, string relativePath, string contents)
    {
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents, new UTF8Encoding(false));
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(string[] args)
    {
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        var exitCode = Program.Run(args, stdout, stderr);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private static void AssertRun(string[] args, int exitCode, string stdout, string stderr)
    {
        var result = Run(args);
        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(stdout, result.Stdout);
        Assert.Equal(stderr, result.Stderr);
        AssertLf(result.Stdout);
        AssertLf(result.Stderr);
    }

    private static void AssertSummary((int ExitCode, string Stdout, string Stderr) result, string expectedSummary)
    {
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expectedSummary, result.Stderr);
        AssertLf(result.Stdout);
        AssertLf(result.Stderr);
    }

    private static void AssertLf(string value) =>
        Assert.DoesNotContain('\r', value);

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Dirdrift.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find Dirdrift.slnx above the test output directory.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(), $"dirdrift-summary-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
