using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Game.Sim;

// Binary command format (little-endian, as BinaryWriter always is):
//   int typeId, int playerId, int tick, int payloadLength, payload bytes.
// The length prefix lets Read verify that a reader consumed exactly what the writer wrote.
public sealed class CommandCodec
{
    private readonly SortedDictionary<int, Func<int, int, BinaryReader, ICommand>> _readers =
        new SortedDictionary<int, Func<int, int, BinaryReader, ICommand>>();

    // read(playerId, tick, payload) must consume exactly the payload written by WritePayload.
    public void Register(int typeId, Func<int, int, BinaryReader, ICommand> read)
    {
        if (_readers.ContainsKey(typeId))
            throw new ArgumentException($"Command type {typeId} is already registered.", nameof(typeId));
        _readers.Add(typeId, read);
    }

    public void Write(BinaryWriter writer, ICommand command)
    {
        using var payload = new MemoryStream();
        using (var payloadWriter = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
            command.WritePayload(payloadWriter);

        writer.Write(command.TypeId);
        writer.Write(command.PlayerId);
        writer.Write(command.Tick);
        writer.Write(checked((int)payload.Length));
        writer.Write(payload.GetBuffer(), 0, (int)payload.Length);
    }

    public ICommand Read(BinaryReader reader)
    {
        int typeId = reader.ReadInt32();
        int playerId = reader.ReadInt32();
        int tick = reader.ReadInt32();
        int payloadLength = reader.ReadInt32();
        if (payloadLength < 0)
            throw new InvalidDataException($"Negative payload length {payloadLength}.");

        if (!_readers.TryGetValue(typeId, out var read))
            throw new InvalidDataException($"Unknown command type {typeId}.");

        byte[] payloadBytes = reader.ReadBytes(payloadLength);
        if (payloadBytes.Length != payloadLength)
            throw new InvalidDataException($"Command type {typeId}: payload truncated.");

        using var payload = new MemoryStream(payloadBytes, writable: false);
        using var payloadReader = new BinaryReader(payload);
        ICommand command = read(playerId, tick, payloadReader);
        if (payload.Position != payloadLength)
            throw new InvalidDataException($"Command type {typeId}: reader consumed {payload.Position} of {payloadLength} payload bytes.");
        return command;
    }

    public byte[] WriteLog(IReadOnlyList<ICommand> commands)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(commands.Count);
            foreach (ICommand command in commands)
                Write(writer, command);
        }
        return stream.ToArray();
    }

    public List<ICommand> ReadLog(byte[] log)
    {
        using var stream = new MemoryStream(log, writable: false);
        using var reader = new BinaryReader(stream);
        int count = reader.ReadInt32();
        if (count < 0)
            throw new InvalidDataException($"Negative command count {count}.");

        var commands = new List<ICommand>(count);
        for (int i = 0; i < count; i++)
            commands.Add(Read(reader));
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Trailing bytes after the command log.");
        return commands;
    }
}
