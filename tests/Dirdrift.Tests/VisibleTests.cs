using Dirdrift.Core;

namespace Dirdrift.Tests;

public sealed class VisibleTests
{
    private const string Sha256 = "5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03";

    [Theory]
    [InlineData("src/app.cs", "src/app.cs")]
    [InlineData("docs/old.md", "docs/old.md")]
    [InlineData("a\u200Bb.txt", "a<U+200B>b.txt")]
    [InlineData("\uFEFFbom.txt", "<U+FEFF>bom.txt")]
    [InlineData("price\u00A0list.txt", "price<U+00A0>list.txt")]
    [InlineData("a\u0085b", "a<U+0085>b")]
    [InlineData("a\u2028b\u2029c", "a<U+2028>b<U+2029>c")]
    [InlineData("a\u3000b", "a<U+3000>b")]
    [InlineData("a\rb", "a<U+000D>b")]
    [InlineData("a\tb", "a<U+0009>b")]
    [InlineData("a\u007Fb", "a<U+007F>b")]
    [InlineData("a\u00ADb", "a<U+00AD>b")]
    [InlineData("a\u2003b", "a<U+2003>b")]
    [InlineData("a\u202Eb", "a<U+202E>b")]
    [InlineData("a\u2060\u2064b", "a<U+2060><U+2064>b")]
    [InlineData("tail\u200B", "tail<U+200B>")]
    [InlineData("\u200B\u200B", "<U+200B><U+200B>")]
    [InlineData("x¡y", "x¡y")]
    [InlineData("x~\u009Fy", "x~<U+009F>y")]
    [InlineData("x\u2027\u2030y", "x\u2027\u2030y")]
    [InlineData("x\u205E\u2065y", "x\u205E\u2065y")]
    [InlineData("x\u2010、y", "x\u2010、y")]
    [InlineData("x﻾＀y", "x﻾＀y")]
    [InlineData("café/😀.txt", "café/😀.txt")]
    [InlineData("a<U+200B>b", "a<U+200B>b")]
    public void ShowSpellsOutOnlySpecifiedUtf16Units(string path, string expected)
    {
        Assert.Equal(expected, VisiblePath.Show(path));
    }

    [Fact]
    public void VisibleDiffSpellsOutEveryRemovedPath()
    {
        using var scenario = CreateScenario(
            [
                new FileEntry("a\u200Bb.txt", 6, Sha256),
                new FileEntry("docs/x\u2028y.md", 6, Sha256),
                new FileEntry("price\u00A0list.txt", 6, Sha256)
            ]);

        AssertRun(
            ["diff", "--visible", scenario.ManifestPath, scenario.SitePath],
            1,
            "removed  a<U+200B>b.txt\n" +
            "removed  docs/x<U+2028>y.md\n" +
            "removed  price<U+00A0>list.txt\n" +
            "3 changes\n",
            string.Empty);
    }

    [Fact]
    public void DefaultDiffPreservesInvisiblePathCharacters()
    {
        using var scenario = CreateScenario(
            [
                new FileEntry("a\u200Bb.txt", 6, Sha256),
                new FileEntry("docs/x\u2028y.md", 6, Sha256),
                new FileEntry("price\u00A0list.txt", 6, Sha256)
            ]);

        AssertRun(
            ["diff", scenario.ManifestPath, scenario.SitePath],
            1,
            "removed  a\u200Bb.txt\n" +
            "removed  docs/x\u2028y.md\n" +
            "removed  price\u00A0list.txt\n" +
            "3 changes\n",
            string.Empty);
    }

    [Fact]
    public void SizedVisibleDiffUsesRealPathsForSizeLookups()
    {
        using var scenario = CreateScenario([new FileEntry("a\u200Bb.txt", 6, Sha256)]);

        AssertRun(
            ["diff", "--sizes", "--visible", scenario.ManifestPath, scenario.SitePath],
            1,
            "removed  a<U+200B>b.txt (6 B)\n" +
            "1 change, net -6 B\n",
            string.Empty);
    }

    [Fact]
    public void VisibleDiffPreservesNoDriftOutput()
    {
        using var scenario = CreateScenario([]);

        AssertRun(
            ["diff", "--visible", scenario.ManifestPath, scenario.SitePath],
            0,
            "no drift\n",
            string.Empty);
    }

    [Fact]
    public void VisibleOptionRejectsJsonFormat()
    {
        AssertRun(
            ["diff", "--visible", "--format", "json", "before.json", "site"],
            2,
            string.Empty,
            "dirdrift: --visible needs --format text\n");
    }

    [Fact]
    public void VisibleOptionIsUnknownForSnapshotAndInlineValues()
    {
        AssertRun(
            ["snapshot", "--visible", "site"],
            2,
            string.Empty,
            "dirdrift: unknown option \"--visible\"\n");
        AssertRun(
            ["diff", "--visible=1", "before.json", "site"],
            2,
            string.Empty,
            "dirdrift: unknown option \"--visible=1\"\n");
    }

    private static Scenario CreateScenario(FileEntry[] entries)
    {
        var temporary = new TemporaryDirectory();
        var sitePath = Path.Combine(temporary.Path, "site");
        Directory.CreateDirectory(sitePath);

        var manifestPath = Path.Combine(temporary.Path, "before.json");
        File.WriteAllBytes(
            manifestPath,
            ManifestStore.Serialize(new Manifest(Manifest.CurrentVersion, entries)));

        return new Scenario(temporary, manifestPath, sitePath);
    }

    private static void AssertRun(string[] args, int exitCode, string stdout, string stderr)
    {
        using var actualStdout = new StringWriter { NewLine = "\n" };
        using var actualStderr = new StringWriter { NewLine = "\n" };

        var actualExitCode = Program.Run(args, actualStdout, actualStderr);

        Assert.Equal(exitCode, actualExitCode);
        Assert.Equal(stdout, actualStdout.ToString());
        Assert.Equal(stderr, actualStderr.ToString());
    }

    private sealed class Scenario : IDisposable
    {
        private readonly TemporaryDirectory temporary;

        public Scenario(TemporaryDirectory temporary, string manifestPath, string sitePath)
        {
            this.temporary = temporary;
            ManifestPath = manifestPath;
            SitePath = sitePath;
        }

        public string ManifestPath { get; }

        public string SitePath { get; }

        public void Dispose() => temporary.Dispose();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dirdrift-visible-tests-{Guid.NewGuid():N}");
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
