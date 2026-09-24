# Windows connection workflow

`ConnectionClient` serializes PAKE, protected Pending-before-POST, phone approval, strict session validation and read-only mounts. Calls run on workers; progress contains fixed states only. Cancellation of pairing persists `RevocationPending` before network cleanup. A lost reply leaves the protected record recoverable through `ConnectAsync`'s session check; no token regeneration or POST replay occurs.

`DeviceApi` trusts only the confirmed/saved CA using `CustomRootTrust` and native address verification. No certificate acceptance callback, proxy, redirects, cookies or compression. Requests are HTTP/1.1 with bounded headers, 4096-byte bodies, exact JSON fields, response identity and status validation, and a 10-second request deadline. PAKE uses 30 seconds overall, 5 seconds per frame, send half-close after W7 and EOF after A8. Input routing rejects loopback, DNS names and noncanonical addresses, consistently with discovery/mounting.

One product desktop instance owns the current read-only mount. Revocation disables the saved record, stops that mount, requests self-revocation, then verifies token rejection. A 401 alone cannot prove that a still-pending approval cannot later activate, so the record stays disabled. Explicit **Forget locally** is a separate user choice with a warning that phone authorization can remain.

Temporary managed authentication strings are unavoidable in HttpClient/rclone interfaces and cannot be reliably zeroed by the CLR; byte/character buffers and leases are cleared where owned. No credentials, PINs, grant values, arbitrary HTTP bodies or exception messages are logged. Current-user malware/admin isolation is outside DPAPI's guarantee.

This is the P1-008 development implementation. Actual validation, limits and unfinished work are recorded in the active task until acceptance; it does not implement writes, automatic reconnection or an installer.
