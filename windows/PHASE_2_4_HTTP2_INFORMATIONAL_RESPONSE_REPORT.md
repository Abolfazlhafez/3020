# Phase 2.4 — HTTP/2 informational CONNECT responses

HTTP/2 permits informational response header blocks before the final response. Prior code treated every non-2xx response as a terminal CONNECT rejection, which also rejected legal 1xx responses. `WindowsHttp2ConnectSession` now leaves the CONNECT task unresolved for statuses 100–199 except 101, then waits for the eventual final response. HTTP/2 status 101 remains rejected by `ParseResponseStatus`, and an informational block that sets `END_STREAM` is rejected.

Pure test source covers the status classifier. Tests have not been run per the requested coding-first workflow; this report does not claim compile/runtime correctness.
