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
| Maps and objective definitions | Yes | Host flow | Indirect | Baseline validation |
| Chat validation and data | Yes | Session flow | Yes | Characterized |
| Lobby rules | Yes | Session flow | Yes | Characterized |
| Audio selectors and configuration | Yes | Bootstrap composition | Manual | Characterized |
| Pause service | Yes | Game scene | N/A | Characterized |
| Enemy memory and stimulus decisions | Yes | Contract loss | Partial | Characterized |
| Player signals and orchestration | Yes | Player scope | Partial | Characterized |
| Items and interaction | Data helpers | Asset validation | Partial | Needs deeper NGO tests |
| UI presentation | Asset validation | Startup/error UI | N/A | Manual visual check |
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
2. Add real item pickup/drag/drop ownership tests with two clients.
3. Add enemy perception, attack, and objective progression tests on a baked
   NavMesh test scene.
4. Add screenshot/audio-routing smoke checks only where deterministic assertions
   are possible; keep subjective presentation in the manual smoke route.
