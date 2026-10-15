using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dirdrift.Core;

namespace Dirdrift.Tests;

public sealed class CliTests
{
    private static readonly string ExampleManifest = string.Join(
        "\n",
        [
            "{",
            "  \"version\": 1,",
            "  \"files\": [",
            "    {",
            "      \"path\": \"README.md\",",
            "      \"size\": 2,",
            "      \"sha256\": \"8e54b0ca18020275e4aef1ca0eb5e197e066c065c1864817652a8a39c55402cd\"",
            "    },",
            "    {",
            "      \"path\": \"docs/old.md\",",
            "      \"size\": 6,",
            "      \"sha256\": \"5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03\"",
            "    },",
            "    {",
            "      \"path\": \"gone.txt\",",
            "      \"size\": 4,",
            "      \"sha256\": \"abc6fd595fc079d3114d4b71a4d84b1d1d0f79df1e70f8813212f2a65d8916df\"",
            "    },",
            "    {",
            "      \"path\": \"src/app.cs\",",
            "      \"size\": 4,",
            "      \"sha256\": \"2c8b08da5ce60398e1f19af0e5dccc744df274b826abe585eaba68c525434806\"",
            "    }",
            "  ]",
            "}",
            string.Empty
        ]);

    private const string Usage =
        "usage:\n" +
        "  dirdrift snapshot [--out FILE] [--ignore PATTERN]... [--jobs N] [--summary] DIR\n" +
        "  dirdrift diff [--ignore PATTERN]... [--jobs N] [--format text|json] [--sizes] [--only KINDS] MANIFEST DIR\n";

    [Fact]
    public void SnapshotAndDiffMatchTheWorkedExample()
    {
        using var temporary = new TemporaryDirectory();
        var directory = Path.Combine(temporary.Path, "deployment");
        Directory.CreateDirectory(directory);
        WriteFile(directory, "README.md", "r\n");
        WriteFile(directory, "docs/old.md", "hello\n");
        WriteFile(directory, "gone.txt", "bye\n");
        WriteFile(directory, "src/app.cs", "one\n");

        var snapshot = Run(["snapshot", "--jobs=1", directory]);
        Assert.Equal(0, snapshot.ExitCode);
        Assert.Equal(string.Empty, snapshot.Stderr);
        Assert.Equal(ExampleManifest, snapshot.Stdout);

        var savedManifest = Path.Combine(temporary.Path, "before.json");
        var firstOut = Path.Combine(temporary.Path, "unused.json");
        var saved = Run(["snapshot", $"--out={firstOut}", directory, "--jobs", "1", "--jobs=4", "--out", savedManifest]);
        Assert.Equal(0, saved.ExitCode);
        Assert.Equal(string.Empty, saved.Stdout);
        Assert.Equal(string.Empty, saved.Stderr);
        Assert.Equal(Encoding.UTF8.GetBytes(ExampleManifest), File.ReadAllBytes(savedManifest));
        Assert.False(File.Exists(firstOut));

        File.Delete(Path.Combine(directory, "docs", "old.md"));
        File.Delete(Path.Combine(directory, "gone.txt"));
        WriteFile(directory, "docs/new.md", "hello\n");
        WriteFile(directory, "src/app.cs", "two\n");
        WriteFile(directory, "notes.txt", "new\n");

        var textDiff = Run(["diff", savedManifest, directory]);
        Assert.Equal(1, textDiff.ExitCode);
        Assert.Equal(
            "renamed  docs/old.md -> docs/new.md\n" +
            "modified src/app.cs\n" +
            "added    notes.txt\n" +
            "removed  gone.txt\n" +
            "4 changes\n",
            textDiff.Stdout);
        Assert.Equal(string.Empty, textDiff.Stderr);

        var jsonDiff = Run(["diff", savedManifest, "--format=json", directory]);
        Assert.Equal(1, jsonDiff.ExitCode);
        Assert.Equal(
            "{\"renamed\":[{\"from\":\"docs/old.md\",\"to\":\"docs/new.md\"}],\"modified\":[\"src/app.cs\"],\"added\":[\"notes.txt\"],\"removed\":[\"gone.txt\"],\"changes\":4}\n",
            jsonDiff.Stdout);
        Assert.Equal(string.Empty, jsonDiff.Stderr);

        var currentManifest = Path.Combine(temporary.Path, "current.json");
        var currentSnapshot = Run(["snapshot", "--out", currentManifest, directory]);
        Assert.Equal(0, currentSnapshot.ExitCode);
        var noDrift = Run(["diff", "--format", "text", currentManifest, directory]);
        Assert.Equal(0, noDrift.ExitCode);
        Assert.Equal("no drift\n", noDrift.Stdout);
        Assert.Equal(string.Empty, noDrift.Stderr);
    }

    [Fact]
    public void SnapshotAndDiffApplyCliAndRootIgnoreRulesToBothSides()
    {
        using var temporary = new TemporaryDirectory();
        var directory = Path.Combine(temporary.Path, "deployment");
        Directory.CreateDirectory(directory);
        const string ignoreContents = "  # generated files\r\n\t\r\nroot-hidden/\r\nroot-only.txt\r\n";
        WriteFile(directory, ".dirdriftignore", ignoreContents);
        WriteFile(directory, "visible.txt", "keep\n");
        WriteFile(directory, "root-only.txt", "root hidden\n");
        WriteFile(directory, "cli-hidden.tmp", "cli hidden\n");
        WriteFile(directory, "root-hidden/old.txt", "old hidden\n");

        var snapshot = Run(["snapshot", directory, "--ignore=cli-hidden.tmp"]);
        Assert.Equal(0, snapshot.ExitCode);
        var snapshotManifest = ManifestStore.Parse(Encoding.UTF8.GetBytes(snapshot.Stdout));
        Assert.Equal(
            [".dirdriftignore", "visible.txt"],
            snapshotManifest.Files.Select(file => file.Path).ToArray());

        var manifestPath = Path.Combine(temporary.Path, "comparison.json");
        var ignoreBytes = Encoding.UTF8.GetBytes(ignoreContents);
        var visibleBytes = Encoding.UTF8.GetBytes("keep\n");
        var manifest = new Manifest(
            Manifest.CurrentVersion,
            [
                Entry(".dirdriftignore", ignoreBytes),
                Entry("visible.txt", visibleBytes),
                Entry("root-hidden/manifest-only.txt", Encoding.UTF8.GetBytes("old\n")),
                Entry("root-only.txt", Encoding.UTF8.GetBytes("old\n")),
                Entry("cli-hidden.tmp", Encoding.UTF8.GetBytes("old\n"))
            ]);
        File.WriteAllBytes(manifestPath, ManifestStore.Serialize(manifest));

        var diff = Run(["diff", "--format=json", manifestPath, "--ignore", "cli-hidden.tmp", directory]);
        Assert.Equal(0, diff.ExitCode);
        Assert.Equal("{\"renamed\":[],\"modified\":[],\"added\":[],\"removed\":[],\"changes\":0}\n", diff.Stdout);
        Assert.Equal(string.Empty, diff.Stderr);
    }

    [Fact]
    public void HelpNoArgumentsAndCommandErrorsUseExactStreamsAndMessages()
    {
        AssertRun(["--help"], 0, Usage, string.Empty);
        AssertRun([], 2, string.Empty, Usage);
        AssertRun(["unknown"], 2, string.Empty, "dirdrift: unknown command \"unknown\"\n");
        AssertRun(["snapshot", "--unknown", "folder"], 2, string.Empty, "dirdrift: unknown option \"--unknown\"\n");
        AssertRun(["diff", "--out", "file", "manifest", "folder"], 2, string.Empty, "dirdrift: unknown option \"--out\"\n");
        AssertRun(["snapshot", "--format", "json", "folder"], 2, string.Empty, "dirdrift: unknown option \"--format\"\n");
    }

    [Fact]
    public void MissingValuesAreReportedBeforeFollowingOptions()
    {
        AssertRun(["snapshot", "--out"], 2, string.Empty, "dirdrift: --out requires a value\n");
        AssertRun(["snapshot", "--ignore"], 2, string.Empty, "dirdrift: --ignore requires a value\n");
        AssertRun(["snapshot", "--jobs"], 2, string.Empty, "dirdrift: --jobs requires a value\n");
        AssertRun(["diff", "--format"], 2, string.Empty, "dirdrift: --format requires a value\n");
        AssertRun(["diff", "--jobs", "--format=json", "manifest", "folder"], 2, string.Empty, "dirdrift: --jobs requires a value\n");
    }

    [Fact]
    public void OptionValidationAndPositionalErrorsMatchTheContract()
    {
        AssertRun(["snapshot", "--jobs=0", "folder"], 2, string.Empty, "dirdrift: --jobs must be an integer from 1 through 32\n");
        AssertRun(["snapshot", "--jobs=33", "folder"], 2, string.Empty, "dirdrift: --jobs must be an integer from 1 through 32\n");
        AssertRun(["snapshot", "--jobs=1.5", "folder"], 2, string.Empty, "dirdrift: --jobs must be an integer from 1 through 32\n");
        AssertRun(["snapshot", "--jobs=-1", "folder"], 2, string.Empty, "dirdrift: --jobs must be an integer from 1 through 32\n");
        AssertRun(["diff", "--format=xml", "manifest", "folder"], 2, string.Empty, "dirdrift: invalid --format \"xml\"\n");
        AssertRun(["snapshot", "--ignore=", "folder"], 2, string.Empty, "dirdrift: invalid --ignore \"\"\n");
        AssertRun(["snapshot", "--ignore=///", "folder"], 2, string.Empty, "dirdrift: invalid --ignore \"///\"\n");
        AssertRun(["snapshot"], 2, string.Empty, "dirdrift: usage: dirdrift snapshot [options] DIR\n");
        AssertRun(["diff", "manifest"], 2, string.Empty, "dirdrift: usage: dirdrift diff [options] MANIFEST DIR\n");
    }

    [Fact]
    public void DirectoryManifestAndWriteFailuresUseTheRequestedErrorShape()
    {
        using var temporary = new TemporaryDirectory();
        var notDirectory = Path.Combine(temporary.Path, "missing-directory");
        AssertRun(["snapshot", notDirectory], 2, string.Empty, $"dirdrift: {notDirectory}: not a directory\n");

        var directory = Path.Combine(temporary.Path, "directory");
        Directory.CreateDirectory(directory);
        var missingManifest = Path.Combine(temporary.Path, "missing.json");
        AssertRun(
            ["diff", missingManifest, directory],
            2,
            string.Empty,
            $"dirdrift: {missingManifest}: invalid manifest: cannot read\n");

        var invalidManifest = Path.Combine(temporary.Path, "invalid.json");
        File.WriteAllText(invalidManifest, "{", new UTF8Encoding(false));
        AssertRun(
            ["diff", invalidManifest, directory],
            2,
            string.Empty,
            $"dirdrift: {invalidManifest}: invalid manifest: not valid JSON\n");

        var unwritablePath = Path.Combine(temporary.Path, "absent-parent", "snapshot.json");
        AssertRun(
            ["snapshot", "--out", unwritablePath, directory],
            2,
            string.Empty,
            $"dirdrift: {unwritablePath}: cannot write\n");
    }

    [Fact]
    public void CliReportsEveryManifestValidationFailure()
    {
        using var temporary = new TemporaryDirectory();
        var directory = Path.Combine(temporary.Path, "directory");
        Directory.CreateDirectory(directory);

        const string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var manifestCases = new[]
        {
            ("unsupported-version.json", "{\"version\":2,\"files\":[]}", "unsupported version"),
            ("missing-files.json", "{\"version\":1}", "missing files"),
            ("bad-entry.json", "{\"version\":1,\"files\":[{}]}", "bad entry 0"),
            (
                "duplicate-path.json",
                "{\"version\":1,\"files\":[{\"path\":\"same.txt\",\"size\":0,\"sha256\":\"" + hash + "\"},{\"path\":\"same.txt\",\"size\":0,\"sha256\":\"" + hash + "\"}]}",
                "duplicate path \"same.txt\"")
        };

        foreach (var (fileName, contents, reason) in manifestCases)
        {
            var manifest = Path.Combine(temporary.Path, fileName);
            File.WriteAllText(manifest, contents, new UTF8Encoding(false));

            AssertRun(
                ["diff", manifest, directory],
                2,
                string.Empty,
                $"dirdrift: {manifest}: invalid manifest: {reason}\n");
        }
    }

    [Fact]
    public void CliReportsScanReadFailuresWithNoStandardOutput()
    {
        using var temporary = new TemporaryDirectory();
        var directory = Path.Combine(temporary.Path, "directory");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, ".dirdriftignore"), [0xFF]);

        AssertRun(
            ["snapshot", directory],
            2,
            string.Empty,
            "dirdrift: .dirdriftignore: cannot read\n");
    }

    [Fact]
    public void ReadmeContainsTheWorkedExampleAndRequiredHeadings()
    {
        var repositoryRoot = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(repositoryRoot, "README.md"));
        var normalized = NormalizeNewlines(readme);
        var license = File.ReadAllText(Path.Combine(repositoryRoot, "LICENSE"));
        Assert.Contains("MIT License", license);
        Assert.Contains("Permission is hereby granted, free of charge", license);
        Assert.Contains("THE SOFTWARE IS PROVIDED \"AS IS\"", license);
        Assert.Contains(ExampleManifest.TrimEnd('\n'), normalized);

        var expectedExampleLines = new[]
        {
            "{",
            "  \"version\": 1,",
            "  \"files\": [",
            "      \"path\": \"README.md\",",
            "      \"size\": 2,",
            "      \"sha256\": \"8e54b0ca18020275e4aef1ca0eb5e197e066c065c1864817652a8a39c55402cd\"",
            "      \"path\": \"docs/old.md\",",
            "      \"size\": 6,",
            "      \"sha256\": \"5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03\"",
            "      \"path\": \"gone.txt\",",
            "      \"size\": 4,",
            "      \"sha256\": \"abc6fd595fc079d3114d4b71a4d84b1d1d0f79df1e70f8813212f2a65d8916df\"",
            "      \"path\": \"src/app.cs\",",
            "      \"size\": 4,",
            "      \"sha256\": \"2c8b08da5ce60398e1f19af0e5dccc744df274b826abe585eaba68c525434806\"",
            "  ]",
            "}",
            "renamed  docs/old.md -> docs/new.md",
            "modified src/app.cs",
            "added    notes.txt",
            "removed  gone.txt",
            "4 changes"
        };
        foreach (var line in expectedExampleLines)
        {
            Assert.Contains(line, normalized);
        }

        foreach (var heading in new[]
        {
            "## Install",
            "## Usage",
            "## Ignore patterns",
            "## Manifest format",
            "## Exit codes",
            "## Development"
        })
        {
            Assert.Contains(heading, normalized);
        }
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dirdrift-tests-{Guid.NewGuid():N}");
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
