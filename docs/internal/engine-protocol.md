# Engine protocol (internal)

This is the machine interface the CLI offers CodeMuster's hosted service (D65). It is
deliberately left out of `codemuster --help`, `docs/usage.md` and the READMEs. Hidden means
undocumented for users, not secret: anyone who reads this file can use it. Tests in
`EngineEventTests`, `EngineEventFileTests` and the CLI `EngineEventsTests` pin it.

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

## Versioning

`v` changes only when an existing event or field changes meaning, changes type, or is removed.
Adding an event type or adding a field to an existing event does not change `v`, so a reader
must ignore event types and fields it does not know. A reader that sees a `v` it does not
support should stop reading rather than guess. A new version is recorded here, with what
changed, before the CLI writes it.

| v | since | change |
| --- | --- | --- |
| 1 | after 0.3.6 | first version |
