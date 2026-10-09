// SPDX-License-Identifier: AGPL-3.0-only
using Grpc.Core;

namespace ArcForges.Web.App.Probe;

/// <summary>The closed set of reasons a probe call ends without a usable answer (the React failure kinds).</summary>
public enum FailureKind
{
    /// <summary>The user cancelled the call.</summary>
    Cancelled,
    /// <summary>The server did not answer before the deadline.</summary>
    Timeout,
    /// <summary>The server or the network is unavailable.</summary>
    Unavailable,
    /// <summary>The session has ended.</summary>
    Unauthenticated,
    /// <summary>The server refused the request.</summary>
    Forbidden,
    /// <summary>The server rejected the request.</summary>
    Rejected,
    /// <summary>The request exceeded a server limit.</summary>
    Limit,
    /// <summary>The answer is not something this page can read.</summary>
    Malformed,
    /// <summary>Anything else.</summary>
    Unexpected,
}

/// <summary>The fixed visible text of each failure kind. Server text is never echoed into a message.</summary>
public static class FailureText
{
    /// <summary>Returns the fixed text of the kind.</summary>
    public static string For(FailureKind kind) => kind switch
    {
        FailureKind.Cancelled => "The request was cancelled.",
        FailureKind.Timeout => "The server did not answer in time.",
        FailureKind.Unavailable => "The server is unavailable. Try again later.",
        FailureKind.Unauthenticated => "Your session has ended. Reload to check it again.",
        FailureKind.Forbidden => "The server refused this request.",
        FailureKind.Rejected => "The server rejected the request.",
        FailureKind.Limit => "The request exceeded a server limit.",
        FailureKind.Malformed => "The server answered with something this page cannot read.",
        FailureKind.Unexpected => "The server failed unexpectedly. Try again later.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>A typed probe failure. Its message is the fixed text of its kind, never server or request content.</summary>
public sealed class ProbeFailureException : Exception
{
    /// <summary>Creates the failure of one kind.</summary>
    public ProbeFailureException(FailureKind kind, Exception? cause = null)
        : base(FailureText.For(kind), cause)
    {
        Kind = kind;
    }

    /// <summary>The closed failure kind.</summary>
    public FailureKind Kind { get; }
}

/// <summary>The status-to-kind mappings of the same-origin session routes and of the gRPC-Web hello call.</summary>
public static class FailureMapping
{
    /// <summary>The kind of an HTTP status of a session route (the failureFromStatus table). A 200 is never passed here.</summary>
    public static FailureKind FromStatus(int status)
    {
        if (status == 401)
            return FailureKind.Unauthenticated;
        if (status == 403)
            return FailureKind.Forbidden;
        if (status is 408 or 504)
            return FailureKind.Timeout;
        if (status is 413 or 429)
            return FailureKind.Limit;
        if (status is 502 or 503)
            return FailureKind.Unavailable;
        if (status >= 500)
            return FailureKind.Unexpected;
        if (status >= 400)
            return FailureKind.Rejected;
        return FailureKind.Malformed;
    }

    /// <summary>
    /// The kind of a server gRPC status. A <c>Cancelled</c> status the user did not ask for is an unexpected server answer;
    /// the user's own cancellation is detected from the caller's token.
    /// </summary>
    public static FailureKind FromGrpc(StatusCode code) => code switch
    {
        StatusCode.DeadlineExceeded => FailureKind.Timeout,
        StatusCode.Unavailable or StatusCode.Aborted => FailureKind.Unavailable,
        StatusCode.Unauthenticated => FailureKind.Unauthenticated,
        StatusCode.PermissionDenied => FailureKind.Forbidden,
        StatusCode.ResourceExhausted => FailureKind.Limit,
        StatusCode.InvalidArgument or StatusCode.NotFound or StatusCode.AlreadyExists
            or StatusCode.FailedPrecondition or StatusCode.OutOfRange or StatusCode.Unimplemented => FailureKind.Rejected,
        _ => FailureKind.Unexpected,
    };
}
