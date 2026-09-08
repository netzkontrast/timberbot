# Spec — Error contract

Status: helper implemented in `timberbot/src/TimberbotErrors.cs`; applied on the MCP surface. REST responses keep their legacy shape in this iteration (compatibility with `tbot` and the integration suite); migrating REST bodies is slice 3a-2.

## Requirements
- E1 A structured error MUST have the shape `{ok:false, code, reason, hint?, at?:{x,y,z}, details?}`.
- E2 `code` MUST be a stable SCREAMING_SNAKE identifier from the table below; unknown legacy prefixes map to `UNKNOWN_ERROR` and keep the full text in `reason`.
- E3 `reason` MUST say what failed; `hint` SHOULD say what to try next. When a legacy message has the form `<prefix>: <reason>. <hint>` the split MUST happen at the first `". "`.
- E4 `at` MUST be present when the legacy payload carries top-level integer `x` and `y` (and `z` when present).
- E5 `details` MUST carry every other legacy key (e.g. `prefab`, `scienceCost`, `currentPoints`, `districts`, `available`) unchanged.
- E6 On MCP, a domain error MUST be a tool execution error (`isError: true`), never a JSON-RPC protocol error.

## Code table

| Legacy prefix / pattern | Code |
|---|---|
| `not_found` | `NOT_FOUND` |
| `invalid_param`, `invalid_mode`, `invalid_prompt`, `invalid_message`, `invalid_ready` | `INVALID_PARAM` |
| `invalid_type` | `INVALID_TYPE` |
| `invalid_prefab` | `INVALID_PREFAB` |
| `not_unlocked` | `NOT_UNLOCKED` |
| `insufficient_science` | `INSUFFICIENT_SCIENCE` |
| `no_population` | `NO_POPULATION` |
| `operation_failed` | `OPERATION_FAILED` |
| `refresh_timeout` | `REFRESH_TIMEOUT` |
| `unknown_endpoint` | `UNKNOWN_ENDPOINT` |
| `internal_error` | `INTERNAL_ERROR` |
| `game_not_ready` | `GAME_NOT_READY` |
| `unauthorized` | `UNAUTHORIZED` |
| `invalid_body` | `INVALID_BODY` |
| `body_too_large` | `BODY_TOO_LARGE` |
| `disabled` | `DISABLED` |
| `occupied by …` | `PLACEMENT_OCCUPIED` |
| `terrain conflict …` | `PLACEMENT_TERRAIN` |
| `blocked above …` | `PLACEMENT_BLOCKED_ABOVE` |
| `blocked below …` | `PLACEMENT_BLOCKED_BELOW` |
| `out of map …` | `PLACEMENT_OUT_OF_MAP` |
| `underground conflict …` | `PLACEMENT_UNDERGROUND` |
| `not underground …` | `PLACEMENT_NOT_UNDERGROUND` |
| `placement invalid …`, `no placeable spec` | `PLACEMENT_INVALID` |
| MCP-only | `CONFIRMATION_REQUIRED`, `CONFIRMATION_DECLINED`, `CONFIRMATION_EXPIRED`, `UNKNOWN_ERROR` |

```gherkin
Feature: Legible errors
  Scenario: Placement occupied
    Given the legacy payload {"error":"occupied by Path at (120,130,2). demolish it or try a different location","x":120,"y":130,"z":2,"prefab":"LumberjackFlag.IronTeeth"}
    Then code is "PLACEMENT_OCCUPIED"
    And reason is "occupied by Path at (120,130,2)"
    And hint is "demolish it or try a different location"
    And at is {x:120,y:130,z:2}
    And details.prefab is "LumberjackFlag.IronTeeth"

  Scenario: Speed out of range
    Given the legacy payload {"error":"invalid_param: speed must be 0-3 (0=pause, 1=normal, 2=fast, 3=fastest)","got":5}
    Then code is "INVALID_PARAM"
    And reason is "speed must be 0-3 (0=pause, 1=normal, 2=fast, 3=fastest)"
    And details.got is 5

  Scenario: Not an error
    Given the payload {"id":5,"name":"Path"}
    Then parsing reports no error
```
