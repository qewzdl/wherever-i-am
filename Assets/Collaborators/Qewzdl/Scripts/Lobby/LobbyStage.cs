using UnityEngine;

// The room behind the column: one place to stand per seat, lit or unlit by who
// is actually in the lobby.
//
// These are stand-ins, not the players. The real player object is a first
// person rig - two cameras, its own lights, its own input - and putting it on a
// stage would mean switching most of it off and then back on again for the sake
// of a silhouette. What stands here reads the same list the column reads and
// knows nothing else, which is also why it costs nothing when a lobby is
// hosted and never joined.
//
// The seats are objects in the scene rather than a prefab and a set of marks.
// A prefab buys reuse of a thing used once, and the scene has to hold four
// positioned objects either way - so the position and the thing standing on it
// are the same object, and swapping a capsule for a character is done by
// replacing what is under the seat rather than by wiring anything.
[DisallowMultipleComponent]
public sealed class LobbyStage : MonoBehaviour
{
    [Header("Seats")]
    [Tooltip("One per place at the table, in the order players fill them.")]
    [SerializeField] private GameObject[] seats = new GameObject[0];

    [Header("Look")]
    // Ink and bone, the same two the interface is built from. A stage lit in
    // colours the menu does not use is a stage that looks pasted on.
    [SerializeField] private Color waitingColor = new Color(0.227f, 0.251f, 0.290f);
    [SerializeField] private Color readyColor = new Color(0.929f, 0.914f, 0.875f);

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private ILobbyReadService readService;
    private Renderer[] seatRenderers;
    private MaterialPropertyBlock colorBlock;

    public void Construct(ILobbyReadService readService)
    {
        if (this.readService != null)
            this.readService.LobbyChanged -= Refresh;

        this.readService = readService;

        if (this.readService != null)
            this.readService.LobbyChanged += Refresh;

        Refresh();
    }

    public void Dispose()
    {
        if (readService != null)
            readService.LobbyChanged -= Refresh;

        readService = null;
    }

    private void Awake()
    {
        CacheSeatRenderers();
        ClearStage();
    }

    private void OnDestroy()
    {
        Dispose();
    }

    // Looked up once. A seat is whatever object was put there, so the renderer
    // may be on it or under it, and neither answer changes while the lobby is
    // open.
    private void CacheSeatRenderers()
    {
        seatRenderers = new Renderer[seats.Length];

        for (int i = 0; i < seats.Length; i++)
        {
            if (seats[i] != null)
                seatRenderers[i] = seats[i].GetComponentInChildren<Renderer>(true);
        }
    }

    // Empty until somebody is in the lobby. A room that starts full and empties
    // itself on the first update is a room that flickers on the way in.
    private void ClearStage()
    {
        for (int i = 0; i < seats.Length; i++)
        {
            if (seats[i] != null)
                seats[i].SetActive(false);
        }
    }

    private void Refresh()
    {
        if (readService == null)
            return;

        int playerCount = readService.PlayerCount;

        for (int i = 0; i < seats.Length; i++)
        {
            GameObject seat = seats[i];

            if (seat == null)
                continue;

            // More players than places is a room configured for more than it
            // was built for. The list in the column still counts them all; the
            // stage simply runs out of chairs, which is the failure that costs
            // nothing.
            bool occupied = i < playerCount;

            seat.SetActive(occupied);

            if (occupied)
                SetSeatReady(i, readService.GetPlayer(i).IsReady);
        }
    }

    // Tinted through a property block rather than through the material: one
    // material shared by every seat stays one material, and nothing is left
    // behind in the project when play mode ends.
    private void SetSeatReady(int index, bool isReady)
    {
        Renderer seatRenderer = seatRenderers[index];

        if (seatRenderer == null)
            return;

        colorBlock ??= new MaterialPropertyBlock();

        seatRenderer.GetPropertyBlock(colorBlock);
        colorBlock.SetColor(BaseColorId, isReady ? readyColor : waitingColor);
        seatRenderer.SetPropertyBlock(colorBlock);
    }
}
