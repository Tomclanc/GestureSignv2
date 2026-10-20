# Issue 6: input health recovery (18.4.1)

The 2026-10-17 reporter log ends gesture input at 20:23:07 while tray interactions and normal exit remain logged until 20:30:24. It does not establish whether raw messages stopped arriving, a registration was lost, or parsing/capture state stopped progressing. Previous complete-frame stale-contact fixes did trigger earlier in the session; the new freeze is not proven to have the same cause.

Every 30 seconds the input owner thread records raw message and delivered-frame counters, parser errors, invalid-device reports, pending contacts, capture/translator state, mouse hook status and actual process raw-input registrations. Only counts, modes and handles are added, without input contents or window titles. A failed native query or idle input never triggers recovery.

When the actual registrations demonstrably omit an expected digitizer usage, have a different target HWND, or lack background-input delivery, the next health tick cancels capture and restores registration. It preserves the recognition mode and never executes an interrupted gesture. This targets a confirmed condition, not every possible freeze.

Read-only IPC command 15 requests a current snapshot. Collect-SupportInfo.ps1 requests it, waits a bounded time for owner-thread acknowledgement, and includes Input-health.json and request status alongside the existing logs. Run this BEFORE Recover-Input.ps1.

Native tests query actual Windows registrations, remove one in the isolated test process, detect it, re-register it, and verify repeated idle probes. They also destroy and recreate the native message window. This exposed an existing attempt to re-register HWND=0 during destruction; registration now runs only with a live handle, and registration errors preserve the native error code. This lifecycle defect is confirmed by the test, not established as the reporter's cause.

The daemon startup self-test verifies read-only snapshot IPC alongside existing recovery and contact tests. Existing Surface contact-frame protection is preserved. Merged into 18.4.1 after policy, native registration/lifecycle and startup IPC checks. This does not establish the cause of every intermittent Surface Pro 11 freeze; continued hardware testing remains useful.
