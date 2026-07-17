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
4. The real Multiplayer Play Mode scenario passes with `GHost`, `GClient`, and
   `GLateClient`.
5. Complete the manual smoke route:
   `Bootstrap -> MainMenu -> Host -> Lobby -> Game -> shutdown -> MainMenu`.

Save the exported Unity test result next to the commit or CI run that produced
it. A baseline is only valid for the exact source revision that was tested.

## Verified local baseline

The current working tree was verified with Unity `6000.0.73f1`:

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| EditMode | 161 | 0 | 0 |
| PlayMode | 45 | 0 | 0 |

The PlayMode run includes real UTP host/client/late-client transport, a
dedicated server with two item clients, NGO spawn/despawn and ownership paths,
coordinated shutdown, scene lifecycle, and deterministic Unity physics/UI
lifecycle tests. The separate `GRealBootstrap` Multiplayer Play Mode scenario
remains the production multi-process acceptance check.

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
| Full production bootstrap | N/A | Host | Manual scenario | Needs automated multi-process CI |

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

## Remaining high-value automation

1. Run `GRealBootstrap.unity` automatically in separate Unity processes in CI.
2. Add enemy perception, attack, and objective progression tests on a baked
   NavMesh test scene.
3. Add screenshot/audio-routing smoke checks only where deterministic assertions
   are possible; keep subjective presentation in the manual smoke route.

These items are not gaps in unit-testable pure logic. They need a real
multi-process player, baked scene data, rendered frames, audio devices, or
human perception and therefore belong to integration/acceptance testing.
