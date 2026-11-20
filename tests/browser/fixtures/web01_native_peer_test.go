// SPDX-License-Identifier: AGPL-3.0-only

// This test-only file is passed to the exact Cloud verification checkout by
// go test -overlay. The Center executable itself is built without that overlay.
package main

import (
	"bytes"
	"compress/gzip"
	"context"
	"crypto/sha256"
	"crypto/x509"
	"encoding/base64"
	"encoding/binary"
	"encoding/json"
	"io"
	"math/big"
	"net"
	"net/http"
	"net/http/httputil"
	"net/url"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
	"time"

	connectv2 "connectrpc.com/connect/v2"
	"connectrpc.com/connect/v2/connecthttp"
	"github.com/ArcForges/Cloud/center/internal/ingress"
	"github.com/ArcForges/Cloud/center/internal/pgtest"
	"github.com/ArcForges/Cloud/center/internal/platform"
	foundation "github.com/ArcForges/Contracts/go/arcforges/foundation/v1"
	managementv2 "github.com/ArcForges/Contracts/go/arcforges/management/v2"
	"github.com/ArcForges/Contracts/go/arcforges/management/v2/managementv2connect"
	v2 "github.com/ArcForges/Contracts/go/arcforges/sandbox/v2"
	"github.com/ArcForges/Contracts/go/arcforges/sandbox/v2/sandboxv2connect"
	"google.golang.org/protobuf/encoding/protojson"
	"google.golang.org/protobuf/proto"
)

const web01PeerRequired = "ARCFORGES_WEB01_PEER_REQUIRED"

func TestWeb01DashboardPeer(t *testing.T) {
	exchange := web01CheckInputs(t)
	receipt := web01Receipt(t, exchange)
	root := t.TempDir()
	binary := host03Build(t, root)
	fixture := host03Center(t, root, binary)
	db := fixture.install()
	fixture.serve(db, true)
	leaf, err := x509.ParseCertificate(fixture.server.TLS.Certificates[0].Certificate[0])
	if err != nil {
		t.Fatal("could not read the scoped public TLS key")
	}
	spki := sha256.Sum256(leaf.RawSubjectPublicKeyInfo)
	nodeBinary := host03InputFile(t, "ARCFORGES_HOST03_NODE")
	nodeSHA := host03FileDigest(t, nodeBinary)
	if nodeSHA != os.Getenv("ARCFORGES_HOST03_NODE_SHA256") {
		t.Fatal("the scoped installed native Node digest differs")
	}
	web01WriteFile(t, exchange, "browser.json", map[string]any{
		"origin": fixture.origin, "bootstrap": fixture.bootstrap,
		"spkiSha256": base64.StdEncoding.EncodeToString(spki[:]),
	})

	// The browser completes WebAuthn, reads the empty registry and opens the
	// original PKCE consent URI. No fixture code seeds a session or enrollment.
	web01AwaitFile(t, exchange, "enroll-node")
	node := startHost03Node(t, root, nodeBinary, fixture.origin)
	start := node.call("center", "connect", fixture.origin, "--mode", "self_hosted", "--flow", "browser")
	owner := host03LocalID(t, host03Text(start, "ownerEpoch"))
	flowID := host03Text(start, "flowId")
	_ = host03LocalID(t, flowID)
	var verification string
	host03Wait(t, 35*time.Second, "the actual Node's consent URI", func() bool {
		flow := host03Obj(node.call("center", "flow", flowID), "flow")
		verification = host03Text(flow, "verificationUri")
		return host03Text(flow, "state") == "pending" && verification != ""
	})
	parsed, err := url.Parse(verification)
	if err != nil || parsed.Scheme+"://"+parsed.Host != fixture.origin || parsed.Path != "/api/connect" || parsed.Fragment == "" {
		t.Fatal("the installed Node did not name the exact isolated consent origin")
	}
	web01WriteFile(t, exchange, "consent.json", map[string]string{"verificationUri": verification})

	first := node.online(owner)
	protocol := fixture.protocol(db, &first)
	sandbox := node.createSandbox("Browser fixture Sandbox")
	before := node.privateDigest(sandbox)
	web01RequireNoRuns(t, node, sandbox)
	grant, revision := web01GrantRunList(t, node, sandbox, first)
	dbConn := pgtest.Connect(t, pgtest.WithDatabase(t, db.migrationURL, db.name))
	var device platform.ID
	if err := dbConn.QueryRow(t.Context(), "SELECT device_id FROM fleet.node WHERE enrollment_id=$1 AND node_id=$2",
		first.enrollment, first.node).Scan(&device); err != nil || device == (platform.ID{}) {
		t.Fatal("the exact installed Node's browser device identity did not exist")
	}
	web01WriteFile(t, exchange, "device.json", map[string]string{
		"deviceId": device.String(), "nodeId": web01CLIID(first.node), "sandboxId": sandbox.id, "grantId": grant, "grantRevision": revision,
	})

	web01AwaitFile(t, exchange, "disconnect-node")
	node.call("center", "disconnect")
	host03Wait(t, 25*time.Second, "the real enrolled device to go offline", func() bool {
		var disconnected bool
		state := host03Obj(node.call("center", "status"), "connection")
		return dbConn.QueryRow(t.Context(), "SELECT connection_id IS NULL FROM fleet.node WHERE enrollment_id=$1 AND revoked_at IS NULL",
			first.enrollment).Scan(&disconnected) == nil && disconnected && host03Text(state, "channelState") == "offline"
	})
	web01WriteFile(t, exchange, "offline.json", map[string]bool{"offline": true})

	web01AwaitFile(t, exchange, "reconnect-node")
	resumed := node.call("center", "connect", fixture.origin, "--mode", "self_hosted", "--flow", "browser")
	if host03Text(resumed, "flowId") != "" || node.online(owner).enrollment != first.enrollment || fixture.protocol(db, &first) != protocol {
		t.Fatal("the original enrolled Node did not return on its durable V2 protocol")
	}
	web01WriteFile(t, exchange, "rejoined.json", map[string]bool{"online": true})

	web01AwaitFile(t, exchange, "browser-revoked")
	command, revokedRevision := web01VerifyRevokeWire(t, exchange, device)
	host03Wait(t, 25*time.Second, "the browser's real device revocation and channel close", func() bool {
		var revoked bool
		return dbConn.QueryRow(t.Context(), `SELECT d.revoked_at IS NOT NULL AND n.revoked_at IS NOT NULL AND n.connection_id IS NULL
FROM device.device d JOIN fleet.node n ON d.device_id=n.device_id WHERE d.device_id=$1 AND n.enrollment_id=$2`,
			device, first.enrollment).Scan(&revoked) == nil && revoked
	})
	var effects, completed int
	var durableRevision int64
	if err := dbConn.QueryRow(t.Context(), `SELECT d.rev,
 (SELECT count(*) FROM audit.audit_event WHERE event_type='device.revoked' AND target_kind='device' AND target_id=$1::uuid::text),
 (SELECT count(*) FROM platform.command_receipt WHERE command_id=$2 AND status=2 AND completed_at IS NOT NULL AND error_code IS NULL)
FROM device.device d WHERE d.device_id=$1::uuid`, device, command).Scan(&durableRevision, &effects, &completed); err != nil ||
		durableRevision != revokedRevision || effects != 1 || completed != 1 {
		t.Fatal("the browser's two related attempts did not leave one exact durable device revocation and command receipt")
	}
	web01RequireNoRuns(t, node, sandbox)
	if before != node.privateDigest(sandbox) {
		t.Fatal("the real browser device revocation altered Node-private data")
	}
	node.verifyNoOfficialCall()
	fixture.shutdown()
	*receipt = map[string]any{"centerSha256": host03FileDigest(t, binary), "nodeSha256": nodeSHA, "deviceId": device.String(), "postgresVersion": "18.7",
		"protocolEpoch": protocol.String(), "localRunsBeforeAndAfter": 0, "deviceRevoked": true,
		"deviceWentOfflineAndRejoined": true, "ownedDataSha256": before,
		"revokeCommandId": command.String(), "durableDeviceRevocations": effects, "deviceRevision": durableRevision}
}

// TestWeb01NativeAPI covers the Linux native acceptance independently of
// Chromium. It uses the original host passkey-input helper and real typed
// browser-session HTTP requests, and makes no claim about a rendered page.
func TestWeb01NativeAPI(t *testing.T) {
	exchange := web01CheckInputs(t)
	receipt := web01Receipt(t, exchange)
	root := t.TempDir()
	binary := host03Build(t, root)
	fixture := host03Center(t, root, binary)
	db := fixture.install()
	fixture.serve(db, true)
	owner := fixture.browser()
	owner.enroll(fixture.bootstrap)

	registry := connectv2.NewClient(connecthttp.NewTransport(web01RegistryTransport{owner}, fixture.origin+"/api", connecthttp.WithGRPCWeb()))
	list := func() []*managementv2.Device {
		t.Helper()
		var out managementv2.DeviceServiceListResponse
		web01RegistryCall(t, registry, managementv2connect.DeviceServiceListProcedure,
			&managementv2.DeviceServiceListRequest{Meta: host03RequestMeta(), Limit: proto.Uint32(10)}, &out)
		page := out.GetValue().GetPage()
		if out.GetError() != nil || page == nil || page.GetNextCursor() != "" {
			t.Fatal("the actual account-scoped typed registry page was unavailable or incomplete")
		}
		return page.GetDevices()
	}
	if len(list()) != 0 {
		t.Fatal("the fresh account did not start with an empty real device registry")
	}
	nodeBinary := host03InputFile(t, "ARCFORGES_HOST03_NODE")
	nodeSHA := host03FileDigest(t, nodeBinary)
	if nodeSHA != os.Getenv("ARCFORGES_HOST03_NODE_SHA256") {
		t.Fatal("the scoped installed native Node digest differs")
	}
	node := startHost03Node(t, root, nodeBinary, fixture.origin)
	enrolled := node.enroll(owner)
	protocol := fixture.protocol(db, &enrolled)
	sandbox := node.createSandbox("Native dashboard API fixture Sandbox")
	web01RequireNoRuns(t, node, sandbox)
	grant, revisionText := web01GrantRunList(t, node, sandbox, enrolled)
	revision, err := strconv.ParseInt(revisionText, 10, 64)
	if err != nil || revision < 1 {
		t.Fatal("the scoped native Run.List grant did not return a positive revision")
	}
	devices := list()
	if len(devices) != 1 || devices[0].GetPresence() != "online" || len(devices[0].GetDeviceId().GetValue()) != 16 ||
		!bytes.Equal(devices[0].GetEnrollmentId().GetValue(), enrolled.enrollment[:]) {
		t.Fatal("the real typed registry did not return the exact installed Node as online")
	}
	deviceID := devices[0].GetDeviceId()
	inspect := func(wanted string) {
		t.Helper()
		var out managementv2.DeviceServiceInspectResponse
		web01RegistryCall(t, registry, managementv2connect.DeviceServiceInspectProcedure,
			&managementv2.DeviceServiceInspectRequest{Meta: host03RequestMeta(), DeviceId: deviceID}, &out)
		current := out.GetValue().GetDevice()
		if out.GetError() != nil || !proto.Equal(current.GetDeviceId(), deviceID) ||
			!bytes.Equal(current.GetEnrollmentId().GetValue(), enrolled.enrollment[:]) || current.GetPresence() != wanted {
			t.Fatalf("the actual typed device details did not report the exact enrollment in state %q", wanted)
		}
	}
	scope := host03Scope{node: enrolled.node, enrollment: enrolled.enrollment, grant: host03LocalID(t, grant), revision: revision}
	readRuns := func(offline bool) {
		t.Helper()
		request := &v2.RunServiceListRequest{Meta: host03RequestMeta(), SandboxId: host03ProtoID(host03LocalID(t, sandbox.id)), Limit: proto.Uint32(10)}
		var reply v2.RunServiceListResponse
		if err := owner.remote(scope, sandboxv2connect.RunServiceListProcedure, request, &reply); err != nil {
			t.Fatalf("the real typed Run.List transport failed: %s", connectv2.CodeOf(err))
		}
		if offline {
			failure := reply.GetError()
			if reply.GetValue() != nil || failure.GetCategory() != foundation.ErrorCategory_ERROR_CATEGORY_STATE || failure.GetCode() != "node_offline" ||
				failure.GetEffect() != foundation.EffectCertainty_EFFECT_CERTAINTY_DID_NOT_HAPPEN {
				t.Fatal("the scoped Run.List while offline did not return a real offline refusal")
			}
			return
		}
		value := reply.GetValue()
		if reply.GetError() != nil || value.GetPage() == nil || len(value.GetRuns()) != 0 || value.GetPage().GetNextCursor() != "" {
			t.Fatal("the exact granted native Sandbox did not return a verified empty Run page")
		}
	}
	inspect("online")
	readRuns(false)
	node.call("center", "disconnect")
	conn := pgtest.Connect(t, pgtest.WithDatabase(t, db.migrationURL, db.name))
	host03Wait(t, 25*time.Second, "the actual closed native device channel", func() bool {
		var offline bool
		return conn.QueryRow(t.Context(), "SELECT connection_id IS NULL FROM fleet.node WHERE enrollment_id=$1 AND revoked_at IS NULL",
			enrolled.enrollment).Scan(&offline) == nil && offline
	})
	inspect("offline")
	readRuns(true)
	node.rejoin(enrolled)
	if fixture.protocol(db, &enrolled) != protocol {
		t.Fatal("the reconnected installed Node changed its durable protocol generation")
	}
	inspect("online")
	readRuns(false)
	node.verifyNoOfficialCall()
	fixture.shutdown()
	*receipt = map[string]any{"centerSha256": host03FileDigest(t, binary), "nodeSha256": nodeSHA, "postgresVersion": "18.7",
		"deviceId": platform.ID(deviceID.GetValue()).String(), "protocolEpoch": protocol.String(),
		"realDeviceListAndInspect": true, "realScopedEmptyBeforeAndAfterReconnect": true, "realScopedOfflineRefusal": true, "browser": false}
}

type web01RegistryTransport struct{ browser *host03Browser }

func (transport web01RegistryTransport) Do(request *http.Request) (*http.Response, error) {
	transport.browser.headers(request)
	return transport.browser.f.client.Do(request)
}

func web01RegistryCall(t *testing.T, client *connectv2.Client, path string, in, out proto.Message) {
	t.Helper()
	ctx, cancel := context.WithTimeout(t.Context(), 12*time.Second)
	defer cancel()
	if err := client.CallUnary(ctx, connectv2.Spec{Procedure: path, StreamType: connectv2.StreamTypeUnary}, in, out); err != nil {
		t.Fatalf("the real typed browser-session API %s returned %s", path, connectv2.CodeOf(err))
	}
}

func web01CheckInputs(t *testing.T) string {
	t.Helper()
	if os.Getenv(web01PeerRequired) != "1" || os.Getenv(pgtest.EnvRequired) != "1" {
		t.Fatal("the local dashboard peer requires explicit opt-in and isolated PostgreSQL")
	}
	exchange := os.Getenv("ARCFORGES_WEB01_EXCHANGE")
	directory, err := os.Stat(exchange)
	if !filepath.IsAbs(exchange) || err != nil || !directory.IsDir() {
		t.Fatal("the local dashboard peer requires an owned private exchange directory")
	}
	dbURL, err := url.Parse(pgtest.ServerURL(t))
	if err != nil || net.ParseIP(dbURL.Hostname()) == nil || !net.ParseIP(dbURL.Hostname()).IsLoopback() {
		t.Fatal("the real dashboard database endpoint must use loopback")
	}
	conn := pgtest.Admin(t)
	defer conn.Close(context.Background())
	var postgresVersion int
	if err := conn.QueryRow(t.Context(), "SELECT current_setting('server_version_num')::integer").Scan(&postgresVersion); err != nil || postgresVersion != 180007 {
		t.Fatal("the real dashboard fixture must observe PostgreSQL 18.7")
	}
	return exchange
}

func web01Receipt(t *testing.T, exchange string) *map[string]any {
	t.Helper()
	var receipt map[string]any
	t.Cleanup(func() {
		if receipt != nil && !t.Failed() {
			web01WriteFile(t, exchange, "native-receipt.json", receipt)
		}
	})
	return &receipt
}

func web01VerifyRevokeWire(t *testing.T, exchange string, device platform.ID) (platform.ID, int64) {
	t.Helper()
	body, err := os.ReadFile(filepath.Join(exchange, "revoke-wire.json"))
	var attempts []struct {
		Request, Response []byte
		ResponseEncoding  string
	}
	if err != nil || len(body) > 192*1024 || json.Unmarshal(body, &attempts) != nil || len(attempts) != 2 {
		t.Fatal("expected exactly two bounded real browser revoke exchanges")
	}
	var command platform.ID
	var expected int64
	for index, attempt := range attempts {
		req := new(managementv2.DeviceServiceRevokeRequest)
		reqBytes, ok := ingress.ReadSingleMessage(attempt.Request)
		if !ok || len(reqBytes) > 4096 || proto.Unmarshal(reqBytes, req) != nil ||
			!bytes.Equal(req.GetDeviceId().GetValue(), device[:]) ||
			req.GetMeta().GetExpectedRev().GetValue() < 1 || len(req.GetMeta().GetCommandId().GetValue()) != len(command) {
			t.Fatal("the browser revoke wire did not bind the exact selected device, revision and command")
		}
		if index == 0 {
			command = platform.ID(req.GetMeta().GetCommandId().GetValue())
			expected = req.GetMeta().GetExpectedRev().GetValue()
		}
		if command == (platform.ID{}) || !bytes.Equal(command[:], req.GetMeta().GetCommandId().GetValue()) ||
			expected != req.GetMeta().GetExpectedRev().GetValue() {
			t.Fatal("the browser's authorized retry changed its original device mutation identity")
		}
		reply := new(managementv2.DeviceServiceRevokeResponse)
		payload := web01ResponseMessage(t, attempt.Response, attempt.ResponseEncoding)
		if proto.Unmarshal(payload, reply) != nil {
			t.Fatal("the real gRPC-Web response did not decode as DeviceService.Revoke")
		}
		if index == 0 {
			failure := reply.GetError()
			if reply.GetValue() != nil || failure.GetCategory() != foundation.ErrorCategory_ERROR_CATEGORY_AUTHENTICATION ||
				failure.GetCode() != "step_up_required" || failure.GetEffect() != foundation.EffectCertainty_EFFECT_CERTAINTY_DID_NOT_HAPPEN {
				t.Fatal("the first browser request did not require fresh step-up before effect")
			}
			continue
		}
		value := reply.GetValue().GetDevice()
		revision := value.GetRevision().GetValue()
		if reply.GetError() != nil || !bytes.Equal(value.GetDeviceId().GetValue(), device[:]) ||
			value.GetPresence() != "revoked" || revision <= expected || reply.GetMeta().GetResultRev().GetValue() != revision {
			t.Fatal("the authorized browser retry did not return the exact revoked device revision")
		}
		return command, revision
	}
	t.Fatal("the browser revoke did not return a completed typed result")
	return platform.ID{}, 0
}

func web01ResponseMessage(t *testing.T, body []byte, encoding string) []byte {
	t.Helper()
	if len(body) < ingress.HeaderLength || len(body) > 65536 {
		t.Fatal("the browser typed response is not bounded")
	}
	if encoding != "" && encoding != "identity" && encoding != "gzip" {
		t.Fatal("the browser typed response declared an unsupported gRPC encoding")
	}
	message, tail := web01ResponseFrame(t, body, 0, encoding)
	trailer, remaining := web01ResponseFrame(t, tail, 128, encoding)
	wantTrailer := ingress.TrailerFrame(ingress.StatusOK, "", false)[ingress.HeaderLength:]
	if len(remaining) != 0 || !bytes.Equal(trailer, wantTrailer) {
		t.Fatal("the browser revoke response lacks its exact single gRPC success trailer")
	}
	return message
}

func web01ResponseFrame(t *testing.T, body []byte, kind byte, encoding string) ([]byte, []byte) {
	t.Helper()
	if len(body) < ingress.HeaderLength || (body[0] != kind && body[0] != kind|1) {
		t.Fatal("the browser revoke response did not carry the expected gRPC-Web frame")
	}
	end := uint64(binary.BigEndian.Uint32(body[1:5])) + ingress.HeaderLength
	if end > uint64(len(body)) {
		t.Fatal("the browser revoke response carried a truncated gRPC-Web frame")
	}
	payload := body[ingress.HeaderLength:end]
	if body[0]&1 == 0 {
		return payload, body[end:]
	}
	if encoding != "gzip" {
		t.Fatal("the browser revoke frame used compression without matching gRPC gzip negotiation")
	}
	source := bytes.NewReader(payload)
	zipped, err := gzip.NewReader(source)
	if err != nil {
		t.Fatal("the browser revoke frame did not carry a valid gzip stream")
	}
	zipped.Multistream(false)
	decoded, readErr := io.ReadAll(io.LimitReader(zipped, 65536+1))
	closeErr := zipped.Close()
	if readErr != nil || closeErr != nil || len(decoded) > 65536 || source.Len() != 0 {
		t.Fatal("the browser revoke frame did not carry exactly one bounded gzip stream")
	}
	return decoded, body[end:]
}

func web01GrantRunList(t *testing.T, node *host03Node, sandbox host03Sandbox, enrollment host03Enrollment) (string, string) {
	t.Helper()
	grant := &v2.RemoteGrant{GrantId: host03ProtoID(platform.NewID()), EnrollmentId: host03ProtoID(enrollment.enrollment),
		SandboxId: host03ProtoID(host03LocalID(t, sandbox.id)), ScopeKind: new("sandbox"), ResourceCeiling: sandbox.resources,
		Methods: []string{sandboxv2connect.RunServiceListProcedure}}
	encoded, err := protojson.Marshal(grant)
	path := filepath.Join(node.directory, "browser-run-list-grant.json")
	if err != nil || os.WriteFile(path, encoded, 0600) != nil {
		t.Fatal("could not prepare the exact owner-local Run List grant")
	}
	view := host03Obj(node.call("grant", "set", "--file", path), "grant")
	id, revision := host03Text(view, "grantId"), host03Text(host03Obj(view, "revision"), "value")
	if host03LocalID(t, id) != platform.ID(grant.GrantId.GetValue()) || revision == "" ||
		host03LocalID(t, host03Text(view, "enrollmentId")) != enrollment.enrollment {
		t.Fatal("the issued local grant did not bind the exact enrolled Node and request")
	}
	return id, revision
}

func web01CLIID(id platform.ID) string {
	const symbols = "0123456789abcdefghjkmnpqrstvwxyz"
	var text [26]byte
	value := new(big.Int).SetBytes(id[:])
	for position := len(text) - 1; position >= 0; position-- {
		text[position] = symbols[value.Uint64()&31]
		value.Rsh(value, 5)
	}
	return string(text[:])
}

func web01RequireNoRuns(t *testing.T, node *host03Node, sandbox host03Sandbox) {
	t.Helper()
	view := node.call("run", "list", "--sandbox", sandbox.id)
	if runs, present := view["runs"]; present {
		list, ok := runs.([]any)
		if !ok || len(list) != 0 {
			t.Fatal("the original native Sandbox was not observed to have zero Runs")
		}
	}
	if page := host03Obj(view, "page"); page == nil || host03Text(page, "nextCursor") != "" {
		t.Fatal("the native zero-Run read was unexpectedly only the first page")
	}
}

func web01AwaitFile(t *testing.T, directory, name string) {
	t.Helper()
	host03Wait(t, 100*time.Second, "browser phase "+name, func() bool {
		if info, err := os.Lstat(filepath.Join(directory, "abort")); err == nil && info.Mode().IsRegular() {
			t.Fatal("the explicit local browser runner ended before the required phase")
		}
		info, err := os.Lstat(filepath.Join(directory, name))
		return err == nil && info.Mode().IsRegular() && info.Size() == 0
	})
}

func web01WriteFile(t *testing.T, directory, name string, value any) {
	t.Helper()
	file, err := os.OpenFile(filepath.Join(directory, name), os.O_CREATE|os.O_EXCL|os.O_WRONLY, 0600)
	if err != nil {
		t.Fatal("could not open the new scoped browser phase receipt")
	}
	err = json.NewEncoder(file).Encode(value)
	closeErr := file.Close()
	if err != nil || closeErr != nil {
		t.Fatal("could not save the scoped browser phase receipt")
	}
}

// The private overlay calls this before starting the live HOST-03 TLS proxy.
// API requests are handled by the original fixture and actual Go process.
func web01NativeStaticHandler(t *testing.T, fixture *host03Fixture, api http.Handler) http.Handler {
	t.Helper()
	upstream, err := url.Parse(os.Getenv("ARCFORGES_WEB01_STATIC_ORIGIN"))
	if err != nil || upstream.Scheme != "http" || upstream.Hostname() != "127.0.0.1" ||
		upstream.Port() == "" || upstream.User != nil || upstream.Path != "" ||
		upstream.RawQuery != "" || upstream.Fragment != "" {
		t.Fatal("the verified static dashboard peer must be literal loopback HTTP")
	}
	_, port, err := net.SplitHostPort(fixture.server.Listener.Addr().String())
	if err != nil || port == "" {
		t.Fatal("could not determine the selected local TLS origin")
	}
	const name = "Local verification Center"
	authority := "localhost:" + port
	origin := "https://" + authority
	transport := &http.Transport{ResponseHeaderTimeout: 10 * time.Second}
	t.Cleanup(transport.CloseIdleConnections)
	asset := &httputil.ReverseProxy{Transport: transport, Rewrite: func(request *httputil.ProxyRequest) {
		request.SetURL(upstream)
		// Static assets do not receive API/session credentials.
		request.Out.Header.Del("Cookie")
		request.Out.Header.Del("Authorization")
		request.Out.Header.Del(ingress.CSRFHeader)
	}, ErrorHandler: func(w http.ResponseWriter, _ *http.Request, _ error) {
		w.WriteHeader(http.StatusBadGateway)
	}}
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Host != authority || r.URL.EscapedPath() != r.URL.Path {
			http.NotFound(w, r)
			return
		}
		switch {
		case r.URL.Path == "/api" || strings.HasPrefix(r.URL.Path, "/api/") ||
			r.URL.Path == "/metrics" || r.URL.Path == "/readyz":
			api.ServeHTTP(w, r)
		case r.URL.Path == "/appsettings.json" && r.Method == http.MethodGet && r.URL.RawQuery == "":
			w.Header().Set("Content-Type", "application/json")
			w.Header().Set("Cache-Control", "no-store")
			w.Header().Set("X-Content-Type-Options", "nosniff")
			_ = json.NewEncoder(w).Encode(map[string]any{"Dashboard": map[string]string{
				"CenterOrigin": origin, "Mode": "self-hosted", "Name": name,
			}})
		default:
			ctx, cancel := context.WithTimeout(r.Context(), 20*time.Second)
			defer cancel()
			asset.ServeHTTP(w, r.WithContext(ctx))
		}
	})
}
