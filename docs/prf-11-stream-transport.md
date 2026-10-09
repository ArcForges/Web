# PRF.11 server-stream transport decision (pending the observed run)

Status: open. The decision is recorded as a candidate order with the offline evidence below. It is not final until the local opt-in run against the deployed ingress observes the binary stream (LS2 in the PRF.11 plan). Until then, no transport is claimed as proven.

## The two candidate framings

| Framing | Content type | Browser path | Offline evidence |
| --- | --- | --- | --- |
| Binary server stream | `application/grpc-web+proto` | .NET 10 browser streaming client (`HttpClient` streaming response in Blazor WebAssembly) | `tests/ArcForges.Web.App.Tests/StreamFramingTests.cs`: frames decode in order, the trailers frame carries `grpc-status: 0`, a truncated frame is malformed, and a data frame over the 32 KiB stream bound is refused. |
| grpc-web-text server stream | `application/grpc-web-text` | the whole body is base64 and decodes before the frames are read | `StreamFramingTests`: the text body decodes to the same bytes as the binary body; a body that is not a whole number of four-character groups, or that has a character outside the base64 alphabet, is malformed. |

Both framings are tested as fixtures only. The decoders in the test are not product code. The App has no streaming surface in this phase, and a streamed message is decoded through the generated parser with the CON.92 stream frame class (`WireLimit.StreamFrame`, 32 KiB).

## Decision order

1. Binary `application/grpc-web+proto` first. Requirement 12 (line 418) makes binary server streaming the required path, and the brief (section 11, S3) keeps streaming in V1.
2. grpc-web-text only on server-stream routes, and only if the binary stream is observed to fail in the deployed run. The variant is then recorded here with the observed behaviour.

Nothing in this record selects grpc-web-text in advance, because the sources conflict (brief section 5, item 12).

## Source pages (cited, to be re-read at the observed run)

These are the two sources the brief records as verified (brief section 7, item 5.12 refined). They are cited here with the statement that the brief attributes to each. Their exact text is re-read and quoted when the observed run is recorded, because the two sources conflict.

- The grpc-web README, `https://raw.githubusercontent.com/grpc/grpc-web/master/README.md`. Brief section 7 records that it states server streaming is supported only in grpc-web-text mode in browsers.
- The Microsoft Learn gRPC-Web page for .NET (the page the brief calls the Learn gRPC-Web page; its exact address is recorded in the PRF.11 observed run, because the brief does not record it). Brief section 7 records that it returned 404 in two earlier passes, was retrieved with HTTP 200 on 2026-10-08 by the decision-record reviewers, and states that `GrpcWebText` is required for server streaming in browsers.

The conflict is the reason the decision is open. The .NET 10 browser streaming client is expected to read a binary stream, and the grpc-web README says browsers need the text mode. The observed run settles it.

## What the observed run must record (LS2)

- The host, browser (installed Chrome or Edge, with its version), the commit, the date and the deployment identity, claimant-reported.
- The binary `application/grpc-web+proto` stream against the deployed ingress: the frames received in order, the trailers status, and whether the stream was delivered incrementally or only at the end.
- The grpc-web-text stream only if the binary stream fails, with the same fields.
- The exact source text quoted from both pages at the time of the run.
- A wire-registry row, if the framing variant needs one, as a handoff to Contracts. It is not a PRF.11 write.

## Not claimed

- No live stream is observed in this phase. The deployed ingress depends on CLOUD.21 and CLOUD.22 (blocked, not proven) and on CLOUD.85 (blocked, not proven).
- The offline fixtures prove the framing and the bounds only. They do not prove that a browser delivers a server stream incrementally.
- The .NET 10 browser streaming client is not exercised offline in this phase.
