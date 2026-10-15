# AI connection repair (2026-10-15)

Store WinUI log at LocalCache/Local/GestureSign V2 showed repeated control-pipe connection timeouts between 08:32 and 08:35, unsuccessful engine-stop attempts, and one engine exit (-1). An independent read-only progress request later succeeded against the existing engine. These logs establish intermittent connection failure, but do not establish why that particular process exited.

The UI previously waited only 1+5 seconds before reporting failure and could launch another engine even while a previous engine held host.lock. Startup now allows a bounded 30-second connection wait and reuses a locked engine instead of launching duplicates. Only pre-connect failures are retried; commands are never replayed after being sent.

Transient connection failures use the existing localized retry message instead of a generic HRESULT. Successful status refresh clears a prior connection error; successful commands clear their old error report. Log diagnostics now include the exact control pipe and data root, with executable/exit code for early engine exit.

Verification: delayed real named-pipe server tests, existing-host reuse, one startup, bounded timeout and no replay after response failure. User samples, models and preferences are not modified by this repair.
