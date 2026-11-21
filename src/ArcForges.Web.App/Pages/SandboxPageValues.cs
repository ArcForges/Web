// SPDX-License-Identifier: AGPL-3.0-only
using System.Buffers.Binary;
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.Sandbox.V2;

namespace ArcForges.Web.App.Pages;

internal static class SandboxPageValues
{
    internal static Guid? Id(Id? id)
    {
        if (id?.Value.Length != 16)
            return null;

        var value = new Guid(id.Value.Span, bigEndian: true);
        return value == Guid.Empty ? null : value;
    }

    internal static string CliId(Guid id)
    {
        const string alphabet = "0123456789abcdefghjkmnpqrstvwxyz";
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        var value = ((UInt128)BinaryPrimitives.ReadUInt64BigEndian(bytes) << 64)
            | BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]);
        Span<char> result = stackalloc char[26];
        for (var index = result.Length - 1; index >= 0; index--)
        {
            result[index] = alphabet[(int)(value & 31)];
            value >>= 5;
        }
        return new string(result);
    }

    internal static string State(SandboxState state) => state switch
    {
        SandboxState.Creating => "Creating",
        SandboxState.Ready => "Ready",
        SandboxState.Deleting => "Deleting",
        SandboxState.Deleted => "Deleted",
        SandboxState.Error => "Error",
        SandboxState.RecoveryRequired => "Recovery required",
        _ => "Unknown",
    };

    internal static string Network(NetworkMode mode) => mode switch
    {
        NetworkMode.Allow => "Allowed by policy",
        NetworkMode.Deny => "Denied by policy",
        _ => "Unavailable",
    };
}
