// SPDX-License-Identifier: AGPL-3.0-only
using System.Globalization;

namespace ArcForges.Web.App.Probe;

/// <summary>
/// A uint64 that crossed the wire as canonical decimal text. The text is validated, held as a <see cref="ulong"/> and shown
/// as the same canonical text: no value passes through a floating-point number.
/// </summary>
public readonly record struct ExactUnsigned(ulong Value, string Text);

/// <summary>The exact 64-bit parser (the TypeScript parseUInt64 rule: canonical digits, no sign, no leading zero, no space).</summary>
public static class Exact
{
    /// <summary>Parses canonical uint64 text, or returns false for any other text, including values above the uint64 maximum.</summary>
    public static bool TryUnsigned(string? wire, out ExactUnsigned result)
    {
        result = default;
        if (string.IsNullOrEmpty(wire))
            return false;
        foreach (var character in wire)
            if (character is < '0' or > '9')
                return false;
        if (wire.Length > 1 && wire[0] == '0')
            return false;
        if (!ulong.TryParse(wire, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return false;
        result = new ExactUnsigned(value, value.ToString(CultureInfo.InvariantCulture));
        return true;
    }
}
