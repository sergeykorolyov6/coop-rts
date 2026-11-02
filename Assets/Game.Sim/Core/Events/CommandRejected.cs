namespace Game.Sim;

// A command (or one unit's part of it) could not be carried out; nothing was paid.
public sealed class CommandRejected : SimEvent
{
    public CommandRejected(int tick, int playerId, RejectReason reason, int unitId = 0)
        : base(tick)
    {
        PlayerId = playerId;
        Reason = reason;
        UnitId = unitId;
    }

    public int PlayerId { get; }
    public RejectReason Reason { get; }

    // The unit the rejection is about, 0 when it concerns the whole command.
    public int UnitId { get; }
}
