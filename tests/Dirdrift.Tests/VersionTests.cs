namespace Dirdrift.Tests;

public sealed class VersionTests
{
    private const string Usage =
        "usage:\n" +
        "  dirdrift snapshot [--out FILE] [--ignore PATTERN]... [--jobs N] [--summary] DIR\n" +
        "  dirdrift diff [--ignore PATTERN]... [--jobs N] [--format text|json] [--sizes] [--only KINDS] MANIFEST DIR\n";

    [Fact]
    public void StandaloneVersionWritesTheVersionAndReturnsSuccess()
    {
        var lfResult = Run(["--version"]);
        Assert.Equal(0, lfResult.ExitCode);
        Assert.Equal("dirdrift 0.2.0\n", lfResult.Stdout);
        Assert.Equal(string.Empty, lfResult.Stderr);

        var crlfResult = Run(["--version"], "\r\n");
        Assert.Equal(0, crlfResult.ExitCode);
        Assert.Equal("dirdrift 0.2.0\n", crlfResult.Stdout);
        Assert.Equal(string.Empty, crlfResult.Stderr);
    }

    [Fact]
    public void VersionWithAnotherCommandArgumentRemainsAnUnknownCommand()
    {
        AssertRun(
            ["--version", "snapshot"],
            2,
            string.Empty,
            "dirdrift: unknown command \"--version\"\n");
        AssertRun(
            ["--version", "x"],
            2,
            string.Empty,
            "dirdrift: unknown command \"--version\"\n");
    }

    [Fact]
    public void SnapshotRejectsVersionOptionInEveryArgumentPosition()
    {
        AssertRun(
            ["snapshot", "--version", "DIR"],
            2,
            string.Empty,
            "dirdrift: unknown option \"--version\"\n");
        AssertRun(
            ["snapshot", "DIR", "--version"],
            2,
            string.Empty,
            "dirdrift: unknown option \"--version\"\n");
    }

    [Fact]
    public void DiffRejectsVersionOptionInEveryArgumentPosition()
    {
        AssertRun(
            ["diff", "--version", "MANIFEST", "DIR"],
            2,
            string.Empty,
            "dirdrift: unknown option \"--version\"\n");
        AssertRun(
            ["diff", "MANIFEST", "DIR", "--version"],
            2,
            string.Empty,
            "dirdrift: unknown option \"--version\"\n");
    }

    [Fact]
    public void HelpOutputRemainsUnchangedAndDoesNotMentionVersion()
    {
        var result = Run(["--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Usage, result.Stdout);
        Assert.Equal(string.Empty, result.Stderr);
        Assert.DoesNotContain("--version", result.Stdout);
    }

    [Fact]
    public void ExecutableProjectDefinesTheVersionExactlyOnce()
    {
        var projectPath = Path.Combine(FindRepositoryRoot(), "src", "Dirdrift", "Dirdrift.csproj");
        var project = File.ReadAllText(projectPath);
        var occurrences = project.Split("<Version>0.2.0</Version>", StringSplitOptions.None).Length - 1;

        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void ReadmeDocumentsTheVersionCommandBeforeIgnorePatterns()
    {
        var readmePath = Path.Combine(FindRepositoryRoot(), "README.md");
        var readme = NormalizeNewlines(File.ReadAllText(readmePath));
        var expectedSection = string.Join(
            "\n",
            [
                "## Version",
                string.Empty,
                "Pass `--version` as the only argument to print the program name and its version on standard output and exit with 0:",
                string.Empty,
                "```sh",
                "dirdrift --version",
                "```",
                string.Empty,
                "prints:",
                string.Empty,
                "```text",
                "dirdrift 0.2.0",
                "```",
                string.Empty,
                "The number is the `<Version>` of `src/Dirdrift/Dirdrift.csproj`. The usage text printed by `--help` is unchanged."
            ]);

        Assert.Contains($"{expectedSection}\n\n## Invisible characters in paths\n\n", readme);
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(string[] args, string newLine = "\n")
    {
        using var stdout = new StringWriter { NewLine = newLine };
        using var stderr = new StringWriter { NewLine = newLine };
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
}
