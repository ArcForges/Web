// SPDX-License-Identifier: AGPL-3.0-only
using System.Buffers.Binary;
using System.Globalization;
using ArcForges.Contracts.Foundation.V1;
using ArcForges.Contracts.Sandbox.V2;
using ArcForges.Web.App.Dashboard;
using ArcForges.Web.App.Probe;
using ArcForges.Web.App.Tests.Fixtures;
using ArcForges.Web.App.Tests.TestDoubles;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.JSInterop;
using Xunit;

namespace ArcForges.Web.App.Tests;

public sealed class DashboardWorkTransportTests
{
    [Fact]
    public async Task ExistingAggregateMutationBindsTheExactRevisionAndLocalGrant()
    {
        var token = TestContext.Current.CancellationToken;
        var command = new DashboardWorkCommand(Guid.NewGuid(), WorkFixture.OwnerEpoch, WorkFixture.Revision);
        using var server = WorkFixture.Server((request, _) =>
        {
            var sent = WorkFixture.Read(request, "/arcforges.sandbox.v2.RunService/Stop", RunServiceStopRequest.Parser);
            Assert.Equal(WorkFixture.Id(WorkFixture.RunId), sent.RunId);
            Assert.Equal(WorkFixture.Id(command.CommandId), sent.Meta.CommandId);
            Assert.Equal(WorkFixture.Id(command.OwnerEpoch), sent.OwnerEpoch);
            Assert.Equal(WorkFixture.Revision, sent.Meta.ExpectedRev.Value);
            Assert.Equal(ulong.MaxValue, sent.Meta.RecoveryGeneration);
            Assert.True(sent.HasGraceMs);
            Assert.Equal(0U, sent.GraceMs);
            return Task.FromResult(WorkFixture.Reply(new RunServiceStopResponse
            {
                Meta = WorkFixture.Meta(sent.Meta, WorkFixture.Revision + 1),
                Value = new()
                {
                    Run = WorkFixture.Run(WorkFixture.Revision + 1, RunState.Stopping),
                    Operation = WorkFixture.Operation(sent.Meta.CommandId, sent.OwnerEpoch, OperationState.Accepted, EffectCertainty.Unknown),
                },
            }));
        });
        using var observed = new InspectHeaders(server, request =>
        {
            if (request.Method != HttpMethod.Post)
                return;

            Assert.Equal(WorkFixture.Target.NodeId.ToString("D"), Assert.Single(request.Headers.GetValues("x-arcforges-node-id")));
            Assert.Equal(WorkFixture.Target.EnrollmentId.ToString("D"), Assert.Single(request.Headers.GetValues("x-arcforges-enrollment-id")));
            Assert.Equal(WorkFixture.Target.GrantId.ToString("D"), Assert.Single(request.Headers.GetValues("x-arcforges-grant-id")));
            Assert.Equal(WorkFixture.Target.GrantRevision.ToString(CultureInfo.InvariantCulture), Assert.Single(request.Headers.GetValues("x-arcforges-grant-revision")));
            Assert.True(request.Headers.CacheControl?.NoStore);
        });
        var workspaces = await WorkFixture.AuthenticateAsync(observed, token);

        var result = await workspaces.StopRunAsync(WorkFixture.Target, command, WorkFixture.RunId, 0, token);

        Assert.Equal(RunState.Stopping, result.Run.State);
        Assert.Equal(OperationState.Accepted, result.Operation.State);
        Assert.Equal(2, server.Count);
    }

    [Fact]
    public async Task MissingOrMismatchedOwnerEpochCannotSendAnEffect()
    {
        var token = TestContext.Current.CancellationToken;
        using var server = WorkFixture.Server((_, _) => throw new InvalidOperationException("The invalid mutation reached the transport."));
        var workspaces = await WorkFixture.AuthenticateAsync(server, token);
        var command = new DashboardWorkCommand(Guid.NewGuid(), WorkFixture.OwnerEpoch, WorkFixture.Revision);

        foreach (var target in new[] { WorkFixture.Target with { OwnerEpoch = null }, WorkFixture.Target with { OwnerEpoch = Guid.NewGuid() } })
        {
            var failure = await Assert.ThrowsAsync<DashboardApiException>(() => workspaces.StopRunAsync(target, command, WorkFixture.RunId, null, token));
            Assert.Equal(DashboardFailure.Validation, failure.Failure);
        }

        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task OversizedCombinedRunArgumentsAreRejectedBeforeSendingTheCommand()
    {
        var token = TestContext.Current.CancellationToken;
        using var server = WorkFixture.Server((_, _) => throw new InvalidOperationException("The oversized command reached the transport."));
        var workspaces = await WorkFixture.AuthenticateAsync(server, token);
        var command = new DashboardWorkCommand(Guid.NewGuid(), WorkFixture.OwnerEpoch, WorkFixture.Revision);
        var argv = Enumerable.Repeat(new string('x', 32_768), 9).ToArray();

        var failure = await Assert.ThrowsAsync<DashboardApiException>(() => workspaces.StartRunAsync(WorkFixture.Target, command, argv, null, token));

        Assert.Equal(DashboardFailure.Validation, failure.Failure);
        Assert.Equal("request_too_large", failure.Code);
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task UnshippedDestructiveRouteDoesNotReachTheBrowserTransport()
    {
        using var server = ScriptedServer.Always(() => throw new InvalidOperationException("The closed route reached the transport."));
        using var channel = WorkFixture.OpenChannel(server);
        var client = new SandboxService.SandboxServiceClient(channel);

        using var call = client.PlanDeleteAsync(new SandboxServicePlanDeleteRequest { SandboxId = WorkFixture.Id(WorkFixture.SandboxId) },
            cancellationToken: TestContext.Current.CancellationToken);
        var failure = await Assert.ThrowsAsync<RpcException>(() => call.ResponseAsync);

        Assert.IsType<DashboardApiException>(failure.Status.DebugException);
        Assert.Empty(server.Requests);
    }

    private sealed class InspectHeaders(HttpMessageHandler inner, Action<HttpRequestMessage> inspect) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            inspect(request);
            return base.SendAsync(request, cancellationToken);
        }
    }
}

internal static class WorkFixture
{
    private static readonly Uri Origin = new("https://manage.example.test/");
    internal static readonly Guid DeviceId = Guid.Parse("fc096a30-64b3-4971-843e-fb209f73a798");
    internal static readonly Guid NodeId = Guid.Parse("aa24bdcc-5e62-4caf-af76-72561b7b28c7");
    internal static readonly Guid EnrollmentId = Guid.Parse("86210f3f-a613-47ec-9ad1-89a8d9751482");
    internal static readonly Guid GrantId = Guid.Parse("b9864996-c3cf-4998-a12b-4e0c6126d55d");
    internal static readonly Guid SandboxId = Guid.Parse("5aac84da-7da7-469b-9e52-96435d7734e6");
    internal static readonly Guid RunId = Guid.Parse("de094a37-11f7-4609-9728-5fea0827f9b7");
    internal static readonly Guid OwnerEpoch = Guid.Parse("316dc71f-d56e-49c8-892b-f7ca688e1691");
    internal const long Revision = 9007199254740993;
    internal static readonly DashboardWorkTarget Target = new(NodeId, EnrollmentId, GrantId, Revision + 9, SandboxId, OwnerEpoch);

    internal static ScriptedServer Server(Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> rpc) =>
        new((request, token) => request.Method == HttpMethod.Get ? Task.FromResult(Bootstrap()) : rpc(request, token));

    internal static IDashboardCenter Center(HttpMessageHandler transport)
    {
        var configuration = DashboardCenterConfiguration.FromDeployment(Origin, Origin.AbsoluteUri, "self-hosted");
        var session = new SessionProbe(transport, new ProbeOrigin(Origin));
        return Assert.IsAssignableFrom<IDashboardCenter>(Create("DashboardCenter", configuration, session,
            Create("DashboardGrpcTransport", transport), Create("DashboardPasskeys", new UnusedJsRuntime()), TimeProvider.System));
    }

    internal static async Task<IDashboardWorkspaces> AuthenticateAsync(HttpMessageHandler transport, CancellationToken token)
    {
        var center = Center(transport);
        Assert.True((await center.ReadSessionAsync(token)).Authenticated);
        return Assert.IsAssignableFrom<IDashboardWorkspaces>(center);
    }

    internal static GrpcChannel OpenChannel(HttpMessageHandler transport)
    {
        var client = Create("DashboardGrpcTransport", transport);
        return Assert.IsType<GrpcChannel>(client.GetType().GetMethod("Open")!.Invoke(client, [Origin, ProbeFixtures.AuthenticatedCsrf, 4096, 4096]));
    }

    internal static T Read<T>(CapturedRequest captured, string path, MessageParser<T> parser) where T : IMessage<T>
    {
        Assert.Equal(new Uri(Origin, "/api" + path), captured.Url);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal(ProbeFixtures.AuthenticatedCsrf, captured.CsrfHeader);
        Assert.False(captured.HasAuthorizationHeader);
        Assert.False(captured.HasCookieHeader);
        Assert.Equal(0, captured.Body[0]);
        Assert.Equal(captured.Body.Length - 5, BinaryPrimitives.ReadInt32BigEndian(captured.Body.AsSpan(1, 4)));
        return parser.ParseFrom(captured.Body.AsSpan(5));
    }

    internal static HttpResponseMessage Reply(params IMessage[] messages) => Responses.Raw(
        ProbeFixtures.Concat([.. messages.Select(message => ProbeFixtures.Frame(message.ToByteArray(), 0)), ProbeFixtures.Trailers("grpc-status: 0\r\n")]),
        "application/grpc-web+proto");

    internal static Id Id(Guid value) => new() { Value = ByteString.CopyFrom(value.ToByteArray(bigEndian: true)) };

    internal static ResponseMeta Meta(RequestMeta request, long? revision = null)
    {
        var meta = new ResponseMeta { CorrelationId = request.CorrelationId };
        if (request.HasRecoveryGeneration)
            meta.RecoveryGeneration = request.RecoveryGeneration;
        if (revision is { } value)
            meta.ResultRev = new Revision { Value = value };
        return meta;
    }

    internal static Run Run(long revision = Revision, RunState state = RunState.Running) => new()
    {
        RunId = Id(RunId),
        SandboxId = Id(SandboxId),
        State = state,
        Revision = new() { Value = revision },
        SandboxRevision = new() { Value = Revision - 1 },
        ProfileId = "fixture-profile",
        ProfileVersion = "1.2.3",
        SafeCommandDisplay = "fixture workload",
    };

    internal static Operation Operation(Id command, Id epoch, OperationState state, EffectCertainty effect) => new()
    {
        CommandId = command,
        OwnerEpoch = epoch,
        AggregateId = Id(RunId),
        State = state,
        Effect = effect,
    };

    private static object Create(string name, params object[] arguments) => Activator.CreateInstance(
        typeof(IDashboardCenter).Assembly.GetType("ArcForges.Web.App.Dashboard." + name, throwOnError: true)!, arguments)!;

    private static HttpResponseMessage Bootstrap()
    {
        var bootstrap = ProbeFixtures.AuthenticatedNode();
        var now = TimeProvider.System.GetUtcNow();
        bootstrap["session"]!["expiresAt"] = now.AddHours(2).UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        bootstrap["session"]!["idleExpiresAt"] = now.AddHours(1).UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        return Responses.Json(ProbeFixtures.Bytes(bootstrap));
    }

    private sealed class UnusedJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException(identifier);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new NotSupportedException(identifier);
    }
}
