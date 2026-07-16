# Real G bootstrap multiplayer scenario

Open `GRealBootstrap.unity`, then create a Multiplayer Play Mode scenario with
three Editor instances using these tags:

- `GHost` for the main Editor;
- `GClient` for the first additional Editor;
- `GLateClient` for the second additional Editor.

Entering Play Mode runs the real Bootstrap scene independently in every process.
The scenario verifies Host, Client and late-client readiness, Lobby to InGame
phase synchronization, dynamic contract loss and coordinated shutdown.
