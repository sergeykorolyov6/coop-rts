namespace Game.Net;

// network.md, "Параметры": network constants, not balance — every machine of a match must agree,
// and the protocol version guards that.
public static class LockstepSettings
{
    // Bump on any change of the wire format or of the numbers below.
    public const int ProtocolVersion = 2;

    // Ticks per turn: commands of a turn run on its first tick.
    public const int TurnTicks = 2;

    // Turns between giving a command and running it: hides the round trip to the host.
    public const int DelayTurns = 3;

    // Machines compare StateHash every this many turns.
    public const int HashEveryTurns = 10;

    public const int DefaultPort = 7777;
    public const int MaxPlayers = 4;

    // A frame larger than this is a broken or hostile peer. Room for a long match's save (Start).
    public const int MaxMessageBytes = 16 << 20;

    // The first turn of a match that begins at startTick (0, or a save's tick): the lockstep's
    // turns before it never happen, and DelayTurns from it are empty by agreement.
    public static int FirstTurn(int startTick) => (startTick + TurnTicks - 1) / TurnTicks;
}
