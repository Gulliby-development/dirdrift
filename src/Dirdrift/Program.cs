using System.Globalization;
using System.Numerics;
using System.Security;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Dirdrift.Core;

public static class Program
{
    private const string Usage =
        "usage:\n" +
        "  dirdrift snapshot [--out FILE] [--ignore PATTERN]... [--jobs N] DIR\n" +
        "  dirdrift diff [--ignore PATTERN]... [--jobs N] [--format text|json] [--sizes] MANIFEST DIR";

    public static int Main(string[] args)
    {
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
        {
            NewLine = "\n"
        };
        var stderr = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false))
        {
            NewLine = "\n"
        };

        int exitCode;
        try
        {
            exitCode = Run(args, stdout, stderr);
        }
        finally
        {
            stdout.Flush();
            stderr.Flush();
        }

        return exitCode;
    }

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args.Length == 1 && args[0] == "--help")
        {
            WriteLineLf(stdout, Usage);
            return 0;
        }

        if (args.Length == 0)
        {
            WriteLineLf(stderr, Usage);
            return 2;
        }

        var command = args[0];
        var isSnapshot = command == "snapshot";
        var isDiff = command == "diff";
        if (!isSnapshot && !isDiff)
        {
            return WriteError(stderr, $"dirdrift: unknown command \"{command}\"");
        }

        var options = ParseOptions(args, isSnapshot, out var parseError);
        if (parseError is not null)
        {
            return WriteError(stderr, parseError);
        }

        if (!TryValidateOptions(options, out var validationError))
        {
            return WriteError(stderr, validationError!);
        }

        var expectedPositionals = isSnapshot ? 1 : 2;
        if (options.Positionals.Count != expectedPositionals)
        {
            var expectedUsage = isSnapshot
                ? "dirdrift: usage: dirdrift snapshot [options] DIR"
                : "dirdrift: usage: dirdrift diff [options] MANIFEST DIR";
            return WriteError(stderr, expectedUsage);
        }

        var directory = options.Positionals[isSnapshot ? 0 : 1];
        if (!Directory.Exists(directory))
        {
            return WriteError(stderr, $"dirdrift: {directory}: not a directory");
        }

        try
        {
            Manifest? originalManifest = null;
            if (isDiff)
            {
                originalManifest = ManifestStore.Read(options.Positionals[0]);
            }

            var source = new DiskFileSource(directory);
            var scanner = new Scanner();
            var currentFiles = scanner.ScanAsync(source, options.IgnorePatterns, options.Jobs)
                .GetAwaiter()
                .GetResult();

            if (isSnapshot)
            {
                var manifest = new Manifest(Manifest.CurrentVersion, currentFiles);
                var serialized = ManifestStore.Serialize(manifest);
                if (options.OutputPath is null)
                {
                    stdout.Write(Encoding.UTF8.GetString(serialized));
                }
                else
                {
                    try
                    {
                        File.WriteAllBytes(options.OutputPath, serialized);
                    }
                    catch (Exception exception) when (IsFileSystemFailure(exception))
                    {
                        return WriteError(stderr, $"dirdrift: {options.OutputPath}: cannot write");
                    }
                }

                return 0;
            }

            var filteredManifestFiles = originalManifest!.Files
                .Where(file => !Glob.IsIgnored(file.Path, scanner.EffectiveIgnorePatterns))
                .ToArray();
            var diff = Differ.Compare(filteredManifestFiles, currentFiles);

            if (options.Format == "json")
            {
                WriteJsonDiff(stdout, diff);
            }
            else
            {
                var manifestSizes = options.Sizes
                    ? filteredManifestFiles.ToDictionary(file => file.Path, file => file.Size, StringComparer.Ordinal)
                    : null;
                var currentSizes = options.Sizes
                    ? currentFiles.ToDictionary(file => file.Path, file => file.Size, StringComparer.Ordinal)
                    : null;
                WriteTextDiff(stdout, diff, manifestSizes, currentSizes);
            }

            return diff.ChangeCount == 0 ? 0 : 1;
        }
        catch (ScanException exception)
        {
            return WriteError(stderr, $"dirdrift: {exception.Path}: cannot read");
        }
        catch (ManifestException exception)
        {
            return WriteError(stderr, $"dirdrift: {options.Positionals[0]}: invalid manifest: {exception.Reason}");
        }
    }

    private static ParsedOptions ParseOptions(string[] args, bool isSnapshot, out string? error)
    {
        var options = new ParsedOptions();
        error = null;

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            var equalsIndex = argument.StartsWith("--", StringComparison.Ordinal)
                ? argument.IndexOf('=')
                : -1;
            var optionName = equalsIndex >= 0 ? argument[..equalsIndex] : argument;
            var hasInlineValue = equalsIndex >= 0;
            var inlineValue = hasInlineValue ? argument[(equalsIndex + 1)..] : string.Empty;

            switch (optionName)
            {
                case "--out" when isSnapshot:
                    if (!TryReadValue(args, ref index, optionName, hasInlineValue, inlineValue, out var outputPath, out error))
                    {
                        return options;
                    }

                    options.OutputPath = outputPath;
                    break;

                case "--ignore":
                    if (!TryReadValue(args, ref index, optionName, hasInlineValue, inlineValue, out var ignorePattern, out error))
                    {
                        return options;
                    }

                    options.IgnorePatterns.Add(ignorePattern);
                    break;

                case "--jobs":
                    if (!TryReadValue(args, ref index, optionName, hasInlineValue, inlineValue, out var jobs, out error))
                    {
                        return options;
                    }

                    options.JobsValue = jobs;
                    break;

                case "--format" when !isSnapshot:
                    if (!TryReadValue(args, ref index, optionName, hasInlineValue, inlineValue, out var format, out error))
                    {
                        return options;
                    }

                    options.Format = format;
                    break;

                case "--sizes" when !isSnapshot && !hasInlineValue:
                    options.Sizes = true;
                    break;

                default:
                    if (argument.StartsWith("-", StringComparison.Ordinal))
                    {
                        error = $"dirdrift: unknown option \"{argument}\"";
                        return options;
                    }

                    options.Positionals.Add(argument);
                    break;
            }
        }

        return options;
    }

    private static bool TryReadValue(
        string[] args,
        ref int index,
        string optionName,
        bool hasInlineValue,
        string inlineValue,
        out string value,
        out string? error)
    {
        error = null;
        if (hasInlineValue)
        {
            value = inlineValue;
            return true;
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
        {
            value = string.Empty;
            error = $"dirdrift: {optionName} requires a value";
            return false;
        }

        value = args[++index];
        return true;
    }

    private static bool TryValidateOptions(ParsedOptions options, out string? error)
    {
        error = null;

        if (options.JobsValue is not null)
        {
            var value = options.JobsValue;
            if (value.Length == 0
                || value.Any(character => character is < '0' or > '9')
                || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var jobs)
                || jobs is < 1 or > 32)
            {
                error = "dirdrift: --jobs must be an integer from 1 through 32";
                return false;
            }

            options.Jobs = jobs;
        }

        if (options.Format is not ("text" or "json"))
        {
            error = $"dirdrift: invalid --format \"{options.Format}\"";
            return false;
        }

        foreach (var pattern in options.IgnorePatterns)
        {
            if (pattern.Length == 0 || pattern.Trim('/').Length == 0)
            {
                error = $"dirdrift: invalid --ignore \"{pattern}\"";
                return false;
            }
        }

        if (options.Sizes && options.Format == "json")
        {
            error = "dirdrift: --sizes needs --format text";
            return false;
        }

        return true;
    }

    private static void WriteTextDiff(
        TextWriter stdout,
        DiffResult diff,
        IReadOnlyDictionary<string, long>? manifestSizes,
        IReadOnlyDictionary<string, long>? currentSizes)
    {
        if (diff.ChangeCount == 0)
        {
            WriteLineLf(stdout, "no drift");
            return;
        }

        var lines = new List<string>();
        if (manifestSizes is null || currentSizes is null)
        {
            lines.AddRange(diff.Renamed.Select(rename => $"{"renamed".PadRight(9)}{rename.From} -> {rename.To}"));
            lines.AddRange(diff.Modified.Select(path => $"{"modified".PadRight(9)}{path}"));
            lines.AddRange(diff.Added.Select(path => $"{"added".PadRight(9)}{path}"));
            lines.AddRange(diff.Removed.Select(path => $"{"removed".PadRight(9)}{path}"));
            lines.Add($"{diff.ChangeCount} {(diff.ChangeCount == 1 ? "change" : "changes")}");
        }
        else
        {
            var netChange = BigInteger.Zero;
            foreach (var rename in diff.Renamed)
            {
                lines.Add($"{"renamed".PadRight(9)}{rename.From} -> {rename.To} ({FormatBytes(currentSizes[rename.To])} B)");
            }

            foreach (var path in diff.Modified)
            {
                var oldSize = manifestSizes[path];
                var newSize = currentSizes[path];
                var difference = new BigInteger(newSize) - oldSize;
                netChange += difference;
                lines.Add($"{"modified".PadRight(9)}{path} ({FormatBytes(oldSize)} -> {FormatBytes(newSize)} B, {FormatSignedBytes(difference)})");
            }

            foreach (var path in diff.Added)
            {
                var size = currentSizes[path];
                netChange += size;
                lines.Add($"{"added".PadRight(9)}{path} ({FormatBytes(size)} B)");
            }

            foreach (var path in diff.Removed)
            {
                var size = manifestSizes[path];
                netChange -= size;
                lines.Add($"{"removed".PadRight(9)}{path} ({FormatBytes(size)} B)");
            }

            lines.Add($"{diff.ChangeCount} {(diff.ChangeCount == 1 ? "change" : "changes")}, net {FormatSignedBytes(netChange)} B");
        }

        stdout.Write(string.Join('\n', lines));
        stdout.Write('\n');
    }

    private static string FormatBytes(long size) =>
        size.ToString(CultureInfo.InvariantCulture);

    private static string FormatSignedBytes(BigInteger value) =>
        $"{(value.Sign >= 0 ? "+" : string.Empty)}{value.ToString(CultureInfo.InvariantCulture)}";

    private static void WriteJsonDiff(TextWriter stdout, DiffResult diff)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("renamed");
            foreach (var rename in diff.Renamed)
            {
                writer.WriteStartObject();
                writer.WriteString("from", rename.From);
                writer.WriteString("to", rename.To);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteStartArray("modified");
            foreach (var path in diff.Modified)
            {
                writer.WriteStringValue(path);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("added");
            foreach (var path in diff.Added)
            {
                writer.WriteStringValue(path);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("removed");
            foreach (var path in diff.Removed)
            {
                writer.WriteStringValue(path);
            }

            writer.WriteEndArray();
            writer.WriteNumber("changes", diff.ChangeCount);
            writer.WriteEndObject();
            writer.Flush();
        }

        stdout.Write(Encoding.UTF8.GetString(buffer.ToArray()));
        stdout.Write('\n');
    }

    private static int WriteError(TextWriter stderr, string message)
    {
        WriteLineLf(stderr, message);
        return 2;
    }

    private static void WriteLineLf(TextWriter writer, string message)
    {
        writer.Write(message);
        writer.Write('\n');
    }

    private static bool IsFileSystemFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or SecurityException;

    private sealed class ParsedOptions
    {
        public List<string> Positionals { get; } = [];

        public List<string> IgnorePatterns { get; } = [];

        public string? OutputPath { get; set; }

        public string? JobsValue { get; set; }

        public int Jobs { get; set; } = 4;

        public string Format { get; set; } = "text";

        public bool Sizes { get; set; }
    }
}

