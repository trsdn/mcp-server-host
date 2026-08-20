# Tool reference

All tools return a JSON object with `snake_case` properties. Every result carries
`success` (boolean) and, on failure, `error_message`. Errors never propagate as exceptions across
the MCP boundary — a failing tool returns:

```json
{
  "success": false,
  "error_message": "connect_session failed: COM ProgID 'PCOMM.autECLConnList' is not registered ...",
  "error_kind": "no_provider",
  "is_error": true
}
```

`error_kind` is machine readable: `no_provider`, `timeout`, `terminal`, `bad_mnemonic`,
`bad_argument`, `cancelled`, or an exception type name for anything unexpected.

## Conventions

- **Rows and columns are 1-based**, matching EHLLAPI and every emulator UI. Row 1 / column 1 is the
  top-left character.
- **Field indexes are 0-based**, matching the `fields` array returned by `get_screen`.
- **`session` is the emulator short name** (`"A"`, `"B"`, …), not a host name.
- Tools that change the screen wait for host readiness before returning, unless documented
  otherwise.

---

## Discovery and lifecycle

### `list_sessions`

Lists the terminal sessions the detected emulator exposes. Call this first.

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `provider` | string? | `auto` | `auto`, `pcomm`, `ehllapi` or `fake`. |

Returns `{ provider, sessions: [{ name, provider, connected, rows, columns, description }], success }`.

The EHLLAPI provider cannot enumerate; it reports the conventional short names `A`–`Z` and notes
that existence is only verified on connect.

### `connect_session`

Attaches to a session and caches the connection for subsequent tools.

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `session` | string | — | Emulator short name. |
| `provider` | string? | `auto` | Provider override. |

Returns `{ session, provider, rows, columns, success }`.

### `disconnect_session`

Releases the cached connection. **Does not log the user off the host.**

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `session` | string | — | Emulator short name. |

---

## Reading the screen

### `get_screen`

The primary read tool: a complete structured snapshot.

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `session` | string | — | Emulator short name. |

Returns:

```json
{
  "session": "A",
  "rows": 24,
  "columns": 80,
  "text": "...newline separated screen...",
  "fields": [
    {
      "index": 0,
      "row": 1,
      "column": 2,
      "length": 20,
      "text": "USERID",
      "protected": true,
      "numeric": false,
      "hidden": false
    }
  ],
  "cursor_row": 5,
  "cursor_column": 20,
  "oia": {
    "input_inhibited": false,
    "alarm": false,
    "communication_error": false,
    "status_text": null
  },
  "success": true
}
```

**Hidden (non-display / password) fields are returned with empty `text`.** This is deliberate: MCP
clients log tool results, and a password field must not end up in a transcript.

### `read_field`

One field, addressed by index or by any coordinate inside it.

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `session` | string | — | Emulator short name. |
| `field_index` | int? | — | 0-based index from `get_screen`. |
| `row` | int? | — | 1-based row inside the field, when `field_index` is omitted. |
| `column` | int? | — | 1-based column inside the field. |

Provide either `field_index` **or** `row`+`column`.

### `get_text`

A fixed number of characters from a coordinate, ignoring field boundaries.

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `session` | string | — | Emulator short name. |
| `row` | int | — | 1-based row. |
| `column` | int | — | 1-based column. |
| `length` | int | — | Characters to read. Wraps across row boundaries. |

### `search_text`

Searches the *current* screen without waiting.

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `session` | string | — | Emulator short name. |
| `text` | string | — | Text to find. |
| `ignore_case` | bool | `false` | Case-insensitive comparison. |

Returns `{ session, found, row, column, success }`.

### `get_cursor`

Returns `{ session, row, column, success }`.

---

## Writing

Writing tools **do not submit the screen**. Use `send_keys` with an AID mnemonic
(`[enter]`, `[pf3]`, …) to hand control to the host.

### `write_field`

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `session` | string | — | Emulator short name. |
| `text` | string | — | Text to place in the field. |
| `field_index` | int? | — | 0-based index from `get_screen`. |
| `row` / `column` | int? | — | 1-based coordinate inside the field. |

The text is truncated to the field length. Writing to a protected field fails with
`error_kind: "terminal"`.

### `set_text`

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `session` | string | — | Emulator short name. |
| `row` | int | — | 1-based row. |
| `column` | int | — | 1-based column. |
| `text` | string | — | Text to write. |

### `set_cursor`

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `session` | string | — | Emulator short name. |
| `row` | int | — | 1-based row. |
| `column` | int | — | 1-based column. |

---

## Keystrokes and synchronization

### `send_keys`

The workhorse. Literal text mixed with bracketed mnemonics.

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `session` | string | — | Emulator short name. |
| `keys` | string | — | For example `USER01[tab]SECRET[enter]`. Write `[[` for a literal `[`. |
| `wait_for_ready` | bool | `true` | Wait for the OIA keyboard lock to clear before returning. |
| `timeout_seconds` | int? | `30` | Timeout for the readiness wait. |

Returns a full `get_screen`-shaped result **after** the host responded, so a single call covers the
usual type–submit–read cycle.

Set `wait_for_ready: false` only for keystrokes that never reach the host (cursor movement, local
field editing). Waiting when nothing was submitted just burns the timeout.

Supported mnemonics:

| Category | Mnemonics |
| --- | --- |
| AID keys | `[enter]` `[clear]` `[pf1]`–`[pf24]` `[pa1]`–`[pa3]` `[attn]` `[sysreq]` |
| Navigation | `[tab]` `[backtab]` `[home]` `[end]` `[left]` `[right]` `[up]` `[down]` |
| Editing | `[delete]` `[backspace]` `[insert]` `[eraseeof]` `[eraseinput]` `[reset]` |
| Diagnostic | `[test]` |

Mnemonic names are case-insensitive. `[pf03]` and `[pf3]` are the same key. An unknown mnemonic
fails fast with `error_kind: "bad_mnemonic"` and lists the supported names.

### `wait_for_ready`

Waits until the OIA reports the keyboard is no longer inhibited.

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `session` | string | — | Emulator short name. |
| `timeout_seconds` | int? | `30` | Timeout. |

Returns `{ session, elapsed_milliseconds, oia, success }`. A timeout is reported with
`error_kind: "timeout"` rather than an exception.

### `wait_for_text`

Waits for a text to appear. Use this when the host redraws in several steps and the keyboard
unlocks before the final screen arrives.

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `session` | string | — | Emulator short name. |
| `text` | string | — | Text to wait for. |
| `row` | int? | — | 1-based row. With `column`, the text must appear exactly there. |
| `column` | int? | — | 1-based column. |
| `ignore_case` | bool | `false` | Case-insensitive comparison. |
| `timeout_seconds` | int? | `30` | Timeout. |

---

## `batch`

Runs several tools sequentially in one round trip, waiting for host readiness between steps. This
is the efficient way to drive a multi-screen transaction.

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `steps` | string | — | JSON array of step objects. Each needs a `tool` property; the rest are that tool's arguments. |
| `stop_on_error` | bool | `true` | Stop at the first failing step. |

```json
[
  { "tool": "send_keys",     "session": "A", "keys": "LOGON[enter]" },
  { "tool": "wait_for_text", "session": "A", "text": "PASSWORD" },
  { "tool": "write_field",   "session": "A", "row": 8, "column": 20, "text": "..." },
  { "tool": "send_keys",     "session": "A", "keys": "[enter]" },
  { "tool": "get_screen",    "session": "A" }
]
```

Returns `{ steps: [{ index, tool, success, result, error_message }], steps_executed, success }`.
`batch` cannot nest — a step with `"tool": "batch"` is rejected.

---

## `transfer_file` — not implemented

IND$FILE transfer is **defined but not implemented**. The tool validates its arguments, connects
the session, and then returns an unsuccessful transfer result:

```json
{
  "session": "A",
  "bytes_transferred": 0,
  "message": "IND$FILE transfer is not implemented ...",
  "success": false,
  "error_message": "IND$FILE transfer is not implemented ..."
}
```

Invalid arguments (an unknown `direction`, a relative `local_path`) fail earlier with
`error_kind: "bad_argument"`.

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `session` | string | — | Emulator short name. |
| `direction` | string | — | `send` (workstation → host) or `receive` (host → workstation). |
| `local_path` | string | — | Absolute workstation path. |
| `host_file` | string | — | Host dataset or member, for example `USER.DATA(MEMBER)`. |
| `options` | string? | — | Emulator specific options, for example `ASCII CRLF`. |
| `timeout_seconds` | int? | `300` | Transfer timeout. |

### Why it is deferred

The tool exists now so the contract is stable, but the implementation is not trivial and is easy to
get dangerously wrong:

- **PCOMM** exposes transfer through `autECLSession.autECLXfer` (`SendFile`/`ReceiveFile`) with
  emulator-specific option strings, and the option syntax differs between CMS, TSO and CICS hosts.
- **EHLLAPI** has no transfer function at all. It would have to be driven by typing an `IND$FILE`
  command and screen-scraping the progress, which is fragile.
- **Overwriting a host dataset is destructive and irreversible.** It needs an explicit confirmation
  design and a clear audit story before an agent is allowed to do it.

Track the work before relying on it. Until then, run transfers through the emulator's own UI.
