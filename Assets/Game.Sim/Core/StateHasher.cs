using System;
using System.IO;
using System.Text;

namespace Game.Sim;

// Serializes the key state fields in a fixed order and hashes them with XXH64.
// Compared every tick to catch desyncs in tests and over the network.
// Every new piece of state must be added here, or desyncs in it go unnoticed.
internal sealed class StateHasher
{
    private readonly MemoryStream _buffer = new MemoryStream();
    private readonly BinaryWriter _writer;

    public StateHasher()
    {
        _writer = new BinaryWriter(_buffer, Encoding.UTF8, leaveOpen: true);
    }

    public ulong Compute(SimState state)
    {
        _buffer.SetLength(0);

        _writer.Write(state.Tick);
        _writer.Write(state.Rng.State);
        _writer.Write(state.Grid.Width);
        _writer.Write(state.Grid.Height);
        _writer.Write(state.ResourceTypeCount);

        _writer.Write(state.Players.Count);
        foreach (Player player in state.Players)
        {
            _writer.Write(player.Id);
            for (int type = 0; type < player.Wallet.ResourceTypeCount; type++)
                _writer.Write(player.Wallet.Get(type));
        }

        for (int a = 0; a < state.Relations.PlayerCount; a++)
        {
            for (int b = 0; b < state.Relations.PlayerCount; b++)
                _writer.Write((byte)state.Relations.GetByIndex(a, b));
        }

        _writer.Flush();
        return XxHash64.Compute(new ReadOnlySpan<byte>(_buffer.GetBuffer(), 0, (int)_buffer.Length));
    }
}
