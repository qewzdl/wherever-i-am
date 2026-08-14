# Regression baseline

This document defines the minimum evidence required before the project is
treated as stable. A green test run protects the current behaviour; it does not
replace visual, audio, input, or multiplayer smoke checks.

## Required checks

Run these checks before merging runtime changes:

1. Unity compiles all player, runtime, editor, EditMode test, and PlayMode test
   assemblies without errors.
2. All EditMode tests pass.
3. All PlayMode tests pass.
4. The production bootstrap acceptance check passes. It runs on every pull
   request, so this is normally read rather than run.
5. Complete the manual smoke route:
   `Bootstrap -> MainMenu -> Host -> Lobby -> Game -> shutdown -> MainMenu`.

Save the exported Unity test result next to the commit or CI run that produced
it. A baseline is only valid for the exact source revision that was tested.

## Verified local baseline

Revision `28c06ee` was verified with Unity `6000.0.73f1`:

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| EditMode | 256 | 0 | 0 |
| PlayMode | 132 | 0 | 0 |

The PlayMode run includes real UTP host/client/late-client transport, a
dedicated server with two item clients, NGO spawn/despawn and ownership paths,
coordinated shutdown, scene lifecycle, and deterministic Unity physics/UI
lifecycle tests. Its largest single fixture is `EnemyBakedNavMeshPlayModeTests`,
which drives perception, the attack pipeline, navigation around items, doors and
hiding places, and the investigation search on a prebuilt NavMesh.

The multi-process acceptance checks are not part of either suite. They build a
Player and drive several real processes, and they run from CI rather than from
the Test Runner - see the section below.

## Coverage matrix

| System | EditMode | PlayMode | Multiplayer | Current status |
|---|---:|---:|---:|---|
| G publication and diagnostics | Yes | Yes | Indirect | Covered |
| ServiceScope and ownership policies | Yes | Indirect | Indirect | Covered |
| Global/Session/Player scopes | Yes | Yes | Indirect | Covered |
| Scene feature transactions | Yes | Yes | Indirect | Covered |
| Readiness and state commit isolation | Yes | Yes | Yes | Covered |
| NGO shutdown and MainMenu activation | Yes | Yes | Host/client transport | Covered |
| Project scenes and serialized references | Yes | Startup | N/A | Baseline validation |
| Network prefab catalog | Yes | Spawn paths | Yes | Baseline validation |
| Maps and objective definitions | Yes | Host flow | Indirect | Covered |
| Chat validation, unread state and phone UI | Yes | Yes | Yes | Covered |
| Lobby rules, settings and ownership | Yes | Yes | Yes | Covered |
| Audio selectors and configuration | Yes | Bootstrap composition | Manual listening | Covered deterministically |
| Pause service | Yes | Game scene | N/A | Characterized |
| Enemy memory, perception points and attack pipeline | Yes | NGO host attack | Host | Covered deterministically |
| Player signals, input blockers, posture and camera | Yes | Yes | Player scopes | Covered deterministically |
| Doors and item requirements | Yes | Physics/lifecycle | Server rules | Covered deterministically |
| Item pickup/drag/drop ownership | Data helpers | Physics/lifecycle | Dedicated server + 2 clients | Covered |
| UI state, layout and events | Yes | Yes | N/A | Manual visual check remains |
| Full production bootstrap | N/A | Host | Built Player, 3 processes | Automated in CI |

## Test policy for future changes

- A new service requires ownership-policy, registration, resolution, rollback,
  and disposal coverage.
- A new `NetworkBehaviour` requires spawn, despawn, host/client, late-join, and
  stale-registration coverage where applicable.
- A new scene feature requires validation, install, rollback, reverse uninstall,
  and missing-contract coverage.
- Server-authoritative gameplay requires a pure decision test and a PlayMode or
  multiplayer integration test.
- Every fixed bug requires a regression test that fails without the fix.
- New scenes, prefabs, maps, objectives, audio configs, and network prefabs must
  remain covered by project asset validation.

## Multi-process acceptance checks

Both build a Player and drive several real processes, so neither belongs to the
Test Runner. They are reached from CI, and by hand through
`./ci/run-production-bootstrap.ps1` and `./ci/run-network-soak.ps1`, which call
Unity with `-executeMethod`. Results land under `artifacts/`.

| Check | What it drives | Runs on |
|---|---|---|
| `ProductionBootstrapCi` | Host, client and a late client from bootstrap through a match to shutdown | Pull request, push, manually |
| `NetworkSoakCi` | Cycles of host, two clients, a fault during map load and a reconnect, on a link with 80 ms latency, 20 ms jitter and 2% loss | Push, nightly, manually |

The soak also has a ninety second smoke form for the editor. Both are behind
`Tools/Wherever I Am/Tests/`.

## Remaining high-value automation

1. Add screenshot/audio-routing smoke checks only where deterministic assertions
   are possible; keep subjective presentation in the manual smoke route.

This is not a gap in unit-testable pure logic. It needs rendered frames, audio
devices or human perception, and therefore belongs to acceptance testing.
