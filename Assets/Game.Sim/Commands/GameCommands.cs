namespace Game.Sim;

// Every gameplay command type, for the network, replays and saves.
// Ids are stable forever; test-only commands start at 1000.
public static class GameCommands
{
    public static CommandCodec CreateCodec()
    {
        var codec = new CommandCodec();
        Register(codec);
        return codec;
    }

    public static void Register(CommandCodec codec)
    {
        codec.Register(MoveCommand.Id, MoveCommand.Read);
        codec.Register(MineCommand.Id, MineCommand.Read);
        codec.Register(DeliverCargoCommand.Id, DeliverCargoCommand.Read);
        codec.Register(BuildCommand.Id, BuildCommand.Read);
        codec.Register(RepairCommand.Id, RepairCommand.Read);
    }
}
