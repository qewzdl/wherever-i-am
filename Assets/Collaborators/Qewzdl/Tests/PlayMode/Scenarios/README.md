# Real G bootstrap multiplayer scenarios

## Fast local MPPM probe

Open `GRealBootstrap.unity`, then create a Multiplayer Play Mode scenario with
three Editor instances using these tags:

- `GHost` for the main Editor;
- `GClient` for the first additional Editor;
- `GLateClient` for the second additional Editor.

Entering Play Mode runs the real Bootstrap scene independently in every process.
The scenario verifies Host, Client and late-client readiness, Lobby to InGame
phase synchronization, dynamic contract loss and coordinated shutdown.

## Production Player bootstrap

Use `Tools/Wherever I Am/Tests/Run Production Bootstrap` for the release-gate
scenario. It builds one test-only Development Player and starts three separate
Unity Player processes:

1. Host starts the real `Bootstrap.unity`, opens Lobby and waits for Client.
2. Host and Client commit Lobby, then move through the production Game flow.
3. LateClient starts only after Game and synchronizes directly into InGame.
4. All three processes run `ShutdownToMainMenuAsync` and verify NGO stop,
   Session scope cleanup and committed MainMenu.

Artifacts are written to `artifacts/production-bootstrap`. The Player harness is
compiled only with `WIA_PRODUCTION_BOOTSTRAP_TEST`; it is absent from normal
release builds.

For CI, install a licensed Unity 6000.0.73f1 Editor with Windows Build Support on
a self-hosted Windows runner. Set the repository variable `UNITY_EDITOR_PATH` if
Unity Hub is not installed in its default location. The
`Production Bootstrap` workflow runs automatically for pushes and pull requests
to `Qewzdl`.

## Network soak and fault injection

Use `Tools/Wherever I Am/Tests/Run Network Soak (15 min)` for the long-running
P1 multiplayer gate. It starts the real production Bootstrap in Host and two
Client processes, applies Unity Transport latency, jitter and packet loss, and
repeats Lobby to Game to MainMenu until the requested duration has elapsed.
When started from this Editor menu, all three Players open as 960x540 windows.
Each window shows its role, cycle, phase and network simulation. Use
`Stop network soak` in any window (or close it) to cancel the run and close the
remaining processes. CI and the PowerShell runner stay headless by default; set
`WIA_NETWORK_SOAK_SHOW_WINDOWS=1` to show their Player windows as well.

Across every four cycles Client B is disconnected during map loading, an active
objective, an authoritative drag and an enemy attack. The same process rejoins
the running Game after the objective, drag and enemy faults. An incomplete map
load is expected to fail closed, so all roles roll back to MainMenu and reconnect
in the next cycle. Every MainMenu return compares Global/Scene/Player/Session
scope counts, registration order, spawned NGO objects, live NetworkObjects and
active drag state with the clean startup baseline.

The default and CI duration is 900 seconds. A local smoke run can be selected
without weakening CI:

```powershell
$env:WIA_NETWORK_SOAK_SMOKE = "1"
$env:WIA_NETWORK_SOAK_DURATION_SECONDS = "90"
./ci/run-network-soak.ps1
```

Artifacts and NUnit XML are written to `artifacts/network-soak`. The
`Network Soak` workflow runs on pushes to `Qewzdl`, every night and manually.
