using System.Globalization;
using System.Text;

namespace Dirdrift.Core;

/// <summary>
/// Writes the invisible characters of a path as visible code point labels.
/// </summary>
public static class VisiblePath
{
    /// <summary>
    /// Replaces each invisible character of a path by <c>&lt;U+XXXX&gt;</c> (four uppercase hexadecimal digits).
    /// </summary>
    /// <param name="path">The relative path.</param>
    /// <returns>The path with every invisible character spelled out.</returns>
    public static string Show(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var builder = new StringBuilder(path.Length);
        foreach (var character in path)
        {
            if (IsInvisible(character))
            {
                builder.Append("<U+")
                    .Append(((int)character).ToString("X4", CultureInfo.InvariantCulture))
                    .Append('>');
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static bool IsInvisible(char character)
    {
        var code = (int)character;
        return code <= 0x1F
            || code is >= 0x7F and <= 0xA0
            || code == 0xAD
            || code is >= 0x2000 and <= 0x200F
            || code is >= 0x2028 and <= 0x202F
            || code is >= 0x205F and <= 0x2064
            || code == 0x3000
            || code == 0xFEFF;
    }
}
