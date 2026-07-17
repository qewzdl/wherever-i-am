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
