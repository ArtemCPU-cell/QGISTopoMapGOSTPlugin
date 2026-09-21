# Overpass live run — 21 September 2026

Bbox: `55.75,37.60,55.77,37.64` (central Moscow). The full uncommitted console captures are in `out/overpass-live-before.log` and `out/overpass-live-after.log`; this file retains the material results.

## Before the fix

The original client used a 15-second `HttpClient.Timeout`, `User-Agent: curl/8.4.0`, and did not print successful endpoint/status lines.

| Layer | Observed result |
| --- | --- |
| roads | `overpass-api.de` 406; Mail.ru 504; Kumi cancelled by the 15-second client timeout. |
| buildings | The same 406 / 504 / 15-second-timeout sequence. |
| water | The same 406 / 504 / 15-second-timeout sequence. |
| vegetation | `overpass-api.de` 406, then an unlabelled successful response with 0 elements. |
| places | `overpass-api.de` 406, then an unlabelled successful response with 0 elements. |

The old retry message was inaccurate: its `continue` advanced the outer mirror loop, so it never retried the same mirror.

## After the fix

The client used a 120-second timeout, emits mirror/status/latency/attempt information, and retries 429/503 on the same endpoint before trying another endpoint.

| Layer | Observed result |
| --- | --- |
| roads | `overpass-api.de`: 200 in 1.0 s, 10,432 elements. |
| buildings | `overpass-api.de`: 200 in 1.1 s, 1,905 elements. |
| water | `overpass-api.de`: 429 in 9.1 s; it waited 20 s and retried **the same URL**, which returned 504 in 16.3 s; Mail.ru returned 504 in 0.6 s; Kumi reached the 120-second client timeout. |
| vegetation | `overpass-api.de` 504 in 8.6 s; Mail.ru 504 in 51.9 s; Kumi 200 in 75.1 s with 0 elements. |
| places | `overpass-api.de` 200 in 5.9 s with 0 elements; this exposed a separate query bug: the request was for `way`, but `SettlementBuilder` only accepts `node`. |

A later direct download with the corrected `node[place=...]` query returned 2 elements. The retried direct downloads eventually returned 200 for all five cache files.

## Headers

The earlier statement that browser-only headers select a harmful server path is not reproducible evidence and was removed. In a controlled follow-up on the same small `node[place=...]` form POST, minimal client headers returned 504 in 9.1 seconds while a Chromium-like header set returned 200 in 1.2 seconds. This disproves the claim that those headers are universally harmful. It does not prove they cause the difference: the public service was visibly load/rate-limit sensitive throughout the run. The production client therefore sends only a descriptive User-Agent and JSON Accept negotiation, not synthetic browser headers.

