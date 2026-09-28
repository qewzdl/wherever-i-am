// Whether a player is still in the match. Asked by the server before it acts
// on anything the player requests - here, rather than of the attack receiver
// that knows the answer, because the code doing the asking lives where that
// receiver cannot be seen.
public interface IPlayerInPlay
{
    bool IsInPlay { get; }
}
