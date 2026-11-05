namespace Game.Net;

// network.md, "Параметры": network constants, not balance — every machine of a match must agree,
// and the protocol version guards that.
public static class LockstepSettings
{
    // Bump on any change of the wire format or of the numbers below.
    public const int ProtocolVersion = 1;

    // Ticks per turn: commands of a turn run on its first tick.
    public const int TurnTicks = 2;

    // Turns between giving a command and running it: hides the round trip to the host.
    public const int DelayTurns = 3;

    // Machines compare StateHash every this many turns.
    public const int HashEveryTurns = 10;

    public const int DefaultPort = 7777;
    public const int MaxPlayers = 4;

    // A frame larger than this is a broken or hostile peer.
    public const int MaxMessageBytes = 1 << 20;
}
