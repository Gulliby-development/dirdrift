using System.Text;
using System.Text.Json;
using Dirdrift.Core;

namespace Dirdrift.Tests;

public class ManifestDifferTests
{
    private const string ValidHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void Serialize_sorts_files_and_matches_the_canonical_example_bytes()
    {
        var manifest = new Manifest(
            Manifest.CurrentVersion,
            [
                new FileEntry("src/app.cs", 4, "2c8b08da5ce60398e1f19af0e5dccc744df274b826abe585eaba68c525434806"),
                new FileEntry("gone.txt", 4, "abc6fd595fc079d3114d4b71a4d84b1d1d0f79df1e70f8813212f2a65d8916df"),
                new FileEntry("docs/old.md", 6, "5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03"),
                new FileEntry("README.md", 2, "8e54b0ca18020275e4aef1ca0eb5e197e066c065c1864817652a8a39c55402cd")
            ]);

        string expected = string.Join(
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

        Assert.Equal(Encoding.UTF8.GetBytes(expected), ManifestStore.Serialize(manifest));
    }

    [Fact]
    public void Serialize_empty_manifest_uses_a_single_line_empty_files_array()
    {
        var manifest = new Manifest(Manifest.CurrentVersion, Array.Empty<FileEntry>());
        string expected = string.Join(
            "\n",
            [
                "{",
                "  \"version\": 1,",
                "  \"files\": []",
                "}",
                string.Empty
            ]);

        Assert.Equal(Encoding.UTF8.GetBytes(expected), ManifestStore.Serialize(manifest));
    }

    [Fact]
    public void Parse_accepts_bom_crlf_unknown_properties_and_unsorted_entries()
    {
        string json = string.Join(
            "\r\n",
            [
                "{",
                "  \"extra\": true,",
                "  \"version\": 1,",
                "  \"files\": [",
                $"    {{ \"path\": \"z.txt\", \"size\": 9223372036854775807, \"sha256\": \"{ValidHash}\", \"ignored\": 7 }},",
                $"    {{ \"path\": \"a.txt\", \"size\": 0, \"sha256\": \"{ValidHash}\" }}",
                "  ]",
                "}"
            ]);
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(json)];

        Manifest parsed = ManifestStore.Parse(bytes);

        Assert.Equal(Manifest.CurrentVersion, parsed.Version);
        Assert.Collection(
            parsed.Files,
            file =>
            {
                Assert.Equal("z.txt", file.Path);
                Assert.Equal(long.MaxValue, file.Size);
            },
            file =>
            {
                Assert.Equal("a.txt", file.Path);
                Assert.Equal(0, file.Size);
            });
    }

    [Theory]
    [InlineData("{")]
    [InlineData("// comment\n{\"version\":1,\"files\":[]}")]
    [InlineData("{\"version\":1,\"files\":[],}")]
    public void Parse_reports_invalid_json_for_non_default_json_syntax(string json)
    {
        AssertManifestFailure("not valid JSON", Encoding.UTF8.GetBytes(json));
    }

    [Fact]
    public void Parse_reports_invalid_json_for_invalid_utf8()
    {
        AssertManifestFailure("not valid JSON", [0xFF, 0xFE, 0x7B]);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"files\":[]}")]
    [InlineData("{\"version\":\"1\",\"files\":[]}")]
    [InlineData("{\"version\":2,\"files\":[]}")]
    public void Parse_checks_supported_version_before_files(string json)
    {
        AssertManifestFailure("unsupported version", Encoding.UTF8.GetBytes(json));
    }

    [Theory]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"files\":null}")]
    public void Parse_requires_files_array(string json)
    {
        AssertManifestFailure("missing files", Encoding.UTF8.GetBytes(json));
    }

    [Theory]
    [MemberData(nameof(InvalidEntryCases))]
    public void Parse_reports_bad_entry_for_each_invalid_field_shape(string entry)
    {
        AssertManifestFailure("bad entry 0", Encoding.UTF8.GetBytes(ManifestWithEntries(entry)));
    }

    public static IEnumerable<object[]> InvalidEntryCases
    {
        get
        {
            yield return new object[] { "null" };
            yield return new object[] { "{\"size\":0,\"sha256\":\"" + ValidHash + "\"}" };
            yield return new object[] { ValidEntry(pathJson: "null") };
            yield return new object[] { ValidEntry(pathJson: "7") };
            yield return new object[] { ValidEntry(pathJson: "\"\"") };
            yield return new object[] { ValidEntry(pathJson: JsonSerializer.Serialize("/absolute")) };
            yield return new object[] { ValidEntry(pathJson: JsonSerializer.Serialize("a\\b")) };
            yield return new object[] { ValidEntry(pathJson: JsonSerializer.Serialize("a//b")) };
            yield return new object[] { ValidEntry(pathJson: JsonSerializer.Serialize("a/./b")) };
            yield return new object[] { ValidEntry(pathJson: JsonSerializer.Serialize("a/../b")) };
            yield return new object[] { "{\"path\":\"file\",\"sha256\":\"" + ValidHash + "\"}" };
            yield return new object[] { ValidEntry(sizeJson: "\"1\"") };
            yield return new object[] { ValidEntry(sizeJson: "1.0") };
            yield return new object[] { ValidEntry(sizeJson: "-1") };
            yield return new object[] { ValidEntry(sizeJson: "9223372036854775808") };
            yield return new object[] { "{\"path\":\"file\",\"size\":0}" };
            yield return new object[] { ValidEntry(hashJson: "null") };
            yield return new object[] { ValidEntry(hashJson: "7") };
            yield return new object[] { ValidEntry(hashJson: "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"") };
            yield return new object[] { ValidEntry(hashJson: "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaA\"") };
            yield return new object[] { ValidEntry(hashJson: "\"gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg\"") };
        }
    }

    [Fact]
    public void Parse_reports_the_zero_based_index_of_a_bad_entry()
    {
        string json = ManifestWithEntries(ValidEntry(), "{}");

        AssertManifestFailure("bad entry 1", Encoding.UTF8.GetBytes(json));
    }

    [Fact]
    public void Parse_reports_the_first_duplicate_path_in_file_order_using_ordinal_comparison()
    {
        string json = ManifestWithEntries(
            ValidEntry(pathJson: "\"Z\""),
            ValidEntry(pathJson: "\"x\""),
            ValidEntry(pathJson: "\"A\""),
            ValidEntry(pathJson: "\"x\""),
            ValidEntry(pathJson: "\"Z\""));

        AssertManifestFailure("duplicate path \"x\"", Encoding.UTF8.GetBytes(json));
    }

    [Fact]
    public void Read_reports_cannot_read_for_a_missing_manifest()
    {
        string path = Path.Combine(Path.GetTempPath(), "dirdrift-" + Guid.NewGuid().ToString("N"), "missing.json");

        ManifestException exception = Assert.Throws<ManifestException>(() => ManifestStore.Read(path));

        Assert.Equal("cannot read", exception.Reason);
    }

    [Fact]
    public void Compare_pairs_duplicate_hashes_by_path_and_leaves_unpaired_and_empty_files_alone()
    {
        string hashOne = new('a', 64);
        string hashTwo = new('b', 64);
        string emptyHash = new('c', 64);
        FileEntry[] original =
        [
            new("old/c.txt", 1, hashOne),
            new("old/a.txt", 1, hashOne),
            new("old/b.txt", 1, hashOne),
            new("prior/2.txt", 1, hashTwo),
            new("prior/1.txt", 1, hashTwo),
            new("empty/old.txt", 0, emptyHash)
        ];
        FileEntry[] current =
        [
            new("new/2.txt", 1, hashTwo),
            new("new/extra.txt", 1, hashTwo),
            new("new/b.txt", 1, hashOne),
            new("new/a.txt", 1, hashOne),
            new("empty/new.txt", 0, emptyHash),
            new("new/1.txt", 1, hashTwo)
        ];

        DiffResult result = Differ.Compare(original, current);

        Assert.Equal(
            [
                new FileRename("old/a.txt", "new/a.txt"),
                new FileRename("old/b.txt", "new/b.txt"),
                new FileRename("prior/1.txt", "new/1.txt"),
                new FileRename("prior/2.txt", "new/2.txt")
            ],
            result.Renamed);
        Assert.Equal(["empty/new.txt", "new/extra.txt"], result.Added);
        Assert.Equal(["empty/old.txt", "old/c.txt"], result.Removed);
        Assert.Empty(result.Modified);
        Assert.Equal(8, result.ChangeCount);
    }

    [Fact]
    public void Compare_orders_each_group_and_counts_a_rename_as_one_change()
    {
        string sameHash = new('d', 64);
        FileEntry[] original =
        [
            new("b-modified.txt", 4, "old"),
            new("z-renamed.txt", 1, sameHash),
            new("a-removed.txt", 1, "gone"),
            new("A-removed.txt", 1, "gone"),
            new("A-modified.txt", 4, "old"),
            new("B-renamed.txt", 1, sameHash)
        ];
        FileEntry[] current =
        [
            new("b-modified.txt", 4, "new"),
            new("a-new.txt", 1, "content"),
            new("z-new.txt", 1, sameHash),
            new("A-modified.txt", 4, "new"),
            new("A-new.txt", 1, "content")
        ];

        DiffResult result = Differ.Compare(original, current);

        Assert.Equal([new FileRename("B-renamed.txt", "z-new.txt")], result.Renamed);
        Assert.Equal(["A-modified.txt", "b-modified.txt"], result.Modified);
        Assert.Equal(["A-new.txt", "a-new.txt"], result.Added);
        Assert.Equal(["A-removed.txt", "a-removed.txt", "z-renamed.txt"], result.Removed);
        Assert.Equal(8, result.ChangeCount);
    }

    private static string ValidEntry(
        string pathJson = "\"file\"",
        string sizeJson = "0",
        string hashJson = "\"" + ValidHash + "\"")
    {
        return $"{{\"path\":{pathJson},\"size\":{sizeJson},\"sha256\":{hashJson}}}";
    }

    private static string ManifestWithEntries(params string[] entries)
    {
        return $"{{\"version\":1,\"files\":[{string.Join(",", entries)}]}}";
    }

    private static void AssertManifestFailure(string reason, byte[] json)
    {
        ManifestException exception = Assert.Throws<ManifestException>(() => ManifestStore.Parse(json));

        Assert.Equal(reason, exception.Reason);
    }
}
