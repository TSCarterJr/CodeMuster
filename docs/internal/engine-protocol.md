# Engine protocol (internal)

This is the machine interface the CLI offers CodeMuster's hosted service (D65). It is
deliberately left out of `codemuster --help`, `docs/usage.md` and the READMEs. Hidden means
undocumented for users, not secret: anyone who reads this file can use it. Tests in
`EngineEventTests`, `EngineControlTests`, `EngineEventFileTests`, `EngineControlFileTests` and
the CLI `EngineEventsTests` and `EngineControlTests` pin it.

There are two channels, both plain files so they work the same on Windows, macOS and Linux:
the CLI writes events to one, and a controller writes commands to the other.

## Event stream

Set `CODEMUSTER_ENGINE_EVENTS=<file>` and `scan`, `run`, `verify` and `fix` append events to
that file. Any other command ignores the variable. The path is resolved against the current
directory, missing folders are created, and an existing file is appended to, never truncated.

Each event is one JSON object on one line: UTF-8 without a byte order mark, ending in a single
LF (never CRLF), written and flushed as a whole line. A reader that tails the file only has to
wait for the LF; it never sees half an event. Workers run concurrently, but all writes go
through one lock, so lines never interleave. The file is opened with read, write and delete
sharing, so a reader may open, tail or delete it while the command runs, on Windows too.

Every event starts with the same three fields, in this order:

| field | type | meaning |
| --- | --- | --- |
| `v` | integer | protocol version, currently `1` |
| `t` | string | when the event was written, UTC ISO 8601 with seven fractional digits and `Z` |
| `type` | string | the event type, below |

The event's own fields follow in the order listed here. A field listed for an event is always
present; its value may be `null` where the table says so. Numbers are JSON numbers: counts and
ids are integers, `cost_usd` and `reported_cost_usd` are decimals in US dollars.

### scan

| type | fields |
| --- | --- |
| `scan_progress` | `message` (string): one step, the same text scan prints on standard error, such as `listed 17 files, 7 excluded`, the mapper steps under their language, `planning units`, `saving 10 units` |
| `scan_summary` | `head` (commit sha), `files_included`, `files_excluded`, `units_created`, `units_stale`, `units_total` (integers), `slices`, `orphans` (integers, `null` in file mode), `resolution_rate` (0 to 1, `null` in file mode or when no mapper returned a map), `vulnerable_packages` (integer, `null` when the dependency audit did not run) |

`scan_summary` is the last event of a scan that succeeds. A scan that fails writes no summary.

### run, verify and fix

| type | fields |
| --- | --- |
| `run_started` | `command` (`run`, `verify` or `fix`), `workers` (pool size at start), `agent`, `model`, `effort` (the identity passed to the harness, D35; `model` and `effort` are `null` for the harness default) |
| `unit_started` | `unit` (unit id), `kind` (`file`, `slice`, `orphan`, `verify`, `ux`, `fix`, ...), `key` (short human name), `worker` (1-based lane), `attempt` (1-based, counting this one) |
| `unit_finished` | `unit`, `kind`, `key`, `attempt`, `outcome`, `message` (one line, the text the console prints), `gave_up` (bool: true when this was the unit's last attempt), `duration_ms` (integer), `usage` (object or `null`), `cost_usd` (decimal or `null`), `cost_source` (string or `null`) |
| `finding_recorded` | `unit`, `path`, `line` (first line), `severity` (`critical`, `high`, `medium`, `low`, `info`), `category` |
| `verify_outcome` | `unit`, `key`, `verdict` (`confirmed`, `refuted`, `unsure`, `resolved`) |
| `fix_outcome` | `unit`, `path`, `fixed` (array of finding ids changed in code), `declined` (array of finding ids left alone with a reason) |
| `skipped` | `unit`, `kind`, `key`, `reason`: the pack exceeded `slice_token_budget`, so no agent was called (D50, D56) |
| `unit_not_started` | `unit`, `kind`, `key`, `attempt`, `reason`, `gave_up`: run or verify could not build the unit's pack, or the unit needs browser evidence; no agent was called |
| `run_summary` | run and verify: `command`, `completed`, `gave_up`, `skipped` (counts), `cancelled` (bool). fix: `command`, `units`, `fixed`, `declined`, `gave_up`, `skipped` (counts), `cancelled`, `failed` (bool: the command stopped on an error) |

`outcome` is one of:

- run and verify: `recorded` (the response was stored and the unit is done), `invalid_response`
  (the response was not valid JSON for the schema), `rejected` (the adapter failed, or the
  response was refused, for example a finding outside the unit).
- fix: `recorded` (the repair was committed or declined and recorded), `rejected` (the worker
  failed, or the response or its tests were rejected; the unit may be retried),
  `integration_failed` (the repair could not be applied to the checkout; fix stops starting new
  repairs), `not_applied` (the worker finished after fix stopped starting repairs; its worktree
  is kept).

`usage` has `input_tokens`, `output_tokens`, `cache_read_tokens`, `cache_write_tokens` (integers
or `null`), `model` (the model that actually answered, or `null`) and `reported_cost_usd` (the
harness's own figure, or `null`), as COST1 records them (D63). `usage`, `cost_usd` and
`cost_source` are `null` when the harness never ran, so nothing was spent. `cost_usd` is also
`null` for a model the price table does not know; `cost_source` is `harness`, `config` or
`table <date checked>`.

Ordering guarantees:

- `run_started` is first and `run_summary` is last, including when the command is cancelled
  with Ctrl+C. A cancelled command's summary has `cancelled: true`.
- Every `unit_finished` follows the `unit_started` with the same `unit` and `attempt`. A unit
  that is still running when the command is cancelled has a start and no finish: its call was
  abandoned and nothing was recorded for it.
- `finding_recorded` and `verify_outcome` follow the `unit_finished` they belong to, and
  `fix_outcome` follows its `unit_finished`.
- A retried unit starts again with the next `attempt`.
- `skipped` and `unit_not_started` have no `unit_started`.

Worker lanes are the lowest free number, so a pool of 3 uses lanes 1 to 3; after a resize to
fewer workers, lanes above the new size drain and are not reused.

## Control channel

Set `CODEMUSTER_ENGINE_CONTROL=<file>` and `run`, `verify` and `fix` read commands from that
file while they work. Other commands ignore the variable. Set `CODEMUSTER_ENGINE_EVENTS` as
well: every command is acknowledged on the event stream, and without it the acknowledgements
are lost.

The CLI polls the file every 250 ms. Each poll opens it with read, write and delete sharing,
reads only the bytes after the last one it read, and closes it, so the controller may keep the
file open for writing. The file need not exist when the command starts. It is read from its
beginning, so use a new or empty file for each command: a `pause` left in the file makes the
next command start paused. If the file becomes shorter than what was read, it is read again
from the start.

One command per line, UTF-8, ending in LF (a CR before the LF is ignored). A line without its
LF waits for it. Blank lines are skipped without an acknowledgement. A line is either plain
text, a command name then its value after whitespace, or a JSON object with a string
`command` and an optional `value` (a string or a number):

```
pause
workers 4
{"command": "model", "value": "claude-opus-4-7"}
```

Command names are case-insensitive; values are passed on as written.

| command | effect |
| --- | --- |
| `pause` | Start no new units. Units already running finish and are recorded as usual. A paused run with no work left ends normally. Rejected when already paused. |
| `resume` | Start units again. Rejected when not paused. |
| `stop` | Start no new units and cancel the running calls, exactly as Ctrl+C does: nothing is recorded for a cancelled call, the ledger stays consistent, the command exits 1, and `run_summary` has `cancelled: true`. fix restores stashed changes as it does on Ctrl+C. |
| `workers <n>` | Resize the pool to `n` (a whole number of at least 1). Growing starts more units at once; shrinking lets running units finish and starts new ones only while fewer than `n` run. Applies while paused too. |
| `model <id>` | Units started from now on run with this model; units already running keep theirs. Each analysis, verification and fix records the model and effort it actually ran with (D35), and so does each agent call (D63). |
| `effort <level>` | As `model`, for the effort or reasoning level. |

`model` and `effort` replace only the setting they name. For codex the other setting keeps the
value D55 resolved at start; it is not looked up again. The value is passed to the harness
verbatim, like `--model` and `--effort`; a value the harness rejects fails those units' calls,
which count as failed attempts.

Every non-blank line gets exactly one acknowledgement event, in the order the lines were
written:

| type | fields |
| --- | --- |
| `command_applied` | `command` (lowercase name), `value` (`workers`: integer; `model`, `effort`: string; otherwise `null`), `line` (the line as written) |
| `command_rejected` | `line`, `reason`: `unknown command '<name>'`, `already paused`, `not paused`, `workers needs a whole number of at least 1`, `<command> needs a value`, `not valid JSON: ...`, `a JSON command needs a string "command"`, or `this command cannot change the agent's <model or effort>` |

After the acknowledgement, `pause`, `resume` and `stop` also write a `paused`, `resumed` or
`stopped` event, with no fields of their own. The loop that starts units waits for commands
alongside its running calls, so a command takes effect within one poll interval of being
written, even while every worker is busy.

## Versioning

`v` changes only when an existing event or field changes meaning, changes type, or is removed.
Adding an event type or adding a field to an existing event does not change `v`, so a reader
must ignore event types and fields it does not know. A reader that sees a `v` it does not
support should stop reading rather than guess. A new version is recorded here, with what
changed, before the CLI writes it.

| v | since | change |
| --- | --- | --- |
| 1 | after 0.3.6 | first version |
