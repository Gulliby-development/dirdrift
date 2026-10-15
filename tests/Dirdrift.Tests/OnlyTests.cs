using System.Security.Cryptography;
using System.Text;
using Dirdrift.Core;

namespace Dirdrift.Tests;

public sealed class OnlyTests
{
    private const string WorkedExampleText =
        "renamed  docs/old.md -> docs/new.md\n" +
        "modified src/app.cs\n" +
        "added    notes.txt\n" +
        "removed  gone.txt\n" +
        "4 changes\n";

    private const string SelectingKindsReadmeSection =
        "## Selecting kinds of change\n" +
        "\n" +
        "Pass `--only KINDS` to `diff` to report only some kinds of change. `KINDS` is a comma-separated list of `added`, `removed`, `modified` and `renamed` (lowercase; a kind may be repeated). The other kinds are left out of the text report, the JSON lists, the `--sizes` figures and the change count, and the exit code is 1 only when a selected kind has a change; when none has, the output is `no drift` and the exit code is 0. Renames are paired before the selection, so `--only added` does not list the new path of a renamed file. `--only` is available only for `diff`. For the folder of the earlier example:\n" +
        "\n" +
        "```sh\n" +
        "dirdrift diff --only added,removed before.json ./site\n" +
        "```\n" +
        "\n" +
        "prints:\n" +
        "\n" +
        "```text\n" +
        "added    notes.txt\n" +
        "removed  gone.txt\n" +
        "2 changes\n" +
        "```\n" +
        "\n";

    private static readonly (string Path, string Contents)[] WorkedExampleBefore =
    [
        ("README.md", "r\n"),
        ("docs/old.md", "hello\n"),
        ("gone.txt", "bye\n"),
        ("src/app.cs", "one\n")
    ];

    private static readonly (string Path, string Contents)[] WorkedExampleAfter =
    [
        ("README.md", "r\n"),
        ("docs/new.md", "hello\n"),
        ("src/app.cs", "three!\n"),
        ("notes.txt", "new\n")
    ];

    [Theory]
    [InlineData("renamed", "renamed  docs/old.md -> docs/new.md\n1 change\n")]
    [InlineData("modified", "modified src/app.cs\n1 change\n")]
    [InlineData("added", "added    notes.txt\n1 change\n")]
    [InlineData("removed", "removed  gone.txt\n1 change\n")]
    public void TextReportIncludesOnlyTheSelectedKind(string kind, string expected)
    {
        using var scenario = WorkedExample();

        var result = Run(["diff", "--only", kind, scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(expected, result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        AssertLf(result.Stdout);
    }

    [Fact]
    public void TextReportSelectsAddedAndRemovedKinds()
    {
        using var scenario = WorkedExample();

        var result = Run(["diff", "--only", "added,removed", scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("added    notes.txt\nremoved  gone.txt\n2 changes\n", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        AssertLf(result.Stdout);
    }

    [Fact]
    public void SelectingAllKindsMatchesTextAndJsonWithoutTheOption()
    {
        using var scenario = WorkedExample();
        var allKinds = "removed,modified,renamed,added";

        var defaultText = Run(["diff", scenario.ManifestPath, scenario.SitePath]);
        var selectedText = Run(["diff", "--only", allKinds, scenario.ManifestPath, scenario.SitePath]);
        Assert.Equal(defaultText.ExitCode, selectedText.ExitCode);
        Assert.Equal(defaultText.Stdout, selectedText.Stdout);
        Assert.Equal(string.Empty, selectedText.Stderr);
        Assert.Equal(WorkedExampleText, selectedText.Stdout);

        var defaultJson = Run(["diff", "--format", "json", scenario.ManifestPath, scenario.SitePath]);
        var selectedJson = Run(["diff", "--format", "json", "--only", allKinds, scenario.ManifestPath, scenario.SitePath]);
        Assert.Equal(defaultJson.ExitCode, selectedJson.ExitCode);
        Assert.Equal(defaultJson.Stdout, selectedJson.Stdout);
        Assert.Equal(string.Empty, selectedJson.Stderr);
        AssertLf(selectedJson.Stdout);
    }

    [Theory]
    [InlineData("added,removed,modified,renamed")]
    [InlineData("added,removed,renamed,modified")]
    [InlineData("added,modified,removed,renamed")]
    [InlineData("added,modified,renamed,removed")]
    [InlineData("added,renamed,removed,modified")]
    [InlineData("added,renamed,modified,removed")]
    [InlineData("removed,added,modified,renamed")]
    [InlineData("removed,added,renamed,modified")]
    [InlineData("removed,modified,added,renamed")]
    [InlineData("removed,modified,renamed,added")]
    [InlineData("removed,renamed,added,modified")]
    [InlineData("removed,renamed,modified,added")]
    [InlineData("modified,added,removed,renamed")]
    [InlineData("modified,added,renamed,removed")]
    [InlineData("modified,removed,added,renamed")]
    [InlineData("modified,removed,renamed,added")]
    [InlineData("modified,renamed,added,removed")]
    [InlineData("modified,renamed,removed,added")]
    [InlineData("renamed,added,removed,modified")]
    [InlineData("renamed,added,modified,removed")]
    [InlineData("renamed,removed,added,modified")]
    [InlineData("renamed,removed,modified,added")]
    [InlineData("renamed,modified,added,removed")]
    [InlineData("renamed,modified,removed,added")]
    public void AllKindsAreAcceptedInEveryOrder(string kinds)
    {
        using var scenario = WorkedExample();

        var result = Run(["diff", "--only", kinds, scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(WorkedExampleText, result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        AssertLf(result.Stdout);
    }

    [Fact]
    public void OnlyOptionWorksBeforeBetweenAndAfterOperandsAndAmongOtherOptions()
    {
        using var scenario = WorkedExample();
        string[][] arguments =
        [
            ["diff", "--only", "added", scenario.ManifestPath, scenario.SitePath],
            ["diff", scenario.ManifestPath, "--only", "added", scenario.SitePath],
            ["diff", scenario.ManifestPath, scenario.SitePath, "--only", "added"],
            ["diff", "--jobs", "1", "--format", "text", "--ignore", "ignored.txt", "--only", "added", scenario.ManifestPath, scenario.SitePath]
        ];

        foreach (var args in arguments)
        {
            var result = Run(args);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal("added    notes.txt\n1 change\n", result.Stdout);
            Assert.Equal(string.Empty, result.Stderr);
            AssertLf(result.Stdout);
        }
    }

    [Fact]
    public void EqualsSyntaxAndRepeatedKindsDoNotChangeSelection()
    {
        using var scenario = WorkedExample();
        string[][] arguments =
        [
            ["diff", $"--only=added", scenario.ManifestPath, scenario.SitePath],
            ["diff", "--only=added,added", scenario.ManifestPath, scenario.SitePath]
        ];

        foreach (var args in arguments)
        {
            var result = Run(args);

            Assert.Equal(1, result.ExitCode);
            Assert.Equal("added    notes.txt\n1 change\n", result.Stdout);
            Assert.Equal(string.Empty, result.Stderr);
        }
    }

    [Fact]
    public void RepeatedOnlyOptionUsesItsLastValue()
    {
        using var scenario = WorkedExample();

        var addedWins = Run(["diff", "--only", "removed", "--only", "added", scenario.ManifestPath, scenario.SitePath]);
        var removedWins = Run(["diff", "--only=added", scenario.ManifestPath, "--only=removed", scenario.SitePath]);

        Assert.Equal(1, addedWins.ExitCode);
        Assert.Equal("added    notes.txt\n1 change\n", addedWins.Stdout);
        Assert.Equal(string.Empty, addedWins.Stderr);
        Assert.Equal(1, removedWins.ExitCode);
        Assert.Equal("removed  gone.txt\n1 change\n", removedWins.Stdout);
        Assert.Equal(string.Empty, removedWins.Stderr);
    }

    [Fact]
    public void RepeatedOnlyOptionCanReplaceAnInvalidEarlierValue()
    {
        using var scenario = WorkedExample();

        var result = Run(["diff", "--only", "add", "--only", "added", scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("added    notes.txt\n1 change\n", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
    }

    [Fact]
    public void RepeatedOnlyOptionValidatesTheInvalidFinalValue()
    {
        using var scenario = WorkedExample();

        var result = Run(["diff", "--only", "added", "--only", "add", scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(string.Empty, result.Stdout);
        Assert.Equal("dirdrift: invalid --only \"add\"\n", result.Stderr);
    }

    [Theory]
    [InlineData("VALUE")]
    [InlineData("Added")]
    [InlineData("add")]
    [InlineData("")]
    [InlineData("added,")]
    [InlineData(",added")]
    [InlineData(" ")]
    [InlineData("added, removed")]
    public void InvalidOnlyValuesUseExactErrorStreams(string value)
    {
        AssertRun(
            ["diff", "--only", value],
            2,
            string.Empty,
            $"dirdrift: invalid --only \"{value}\"\n");
    }

    [Fact]
    public void OnlyRequiresASeparateValueAndRejectsDashPrefixedNextArgument()
    {
        AssertRun(
            ["diff", "--only"],
            2,
            string.Empty,
            "dirdrift: --only requires a value\n");
        AssertRun(
            ["diff", "--only", "-added"],
            2,
            string.Empty,
            "dirdrift: --only requires a value\n");
    }

    [Fact]
    public void SnapshotRejectsBothOnlyOptionFormsAsUnknownOptions()
    {
        using var scenario = WorkedExample();

        AssertRun(
            ["snapshot", "--only", "added", scenario.SitePath],
            2,
            string.Empty,
            "dirdrift: unknown option \"--only\"\n");
        AssertRun(
            ["snapshot", "--only=added", scenario.SitePath],
            2,
            string.Empty,
            "dirdrift: unknown option \"--only=added\"\n");
    }

    [Fact]
    public void OptionValidationPreservesPrecedenceAroundOnly()
    {
        AssertRun(
            ["diff", "--jobs=0", "--format=xml", "--ignore=", "--only=bad", "--sizes"],
            2,
            string.Empty,
            "dirdrift: --jobs must be an integer from 1 through 32\n");
        AssertRun(
            ["diff", "--format=xml", "--ignore=", "--only=bad", "--sizes"],
            2,
            string.Empty,
            "dirdrift: invalid --format \"xml\"\n");
        AssertRun(
            ["diff", "--ignore=", "--only=bad", "--sizes"],
            2,
            string.Empty,
            "dirdrift: invalid --ignore \"\"\n");
        AssertRun(
            ["diff", "--only=bad", "--format=json", "--sizes"],
            2,
            string.Empty,
            "dirdrift: invalid --only \"bad\"\n");
        AssertRun(
            ["diff", "--only=added", "--format=json", "--sizes"],
            2,
            string.Empty,
            "dirdrift: --sizes needs --format text\n");
    }

    [Theory]
    [InlineData(
        "renamed",
        "{\"renamed\":[{\"from\":\"docs/old.md\",\"to\":\"docs/new.md\"}],\"modified\":[],\"added\":[],\"removed\":[],\"changes\":1}\n")]
    [InlineData(
        "modified",
        "{\"renamed\":[],\"modified\":[\"src/app.cs\"],\"added\":[],\"removed\":[],\"changes\":1}\n")]
    [InlineData(
        "added",
        "{\"renamed\":[],\"modified\":[],\"added\":[\"notes.txt\"],\"removed\":[],\"changes\":1}\n")]
    [InlineData(
        "removed",
        "{\"renamed\":[],\"modified\":[],\"added\":[],\"removed\":[\"gone.txt\"],\"changes\":1}\n")]
    public void JsonReportKeepsEveryListAndOnlyPopulatesTheSelectedKind(string kind, string expected)
    {
        using var scenario = WorkedExample();

        var result = Run(["diff", "--format", "json", "--only", kind, scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(expected, result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        AssertLf(result.Stdout);
    }

    [Fact]
    public void SizesTotalsCountOnlySelectedChanges()
    {
        using var scenario = WorkedExample();

        var result = Run(["diff", "--sizes", "--only", "modified,added", scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(
            "modified src/app.cs (4 -> 7 B, +3)\n" +
            "added    notes.txt (4 B)\n" +
            "2 changes, net +7 B\n",
            result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        AssertLf(result.Stdout);
    }

    [Fact]
    public void AddedOnlyScenarioCanSelectTheAddedPathOrShowNoDrift()
    {
        using var scenario = new Scenario([], [("extra.txt", "x\n")]);

        var added = Run(["diff", "--only", "added", scenario.ManifestPath, scenario.SitePath]);
        Assert.Equal(1, added.ExitCode);
        Assert.Equal("added    extra.txt\n1 change\n", added.Stdout);
        Assert.Equal(string.Empty, added.Stderr);

        AssertNoSelectedDrift(scenario, "removed,modified,renamed");
    }

    [Fact]
    public void RenamePairingOccursBeforeSelectionAndOnlyShowsTheRenameKind()
    {
        using var scenario = new Scenario([("old.txt", "hello\n")], [("new.txt", "hello\n")]);

        var renamed = Run(["diff", "--only", "renamed", scenario.ManifestPath, scenario.SitePath]);
        Assert.Equal(1, renamed.ExitCode);
        Assert.Equal("renamed  old.txt -> new.txt\n1 change\n", renamed.Stdout);
        Assert.Equal(string.Empty, renamed.Stderr);

        AssertNoSelectedDrift(scenario, "added");
        AssertNoSelectedDrift(scenario, "removed");
    }

    [Theory]
    [InlineData("added")]
    [InlineData("removed")]
    [InlineData("modified")]
    [InlineData("renamed")]
    public void UnchangedScenarioHasNoDriftForEveryKindAndOutputMode(string kind)
    {
        using var scenario = new Scenario([("same.txt", "x\n")], [("same.txt", "x\n")]);

        var text = Run(["diff", "--only", kind, scenario.ManifestPath, scenario.SitePath]);
        Assert.Equal(0, text.ExitCode);
        Assert.Equal("no drift\n", text.Stdout);
        Assert.Equal(string.Empty, text.Stderr);
        AssertLf(text.Stdout);

        var sizes = Run(["diff", "--sizes", "--only", kind, scenario.ManifestPath, scenario.SitePath]);
        Assert.Equal(0, sizes.ExitCode);
        Assert.Equal("no drift\n", sizes.Stdout);
        Assert.Equal(string.Empty, sizes.Stderr);
        AssertLf(sizes.Stdout);

        var json = Run(["diff", "--format", "json", "--only", kind, scenario.ManifestPath, scenario.SitePath]);
        Assert.Equal(0, json.ExitCode);
        Assert.Equal("{\"renamed\":[],\"modified\":[],\"added\":[],\"removed\":[],\"changes\":0}\n", json.Stdout);
        Assert.Equal(string.Empty, json.Stderr);
        AssertLf(json.Stdout);
    }

    [Fact]
    public void SelectedZeroChangesPrintNoDriftWithSizesAndEmptyJsonLists()
    {
        using var scenario = new Scenario([], [("extra.txt", "x\n")]);

        AssertNoSelectedDrift(scenario, "removed,modified,renamed");

        var sizes = Run(["diff", "--sizes", "--only", "removed,modified,renamed", scenario.ManifestPath, scenario.SitePath]);
        Assert.Equal(0, sizes.ExitCode);
        Assert.Equal("no drift\n", sizes.Stdout);
        Assert.Equal(string.Empty, sizes.Stderr);

        var json = Run(["diff", "--format", "json", "--only", "removed,modified,renamed", scenario.ManifestPath, scenario.SitePath]);
        Assert.Equal(0, json.ExitCode);
        Assert.Equal("{\"renamed\":[],\"modified\":[],\"added\":[],\"removed\":[],\"changes\":0}\n", json.Stdout);
        Assert.Equal(string.Empty, json.Stderr);
        AssertLf(json.Stdout);
    }

    [Fact]
    public void HelpAndReadmeDocumentTheOnlyOptionAndExactExample()
    {
        var help = Run(["--help"]);
        Assert.Equal(0, help.ExitCode);
        Assert.Equal(
            "usage:\n" +
            "  dirdrift snapshot [--out FILE] [--ignore PATTERN]... [--jobs N] [--summary] DIR\n" +
            "  dirdrift diff [--ignore PATTERN]... [--jobs N] [--format text|json] [--sizes] [--only KINDS] MANIFEST DIR\n",
            help.Stdout);
        Assert.Equal(string.Empty, help.Stderr);
        AssertLf(help.Stdout);

        var repositoryRoot = FindRepositoryRoot();
        var readme = NormalizeNewlines(File.ReadAllText(Path.Combine(repositoryRoot, "README.md")));
        Assert.Contains(
            "  dirdrift diff [--ignore PATTERN]... [--jobs N] [--format text|json] [--sizes] [--only KINDS] MANIFEST DIR",
            readme);
        var sectionStart = readme.IndexOf(SelectingKindsReadmeSection, StringComparison.Ordinal);
        Assert.True(sectionStart >= 0, "The complete selected-kinds section must match the requested text.");
        Assert.Equal(
            sectionStart + SelectingKindsReadmeSection.Length,
            readme.IndexOf("## Snapshot summary", sectionStart, StringComparison.Ordinal));
    }

    private static Scenario WorkedExample() => new(WorkedExampleBefore, WorkedExampleAfter);

    private static void AssertNoSelectedDrift(Scenario scenario, string kinds)
    {
        var result = Run(["diff", "--only", kinds, scenario.ManifestPath, scenario.SitePath]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("no drift\n", result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        AssertLf(result.Stdout);
    }

    private static ScenarioResult Run(string[] args)
    {
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        var exitCode = Program.Run(args, stdout, stderr);
        return new ScenarioResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private static void AssertRun(string[] args, int exitCode, string stdout, string stderr)
    {
        var result = Run(args);

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(stdout, result.Stdout);
        Assert.Equal(stderr, result.Stderr);
    }

    private static string WriteManifest(string root, (string Path, string Contents)[] files)
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

    private sealed record ScenarioResult(int ExitCode, string Stdout, string Stderr);

    private sealed class Scenario : IDisposable
    {
        private readonly TemporaryDirectory temporary = new();

        public Scenario(
            (string Path, string Contents)[] manifestFiles,
            (string Path, string Contents)[] currentFiles)
        {
            SitePath = Path.Combine(temporary.Path, "site");
            Directory.CreateDirectory(SitePath);
            ManifestPath = WriteManifest(temporary.Path, manifestFiles);

            foreach (var (path, contents) in currentFiles)
            {
                WriteFile(SitePath, path, contents);
            }
        }

        public string SitePath { get; }

        public string ManifestPath { get; }

        public void Dispose() => temporary.Dispose();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dirdrift-only-tests-{Guid.NewGuid():N}");
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
