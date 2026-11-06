using System.Security.Cryptography;
using System.Text;
using Dirdrift.Core;

namespace Dirdrift.Tests;

public sealed class QuietTests
{
    private static readonly (string Path, string Contents)[] Before =
    [
        ("README.md", "r\n"),
        ("docs/old.md", "hello\n"),
        ("gone.txt", "bye\n"),
        ("src/app.cs", "one\n")
    ];

    private static readonly (string Path, string Contents)[] After =
    [
        ("README.md", "r\n"),
        ("docs/new.md", "hello\n"),
        ("src/app.cs", "three!\n"),
        ("notes.txt", "new\n")
    ];

    [Fact]
    public void QuietPrintsNothingAndReturnsOneWhenThereIsDrift()
    {
        using var scenario = new Scenario(Before, After);

        var result = Run(["diff", "--quiet", scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public void QuietPrintsNothingAndReturnsZeroWhenThereIsNoDrift()
    {
        using var scenario = new Scenario(Before, Before);

        var result = Run(["diff", "--quiet", scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public void WithoutTheOptionTheReportIsUnchanged()
    {
        using var scenario = new Scenario(Before, After);

        var plain = Run(["diff", scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(1, plain.ExitCode);
        Assert.Equal(
            "renamed  docs/old.md -> docs/new.md\nmodified src/app.cs\nadded    notes.txt\nremoved  gone.txt\n4 changes\n",
            plain.Stdout);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("json")]
    public void QuietSilencesBothFormats(string format)
    {
        using var scenario = new Scenario(Before, After);

        var result = Run(["diff", "--format", format, "--quiet", scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Theory]
    [InlineData("added", 1)]
    [InlineData("removed", 1)]
    [InlineData("modified", 1)]
    [InlineData("renamed", 1)]
    public void QuietFollowsTheSelectedKinds(string kind, int expected)
    {
        using var scenario = new Scenario(Before, After);

        var result = Run(["diff", "--only", kind, "--quiet", scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(expected, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
    }

    [Fact]
    public void QuietIsZeroWhenNoSelectedKindHasAChange()
    {
        using var scenario = new Scenario(
            [("a.txt", "a\n")],
            [("a.txt", "a\n"), ("b.txt", "b\n")]);

        var removed = Run(["diff", "--quiet", "--only", "removed", scenario.ManifestPath, scenario.SitePath]);
        var added = Run(["diff", "--quiet", "--only", "added", scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(0, removed.ExitCode);
        Assert.Equal(1, added.ExitCode);
        Assert.Equal(string.Empty, removed.Stdout + added.Stdout);
    }

    [Fact]
    public void QuietIsAcceptedBeforeOrAfterTheOtherWords()
    {
        using var scenario = new Scenario(Before, After);

        var first = Run(["diff", "--quiet", scenario.ManifestPath, scenario.SitePath]);
        var last = Run(["diff", scenario.ManifestPath, scenario.SitePath, "--quiet"]);
        var twice = Run(["diff", "--quiet", scenario.ManifestPath, "--quiet", scenario.SitePath]);

        Assert.Equal(1, first.ExitCode);
        Assert.Equal(1, last.ExitCode);
        Assert.Equal(1, twice.ExitCode);
        Assert.Equal(string.Empty, first.Stdout + last.Stdout + twice.Stdout);
    }

    [Theory]
    [InlineData("--quiet=1", "dirdrift: unknown option \"--quiet=1\"\n")]
    [InlineData("--sizes", "dirdrift: --sizes needs --format text\n")]
    public void QuietKeepsTheOtherChecks(string extra, string expectedError)
    {
        using var scenario = new Scenario(Before, After);
        var arguments = extra == "--sizes"
            ? new[] { "diff", "--quiet", "--format", "json", extra, scenario.ManifestPath, scenario.SitePath }
            : new[] { "diff", "--quiet", extra, scenario.ManifestPath, scenario.SitePath };

        var result = Run(arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal(expectedError, result.Stderr);
    }

    [Fact]
    public void SnapshotDoesNotKnowTheOption()
    {
        using var scenario = new Scenario(Before, After);

        var result = Run(["snapshot", "--quiet", scenario.SitePath]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal("dirdrift: unknown option \"--quiet\"\n", result.Stderr);
    }

    [Fact]
    public void QuietDoesNotHideAMissingManifest()
    {
        using var scenario = new Scenario(Before, After);
        var missing = Path.Combine(Path.GetDirectoryName(scenario.SitePath)!, "nope.json");

        var result = Run(["diff", "--quiet", missing, scenario.SitePath]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.NotEqual(string.Empty, result.Stderr);
    }

    [Fact]
    public void ReadmeDocumentsTheQuietOptionAndItsUsageLine()
    {
        var readme = NormalizeNewlines(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "README.md")));

        Assert.Contains("[--sizes] [--only KINDS] [--quiet] MANIFEST DIR", readme);
        Assert.Contains("## Quiet diff", readme);
        Assert.Contains("dirdrift diff --quiet before.json ./site || echo \"the folder changed\"", readme);
    }

    private static ScenarioResult Run(string[] args)
    {
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        var exitCode = Program.Run(args, stdout, stderr);
        return new ScenarioResult(exitCode, stdout.ToString(), stderr.ToString());
    }

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

    private sealed record ScenarioResult(int ExitCode, string Stdout, string Stderr);

    private sealed class Scenario : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"dirdrift-quiet-tests-{Guid.NewGuid():N}");

        public Scenario((string Path, string Contents)[] manifestFiles, (string Path, string Contents)[] currentFiles)
        {
            SitePath = Path.Combine(root, "site");
            Directory.CreateDirectory(SitePath);
            foreach (var (path, contents) in currentFiles)
            {
                var fullPath = Path.Combine(SitePath, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, contents, new UTF8Encoding(false));
            }

            var entries = manifestFiles
                .Select(file => Entry(file.Path, Encoding.UTF8.GetBytes(file.Contents)))
                .ToArray();
            ManifestPath = Path.Combine(root, "before.json");
            File.WriteAllBytes(ManifestPath, ManifestStore.Serialize(new Manifest(Manifest.CurrentVersion, entries)));
        }

        public string SitePath { get; }

        public string ManifestPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        private static FileEntry Entry(string path, byte[] contents) =>
            new(path, contents.LongLength, Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant());
    }
}
