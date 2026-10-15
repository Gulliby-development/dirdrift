<p align="center"><img src="docs/logo.svg" width="96" alt="" /></p>

# dirdrift

[![CI](https://github.com/Gulliby-development/dirdrift/actions/workflows/ci.yml/badge.svg)](https://github.com/Gulliby-development/dirdrift/actions/workflows/ci.yml) [![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

dirdrift records the relative paths, byte sizes, and SHA-256 hashes of files in a directory, then compares a later scan with that snapshot. It is for people checking deployed folders, backups, and build outputs for additions, removals, modifications, or content-based renames.

## Install

Install the .NET 10 SDK, then build a Release version from the repository root:

```sh
dotnet build -c Release
```

The executable is written to `src/Dirdrift/bin/Release/net10.0/dirdrift` (with the platform's executable suffix where applicable). You can also run it through the generated assembly:

```sh
dotnet src/Dirdrift/bin/Release/net10.0/dirdrift.dll snapshot ./site
```

## Usage

```text
usage:
  dirdrift snapshot [--out FILE] [--ignore PATTERN]... [--jobs N] [--summary] DIR
  dirdrift diff [--ignore PATTERN]... [--jobs N] [--format text|json] [--sizes] [--only KINDS] MANIFEST DIR
```

Options can appear anywhere in the command arguments. Both `--option VALUE` and `--option=VALUE` are accepted. `--jobs` is between 1 and 32 (default 4); `--format` is `text` (default) or `json`. A snapshot is printed to standard output unless `--out` names a file.

For example, save a snapshot of a deployed folder:

```sh
dirdrift snapshot --out before.json ./site
```

After deployment, compare the folder with that manifest:

```sh
dirdrift diff before.json ./site
```

A complete example manifest is:

```json
{
  "version": 1,
  "files": [
    {
      "path": "README.md",
      "size": 2,
      "sha256": "8e54b0ca18020275e4aef1ca0eb5e197e066c065c1864817652a8a39c55402cd"
    },
    {
      "path": "docs/old.md",
      "size": 6,
      "sha256": "5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03"
    },
    {
      "path": "gone.txt",
      "size": 4,
      "sha256": "abc6fd595fc079d3114d4b71a4d84b1d1d0f79df1e70f8813212f2a65d8916df"
    },
    {
      "path": "src/app.cs",
      "size": 4,
      "sha256": "2c8b08da5ce60398e1f19af0e5dccc744df274b826abe585eaba68c525434806"
    }
  ]
}
```

If the later folder contains the same README, a renamed `docs/new.md`, a changed `src/app.cs`, and a new `notes.txt`, the text diff is:

```text
renamed  docs/old.md -> docs/new.md
modified src/app.cs
added    notes.txt
removed  gone.txt
4 changes
```

Pass `--format json` to produce a compact, single-line JSON result instead.

## Sizes in the text diff

Pass `--sizes` to include byte counts in the text report. In the earlier folder comparison, the updated `src/app.cs` has 7 bytes and the new `notes.txt` has 4 bytes; the output is:

```text
renamed  docs/old.md -> docs/new.md (6 B)
modified src/app.cs (4 -> 7 B, +3)
added    notes.txt (4 B)
removed  gone.txt (4 B)
4 changes, net +3 B
```

Each line shows the byte size of a renamed, added, or removed file, or the old and current sizes plus their signed difference for a modified file. The final `net` total adds new-file sizes and modified-file size differences, then subtracts removed-file sizes; renames contribute zero. The count uses `change` for one change and `changes` otherwise. `--sizes` is available only for `diff` and cannot be combined with `--format json`. Without `--sizes`, the text output is unchanged.

## Selecting kinds of change

Pass `--only KINDS` to `diff` to report only some kinds of change. `KINDS` is a comma-separated list of `added`, `removed`, `modified` and `renamed` (lowercase; a kind may be repeated). The other kinds are left out of the text report, the JSON lists, the `--sizes` figures and the change count, and the exit code is 1 only when a selected kind has a change; when none has, the output is `no drift` and the exit code is 0. Renames are paired before the selection, so `--only added` does not list the new path of a renamed file. `--only` is available only for `diff`. For the folder of the earlier example:

```sh
dirdrift diff --only added,removed before.json ./site
```

prints:

```text
added    notes.txt
removed  gone.txt
2 changes
```

## Snapshot summary

Pass `--summary` to `snapshot` to print one line on standard error with the number of files in the snapshot and their total size in bytes, after the manifest has been written. The manifest itself is unchanged and still goes to standard output, or to the file named by `--out`. For the folder of the earlier example:

```sh
dirdrift snapshot --summary --out before.json ./site
```

the line on standard error is:

```text
4 files, 16 bytes
```

The wording is `1 file` for one file and `1 byte` for one byte, `0 files, 0 bytes` for an empty folder. Only files that the ignore patterns keep are counted. `--summary` is available only for `snapshot`; nothing is printed when the snapshot fails.

## Ignore patterns

Use `--ignore PATTERN` one or more times, or put patterns in the root `.dirdriftignore` file. Blank lines and lines beginning with `#` are ignored. Patterns are case-sensitive; leading and trailing slashes control anchoring and directory-only matching. A one-segment unanchored pattern matches names at any depth. Patterns with multiple segments, and anchored patterns, match from the root. `*` matches any number of characters other than `/`, and `?` matches one character. A segment exactly equal to `**` matches whole path segments; for example, `src/**/gen` matches `gen` directories below `src`, and `a/**` matches descendants of `a` but not `a` itself.

An ignored directory also excludes its contents. The root `.dirdriftignore` is itself a normal file unless a pattern ignores it. During a diff, the same effective ignore rules filter both the current scan and manifest entries.

Examples, one pattern per line in `.dirdriftignore` (or one `--ignore` argument each):

```text
*.log
build/
/docs
docs/*.md
```

- `*.log` matches any file or directory with the `.log` suffix, at any depth.
- `build/` matches directories named `build` at any depth.
- `/docs` matches only the root path named `docs`.
- `docs/*.md` matches Markdown files directly under `docs`.

## Manifest format

A version 1 manifest is UTF-8 JSON with a top-level `version` number and a `files` array. Each entry has a slash-separated relative `path`, its byte `size`, and a 64-character lowercase hexadecimal SHA-256 `sha256`. Entries are sorted by path using ordinal, case-sensitive ordering. Hashes cover the raw file bytes. The snapshot writer uses indented JSON and a final LF; the diff JSON format is compact.

Diffs classify same-path content changes as modified, unmatched paths as added or removed, and pair same-content nonempty removed/added files as renames. Rename candidates are paired by ordinal path order.

## Exit codes

- `0`: snapshot succeeded, or diff found no drift.
- `1`: diff found one or more changes.
- `2`: command, argument, filesystem, or manifest error.

Errors are written to standard error; successful snapshots without `--out` write their manifest to standard output.

## Development

From the repository root, run the build, formatting check, and tests:

```sh
dotnet build
dotnet format --verify-no-changes
dotnet test
```

## License

MIT, see [LICENSE](LICENSE).

---

Built with BAKED, an open-source coding agent (bench loop round 10, BAKED 2c60d0bb).
