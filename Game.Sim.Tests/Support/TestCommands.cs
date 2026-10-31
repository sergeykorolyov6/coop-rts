namespace Game.Sim.Tests;

internal static class TestCommands
{
    public static CommandCodec CreateCodec()
    {
        var codec = new CommandCodec();
        codec.Register(GrantCommand.Id, GrantCommand.Read);
        codec.Register(RandomGrantCommand.Id, RandomGrantCommand.Read);
        codec.Register(MarkCommand.Id, MarkCommand.Read);
        return codec;
    }
}
