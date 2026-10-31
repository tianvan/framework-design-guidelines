using Fdg.Cli.IO;

namespace Fdg.Cli.Tooling;

/// <summary>
/// Keeps each file's line ending across dotnet format. Without an <c>end_of_line</c> setting, the whitespace
/// formatter writes the platform newline before a line that starts with a block comment, which leaves an LF file
/// on Windows with mixed line endings.
/// </summary>
/// <remarks>
/// The files are edited as code units, so the encoding and its byte order mark stay as they are: a BOM selects
/// UTF-16 or UTF-32, anything else is treated as single-byte units, which suits UTF-8 and other ASCII-compatible
/// encodings.
/// </remarks>
public static class LineEndings
{
    private const int CarriageReturn = '\r';
    private const int LineFeed = '\n';

    /// <summary>Records the line ending of every file that uses one style only.</summary>
    /// <returns>
    /// The newline (<c>"\n"</c> or <c>"\r\n"</c>) per full path. Files with mixed line endings, a lone carriage
    /// return or no line break are left out, so <see cref="Restore"/> leaves them as written.
    /// </returns>
    public static IReadOnlyDictionary<string, string> Capture(IEnumerable<string> files)
    {
        var endings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            if (!endings.ContainsKey(file) && File.Exists(file) && GetEnding(File.ReadAllBytes(file)) is { } ending)
            {
                endings.Add(file, ending);
            }
        }

        return endings;
    }

    /// <summary>Gives each recorded file whose line endings changed its recorded newline throughout.</summary>
    public static void Restore(IReadOnlyDictionary<string, string> endings)
    {
        foreach (KeyValuePair<string, string> entry in endings)
        {
            if (!File.Exists(entry.Key))
            {
                continue;
            }

            byte[] bytes = File.ReadAllBytes(entry.Key);
            if (Convert(bytes, entry.Value == "\r\n") is { } converted)
            {
                AtomicFile.WriteAllBytes(entry.Key, converted);
            }
        }
    }

    private static string? GetEnding(byte[] bytes)
    {
        var units = new Units(bytes);
        bool lf = false;
        bool crlf = false;
        for (int i = units.Start; i < bytes.Length; i += units.Size)
        {
            int unit = units.Read(i);
            if (unit == CarriageReturn)
            {
                if (i + units.Size >= bytes.Length || units.Read(i + units.Size) != LineFeed)
                {
                    return null;
                }

                crlf = true;
                i += units.Size;
            }
            else if (unit == LineFeed)
            {
                lf = true;
            }
        }

        return lf == crlf ? null : lf ? "\n" : "\r\n";
    }

    /// <returns>The converted bytes, or <see langword="null"/> when nothing changed.</returns>
    private static byte[]? Convert(byte[] bytes, bool useCrLf)
    {
        var units = new Units(bytes);
        using var output = new MemoryStream(bytes.Length + bytes.Length / 16);
        output.Write(bytes, 0, units.Start);
        bool changed = false;
        for (int i = units.Start; i < bytes.Length; i += units.Size)
        {
            int unit = units.Read(i);
            bool isCrLf = unit == CarriageReturn
                && i + units.Size < bytes.Length
                && units.Read(i + units.Size) == LineFeed;
            if (isCrLf && !useCrLf)
            {
                // Drop the carriage return; the line feed follows.
                changed = true;
                continue;
            }

            if (unit == LineFeed && useCrLf && (i == units.Start || units.Read(i - units.Size) != CarriageReturn))
            {
                output.Write(units.Encode(CarriageReturn));
                changed = true;
            }

            output.Write(bytes, i, Math.Min(units.Size, bytes.Length - i));
        }

        return changed ? output.ToArray() : null;
    }

    /// <summary>The code units of a file, as its byte order mark gives them.</summary>
    private readonly struct Units
    {
        private readonly byte[] _bytes;
        private readonly bool _bigEndian;

        public Units(byte[] bytes)
        {
            _bytes = bytes;
            if (StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF))
            {
                (Start, Size, _bigEndian) = (4, 4, true);
            }
            else if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00))
            {
                (Start, Size, _bigEndian) = (4, 4, false);
            }
            else if (StartsWith(bytes, 0xFE, 0xFF))
            {
                (Start, Size, _bigEndian) = (2, 2, true);
            }
            else if (StartsWith(bytes, 0xFF, 0xFE))
            {
                (Start, Size, _bigEndian) = (2, 2, false);
            }
            else if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
            {
                (Start, Size, _bigEndian) = (3, 1, false);
            }
            else
            {
                (Start, Size, _bigEndian) = (0, 1, false);
            }
        }

        /// <summary>The offset after the byte order mark.</summary>
        public int Start { get; }

        public int Size { get; }

        /// <returns>The unit at <paramref name="offset"/>, or -1 when the file ends inside it.</returns>
        public int Read(int offset)
        {
            if (offset + Size > _bytes.Length)
            {
                return -1;
            }

            int value = 0;
            for (int k = 0; k < Size; k++)
            {
                int shift = 8 * (_bigEndian ? Size - 1 - k : k);
                value |= _bytes[offset + k] << shift;
            }

            return value;
        }

        public byte[] Encode(int value)
        {
            byte[] unit = new byte[Size];
            for (int k = 0; k < Size; k++)
            {
                int shift = 8 * (_bigEndian ? Size - 1 - k : k);
                unit[k] = (byte)(value >> shift);
            }

            return unit;
        }

        private static bool StartsWith(byte[] bytes, params byte[] prefix)
        {
            return bytes.AsSpan().StartsWith(prefix);
        }
    }
}
