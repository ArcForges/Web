// SPDX-License-Identifier: AGPL-3.0-only
using System.Text;
using ArcForges.Contracts.Hello.V1;
using Google.Protobuf;

namespace ArcForges.Web.Ui;

/// <summary>A name that the hello example refuses. The message is the visitor-facing failure text.</summary>
public sealed class InvalidNameException : Exception
{
    /// <summary>Creates the refusal with the exact visitor-facing text.</summary>
    public InvalidNameException()
        : base(HelloGreeting.InvalidNameMessage)
    {
    }
}

/// <summary>
/// The hello example greeting (the TypeScript apps/site/app/hello.ts port). The greeting is built from the published
/// SayHello request and response messages, so it is one real protobuf round trip and not a local string format only.
/// </summary>
public static class HelloGreeting
{
    /// <summary>The visitor-facing text for a refused name.</summary>
    public const string InvalidNameMessage = "Enter a name between 1 and 80 characters, without control characters.";

    /// <summary>The name shown before the visitor enters one.</summary>
    public const string DefaultName = "World";

    /// <summary>The largest name, counted in Unicode code points before trimming.</summary>
    public const int MaximumNameLength = 80;

    /// <summary>
    /// Returns the greeting for <paramref name="name"/>. The name is refused when it is blank after trimming, longer
    /// than 80 code points or contains a C0 control or DEL character. Surrounding whitespace is removed before the
    /// request is encoded; an unpaired surrogate is encoded as U+FFFD, as the browser text encoder does.
    /// </summary>
    /// <exception cref="InvalidNameException">The name is not accepted.</exception>
    public static string Greet(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (ContainsControl(name) || CodePointCount(name) > MaximumNameLength || JsTrim(name).Length == 0)
            throw new InvalidNameException();
        var trimmed = JsTrim(name);
        var wireName = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(trimmed));
        var request = new SayHelloRequest { Name = wireName };
        var decoded = SayHelloRequest.Parser.ParseFrom(request.ToByteArray());
        var response = new SayHelloResponse { Message = $"Hello, {decoded.Name}!" };
        return SayHelloResponse.Parser.ParseFrom(response.ToByteArray()).Message;
    }

    private static bool ContainsControl(string name)
    {
        foreach (var character in name)
            if (character < 32 || character == 127)
                return true;
        return false;
    }

    private static int CodePointCount(string name)
    {
        var count = 0;
        foreach (var _ in name.EnumerateRunes())
            count++;
        return count;
    }

    /// <summary>Trims the characters that JavaScript String.prototype.trim removes.</summary>
    private static string JsTrim(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsJsWhitespace(value[start]))
            start++;
        while (end > start && IsJsWhitespace(value[end - 1]))
            end--;
        return value[start..end];
    }

    private static bool IsJsWhitespace(char character)
    {
        var code = (int)character;
        return code is 0x09 or 0x0A or 0x0B or 0x0C or 0x0D or 0x20 or 0xA0 or 0x1680 or 0x2028 or 0x2029
            or 0x202F or 0x205F or 0x3000 or 0xFEFF
            || (code >= 0x2000 && code <= 0x200A);
    }
}
