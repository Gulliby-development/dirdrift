using System.Buffers;
using System.Security;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Dirdrift.Core;

/// <summary>
/// Reads and writes version-1 manifests.
/// </summary>
public static class ManifestStore
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Serializes a manifest using the canonical snapshot JSON representation.
    /// </summary>
    /// <param name="manifest">The manifest to serialize.</param>
    /// <returns>UTF-8 JSON bytes with one final line feed.</returns>
    public static byte[] Serialize(Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(manifest.Files);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = true,
                NewLine = "\n",
                IndentSize = 2
            }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", manifest.Version);
            writer.WriteStartArray("files");

            foreach (var file in manifest.Files.OrderBy(file => file.Path, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", file.Path);
                writer.WriteNumber("size", file.Size);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        var serialized = new byte[buffer.WrittenCount + 1];
        buffer.WrittenSpan.CopyTo(serialized);
        serialized[^1] = (byte)'\n';
        return serialized;
    }

    /// <summary>
    /// Writes the canonical JSON representation to a stream.
    /// </summary>
    /// <param name="destination">The destination stream.</param>
    /// <param name="manifest">The manifest to write.</param>
    public static void Write(Stream destination, Manifest manifest)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Write(Serialize(manifest));
    }

    /// <summary>
    /// Reads and validates a manifest file.
    /// </summary>
    /// <param name="path">The manifest path.</param>
    /// <returns>The validated manifest.</returns>
    /// <exception cref="ManifestException">The file cannot be read or is invalid.</exception>
    public static Manifest Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or SecurityException)
        {
            throw new ManifestException("cannot read");
        }

        return Parse(bytes);
    }

    /// <summary>
    /// Parses and validates manifest JSON bytes.
    /// </summary>
    /// <param name="utf8Json">The UTF-8 JSON bytes.</param>
    /// <returns>The validated manifest.</returns>
    /// <exception cref="ManifestException">The input is invalid.</exception>
    public static Manifest Parse(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.Span.StartsWith(Utf8Bom))
        {
            utf8Json = utf8Json[Utf8Bom.Length..];
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json);
        }
        catch (JsonException)
        {
            throw new ManifestException("not valid JSON");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out var version)
                || version != Manifest.CurrentVersion)
            {
                throw new ManifestException("unsupported version");
            }

            if (!root.TryGetProperty("files", out var filesElement)
                || filesElement.ValueKind != JsonValueKind.Array)
            {
                throw new ManifestException("missing files");
            }

            var files = new List<FileEntry>();
            var index = 0;
            foreach (var entryElement in filesElement.EnumerateArray())
            {
                if (!TryReadEntry(entryElement, out var entry))
                {
                    throw new ManifestException($"bad entry {index}");
                }

                files.Add(entry);
                index++;
            }

            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in files)
            {
                if (!seenPaths.Add(file.Path))
                {
                    throw new ManifestException($"duplicate path \"{file.Path}\"");
                }
            }

            return new Manifest(version, files);
        }
    }

    private static bool TryReadEntry(JsonElement element, out FileEntry entry)
    {
        entry = null!;

        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("path", out var pathElement)
            || pathElement.ValueKind != JsonValueKind.String
            || !element.TryGetProperty("size", out var sizeElement)
            || sizeElement.ValueKind != JsonValueKind.Number
            || !sizeElement.TryGetInt64(out var size)
            || size < 0
            || !element.TryGetProperty("sha256", out var hashElement)
            || hashElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var path = pathElement.GetString();
        var sha256 = hashElement.GetString();
        if (!IsValidPath(path)
            || !IsValidSha256(sha256))
        {
            return false;
        }

        entry = new FileEntry(path!, size, sha256!);
        return true;
    }

    private static bool IsValidPath(string? path)
    {
        if (string.IsNullOrEmpty(path)
            || path.StartsWith("/", StringComparison.Ordinal)
            || path.Contains('\\'))
        {
            return false;
        }

        return path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool IsValidSha256(string? sha256)
    {
        if (sha256 is null || sha256.Length != 64)
        {
            return false;
        }

        return sha256.All(character =>
            character is >= '0' and <= '9'
            or >= 'a' and <= 'f');
    }
}
