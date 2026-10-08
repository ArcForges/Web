// SPDX-License-Identifier: AGPL-3.0-only
using System.Text;

namespace ArcForges.Web.Tooling.Profiles;

/// <summary>
/// The deterministic archive of the profile bundle: plain POSIX ustar regular files in a fixed order, zero times and
/// owners, no compression. Equal content is the same bytes, so the bundle's name is the digest of its own archive. The
/// reader accepts exactly what the writer writes and refuses anything else.
/// </summary>
internal static class TarArchive
{
    private const int Block = 512;
    private const int NameLength = 100;

    /// <summary>One regular file of the archive.</summary>
    public sealed record Entry(string Path, byte[] Bytes);

    /// <summary>Writes the entries in the given order and returns the archive bytes.</summary>
    public static byte[] Write(IReadOnlyList<Entry> entries)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var output = new MemoryStream();
        foreach (var entry in entries)
        {
            if (!seen.Add(entry.Path))
                throw new InvalidOperationException("Duplicate entry " + entry.Path);
            output.Write(Header(entry.Path, entry.Bytes.LongLength));
            output.Write(entry.Bytes);
            var padding = (Block - entry.Bytes.Length % Block) % Block;
            output.Write(new byte[padding]);
        }
        output.Write(new byte[Block * 2]);
        return output.ToArray();
    }

    /// <summary>Reads exactly what <see cref="Write"/> writes. Any other header, size or padding is refused.</summary>
    public static IReadOnlyList<Entry> Read(byte[] archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.Length % Block != 0 || archive.Length < Block * 2)
            throw new InvalidOperationException("Not a bundle archive.");
        var entries = new List<Entry>();
        var offset = 0;
        while (true)
        {
            if (offset + Block > archive.Length)
                throw new InvalidOperationException("The archive ends without its terminator.");
            var head = archive.AsSpan(offset, Block);
            if (head.IndexOfAnyExcept((byte)0) < 0)
            {
                if (offset + Block * 2 != archive.Length)
                    throw new InvalidOperationException("Data after the archive terminator.");
                if (archive.AsSpan(offset + Block, Block).IndexOfAnyExcept((byte)0) >= 0)
                    throw new InvalidOperationException("The archive terminator is not zero.");
                return entries;
            }
            var end = head[..NameLength].IndexOf((byte)0);
            var nameBytes = end < 0 ? head[..NameLength] : head[..end];
            var path = Encoding.ASCII.GetString(nameBytes);
            var sizeField = Encoding.ASCII.GetString(head.Slice(124, 11)).TrimEnd('\0');
            if (!IsOctal(sizeField))
                throw new InvalidOperationException("Bad entry size.");
            var size = Convert.ToInt64(sizeField, 8);
            if (size < 0 || size > int.MaxValue)
                throw new InvalidOperationException("Bad entry size.");
            var expected = Header(path, size);
            if (!head.SequenceEqual(expected.AsSpan()))
                throw new InvalidOperationException("Entry " + path + " is not a plain regular file header.");
            var start = offset + Block;
            if (start + size > archive.Length)
                throw new InvalidOperationException("Entry " + path + " is truncated.");
            var bytes = archive.AsSpan(start, (int)size).ToArray();
            var padded = (int)((size + Block - 1) / Block * Block);
            if (start + padded > archive.Length)
                throw new InvalidOperationException("Entry " + path + " has no padding.");
            for (var index = start + (int)size; index < start + padded; index++)
                if (archive[index] != 0)
                    throw new InvalidOperationException("Entry " + path + " has non-zero padding.");
            entries.Add(new Entry(path, bytes));
            offset = start + padded;
        }
    }

    private static bool IsOctal(string text) => text.Length > 0 && text.All(character => character is >= '0' and <= '7');

    private static byte[] Header(string path, long size)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z0-9_][A-Za-z0-9._/-]*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)
            || path.Contains("..", StringComparison.Ordinal) || path.Contains("//", StringComparison.Ordinal) || path.EndsWith('/'))
            throw new InvalidOperationException("Bad archive path: " + path);
        var name = Encoding.ASCII.GetBytes(path);
        if (name.Length > NameLength)
            throw new InvalidOperationException("Path too long for a plain tar header: " + path);
        var head = new byte[Block];
        name.CopyTo(head, 0);
        WriteOctal(head, 100, 8, 0x1A4); // 0644
        WriteOctal(head, 108, 8, 0);
        WriteOctal(head, 116, 8, 0);
        WriteOctal(head, 124, 12, size);
        WriteOctal(head, 136, 12, 0);
        for (var index = 148; index < 156; index++)
            head[index] = 0x20;
        head[156] = (byte)'0';
        Encoding.ASCII.GetBytes("ustar\0").CopyTo(head, 257);
        Encoding.ASCII.GetBytes("00").CopyTo(head, 263);
        long sum = 0;
        foreach (var value in head)
            sum += value;
        Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(head, 148);
        return head;
    }

    private static void WriteOctal(byte[] head, int offset, int length, long value)
    {
        var text = Convert.ToString(value, 8);
        if (text.Length >= length)
            throw new InvalidOperationException("Value does not fit its tar field.");
        Encoding.ASCII.GetBytes(text.PadLeft(length - 1, '0') + "\0").CopyTo(head, offset);
    }
}
