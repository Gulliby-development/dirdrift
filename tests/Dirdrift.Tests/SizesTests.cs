using System.Security.Cryptography;
using System.Text;
using Dirdrift.Core;

namespace Dirdrift.Tests;

public sealed class SizesTests
{
    private const string WorkedExampleReport =
        "renamed  docs/old.md -> docs/new.md (6 B)\n" +
        "modified src/app.cs (4 -> 7 B, +3)\n" +
        "added    notes.txt (4 B)\n" +
        "removed  gone.txt (4 B)\n" +
        "4 changes, net +3 B\n";

    [Fact]
    public void SizesReportMatchesTheWorkedExample()
    {
        using var scenario = new WorkedScenario(applyChanges: true);

        var result = Run(["diff", "--sizes", scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(WorkedExampleReport, result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        AssertLf(result.Stdout);
    }

    [Fact]
    public void SizesReportIncludesZeroAndNegativeDeltasAndCountsEmptyFiles()
    {
        using var temporary = new TemporaryDirectory();
        var site = Path.Combine(temporary.Path, "site");
        Directory.CreateDirectory(site);

        WriteFile(site, "a.txt", "xy");
        WriteFile(site, "b.txt", "b");
        WriteFile(site, "same-size.txt", "new");
        WriteFile(site, "empty.txt", string.Empty);

        var manifest = WriteManifest(
            temporary.Path,
            ("a.txt", "12345"),
            ("b.txt", "a"),
            ("same-size.txt", "old"),
            ("gone.txt", "12345678"));

        var result = Run(["diff", "--sizes", manifest, site]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(
            "modified a.txt (5 -> 2 B, -3)\n" +
            "modified b.txt (1 -> 1 B, +0)\n" +
            "modified same-size.txt (3 -> 3 B, +0)\n" +
            "added    empty.txt (0 B)\n" +
            "removed  gone.txt (8 B)\n" +
            "5 changes, net -11 B\n",
            result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        AssertLf(result.Stdout);
    }

    [Fact]
    public void SizesOptionWorksBeforeBetweenAndAfterOperandsAndCanRepeat()
    {
        using var scenario = new WorkedScenario(applyChanges: true);
        string[][] arguments =
        [
            ["diff", "--sizes", scenario.ManifestPath, scenario.SitePath],
            ["diff", scenario.ManifestPath, "--sizes", scenario.SitePath],
            ["diff", scenario.ManifestPath, scenario.SitePath, "--sizes"],
            ["diff", "--sizes", scenario.ManifestPath, "--sizes", scenario.SitePath, "--sizes"]
        ];

        foreach (var args in arguments)
        {
            var result = Run(args);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal(WorkedExampleReport, result.Stdout);
            Assert.Equal(string.Empty, result.Stderr);
            AssertLf(result.Stdout);
        }
    }

    [Fact]
    public void SizesPreservesNoDriftAndDefaultTextReports()
    {
        using (var unchanged = new WorkedScenario(applyChanges: false))
        {
            var noDrift = Run(["diff", "--sizes", unchanged.ManifestPath, unchanged.SitePath]);

            Assert.Equal(0, noDrift.ExitCode);
            Assert.Equal("no drift\n", noDrift.Stdout);
            Assert.Equal(string.Empty, noDrift.Stderr);
            AssertLf(noDrift.Stdout);
        }

        using var changed = new WorkedScenario(applyChanges: true);
        var defaultText = Run(["diff", changed.ManifestPath, changed.SitePath]);

        Assert.Equal(1, defaultText.ExitCode);
        Assert.Equal(
            "renamed  docs/old.md -> docs/new.md\n" +
            "modified src/app.cs\n" +
            "added    notes.txt\n" +
            "removed  gone.txt\n" +
            "4 changes\n",
            defaultText.Stdout);
        Assert.Equal(string.Empty, defaultText.Stderr);
        AssertLf(defaultText.Stdout);
    }

    [Fact]
    public void RenameReportsItsSizeOnceAndContributesZeroToNet()
    {
        using var temporary = new TemporaryDirectory();
        var site = Path.Combine(temporary.Path, "site");
        Directory.CreateDirectory(site);
        WriteFile(site, "old.txt", "abc");
        var manifest = WriteManifest(temporary.Path, ("old.txt", "abc"));

        File.Delete(Path.Combine(site, "old.txt"));
        WriteFile(site, "new.txt", "abc");

        var result = Run(["diff", "--sizes", manifest, site]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(
            "renamed  old.txt -> new.txt (3 B)\n" +
            "1 change, net +0 B\n",
            result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        AssertLf(result.Stdout);
    }

    [Fact]
    public void SizesUseOnlyEntriesRemainingAfterIgnoreRules()
    {
        using var temporary = new TemporaryDirectory();
        var site = Path.Combine(temporary.Path, "site");
        Directory.CreateDirectory(site);
        WriteFile(site, "visible.txt", "now!");
        WriteFile(site, "ignored.txt", "a much longer ignored file");

        var manifest = WriteManifest(
            temporary.Path,
            ("visible.txt", "ab"),
            ("ignored.txt", "old"));

        var result = Run(["diff", "--sizes", "--ignore", "ignored.txt", manifest, site]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(
            "modified visible.txt (2 -> 4 B, +2)\n" +
            "1 change, net +2 B\n",
            result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        AssertLf(result.Stdout);
    }

    [Fact]
    public void SizesAndOptionValidationErrorsUseExactStreamsAndPrecedence()
    {
        AssertRun(
            ["diff", "--sizes", "--format", "json"],
            2,
            string.Empty,
            "dirdrift: --sizes needs --format text\n");
        AssertRun(
            ["diff", "--format=json", "--sizes"],
            2,
            string.Empty,
            "dirdrift: --sizes needs --format text\n");
        AssertRun(
            ["diff", "--format", "xml", "--sizes"],
            2,
            string.Empty,
            "dirdrift: invalid --format \"xml\"\n");
        AssertRun(
            ["diff", "--jobs=0", "--sizes"],
            2,
            string.Empty,
            "dirdrift: --jobs must be an integer from 1 through 32\n");
        AssertRun(
            ["diff", "--ignore=", "--sizes"],
            2,
            string.Empty,
            "dirdrift: invalid --ignore \"\"\n");
        AssertRun(
            ["snapshot", "--sizes"],
            2,
            string.Empty,
            "dirdrift: unknown option \"--sizes\"\n");
        AssertRun(
            ["diff", "--sizes=1"],
            2,
            string.Empty,
            "dirdrift: unknown option \"--sizes=1\"\n");
    }

    [Fact]
    public void ReadmeDocumentsTheSizesUsageAndWorkedExample()
    {
        var repositoryRoot = FindRepositoryRoot();
        var readme = NormalizeNewlines(File.ReadAllText(Path.Combine(repositoryRoot, "README.md")));

        Assert.Contains(
            "dirdrift diff [--ignore PATTERN]... [--jobs N] [--format text|json] [--sizes] [--only KINDS] MANIFEST DIR",
            readme);
        Assert.Contains("## Sizes in the text diff", readme);
        Assert.Contains(WorkedExampleReport.TrimEnd('\n'), readme);
    }

    private static string WriteManifest(string root, params (string Path, string Contents)[] files)
    {
        var entries = files
            .Select(file => Entry(file.Path, Encoding.UTF8.GetBytes(file.Contents)))
            .ToArray();
        var manifestPath = Path.Combine(root, "before.json");
        File.WriteAllBytes(manifestPath, ManifestStore.Serialize(new Manifest(Manifest.CurrentVersion, entries)));
        return manifestPath;
    }

    private static FileEntry Entry(string path, byte[] contents) =>
        new(path, contents.LongLength, Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant());

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

    private sealed class WorkedScenario : IDisposable
    {
        private readonly TemporaryDirectory temporary = new();

        public WorkedScenario(bool applyChanges)
        {
            SitePath = Path.Combine(temporary.Path, "site");
            Directory.CreateDirectory(SitePath);
            WriteFile(SitePath, "README.md", "r\n");
            WriteFile(SitePath, "docs/old.md", "hello\n");
            WriteFile(SitePath, "gone.txt", "bye\n");
            WriteFile(SitePath, "src/app.cs", "one\n");

            ManifestPath = WriteManifest(
                temporary.Path,
                ("README.md", "r\n"),
                ("docs/old.md", "hello\n"),
                ("gone.txt", "bye\n"),
                ("src/app.cs", "one\n"));

            if (applyChanges)
            {
                ApplyChanges();
            }
        }

        public string SitePath { get; }

        public string ManifestPath { get; }

        public void Dispose() => temporary.Dispose();

        private void ApplyChanges()
        {
            File.Delete(Path.Combine(SitePath, "docs", "old.md"));
            File.Delete(Path.Combine(SitePath, "gone.txt"));
            WriteFile(SitePath, "docs/new.md", "hello\n");
            WriteFile(SitePath, "src/app.cs", "three!\n");
            WriteFile(SitePath, "notes.txt", "new\n");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dirdrift-sizes-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
