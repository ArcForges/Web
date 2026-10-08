// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ArcForges.Web.App.Probe;

/// <summary>
/// Decodes a same-origin session or hello document strictly: the generated Contracts JSON context is the only record
/// source, and a document that repeats a property name at any depth is refused, because the generated reader keeps the
/// last value of a repeated name. Every decoding failure is a malformed answer.
/// </summary>
public static class WireJson
{
    /// <summary>Decodes one document with the generated type information, or fails as malformed.</summary>
    public static T Decode<T>(ReadOnlySpan<byte> bytes, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        try
        {
            RejectDuplicateNames(bytes);
            return JsonSerializer.Deserialize(bytes, typeInfo)
                ?? throw new JsonException("The document is null.");
        }
        catch (JsonException exception)
        {
            throw new ProbeFailureException(FailureKind.Malformed, exception);
        }
    }

    private static void RejectDuplicateNames(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes);
        // Each open container: the names of an object, or null for an array.
        var scopes = new Stack<HashSet<string>?>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.StartArray:
                    scopes.Push(null);
                    break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    scopes.Pop();
                    break;
                case JsonTokenType.PropertyName:
                    var name = reader.GetString() ?? throw new JsonException("A property name is null.");
                    if (!scopes.Peek()!.Add(name))
                        throw new JsonException("A property name is repeated.");
                    break;
            }
        }
    }
}
