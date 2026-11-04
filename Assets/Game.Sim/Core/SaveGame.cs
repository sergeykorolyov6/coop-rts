using System;
using System.IO;
using System.Text;

namespace Game.Sim;

// save-load.md: a save is a recording — mission, seed and every player command up to Tick —
// plus the StateHash at Tick to check that replaying it gives the same match.
public sealed class SaveGame
{
    private const string Magic = "COOPSAVE";
    public const int FormatVersion = 1;

    public SaveGame(string missionId, int seed, int tick, ulong stateHash, byte[] commandLog)
    {
        MissionId = missionId;
        Seed = seed;
        Tick = tick;
        StateHash = stateHash;
        CommandLog = commandLog;
    }

    public string MissionId { get; }
    public int Seed { get; }
    public int Tick { get; }
    public ulong StateHash { get; }

    // CommandCodec.WriteLog of Simulation.CommandLog.
    public byte[] CommandLog { get; }

    public static SaveGame Capture(Simulation simulation, string missionId, int seed, CommandCodec codec) =>
        new SaveGame(missionId, seed, simulation.State.Tick, simulation.StateHash, codec.WriteLog(simulation.CommandLog));

    public byte[] Write()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes(Magic));
            writer.Write(FormatVersion);
            writer.Write(MissionId);
            writer.Write(Seed);
            writer.Write(Tick);
            writer.Write(StateHash);
            writer.Write(CommandLog.Length);
            writer.Write(CommandLog);
        }
        return stream.ToArray();
    }

    public static SaveGame Read(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (Encoding.ASCII.GetString(reader.ReadBytes(Magic.Length)) != Magic)
                throw new InvalidDataException("Not a CoopRTS save.");
            int version = reader.ReadInt32();
            if (version != FormatVersion)
                throw new InvalidDataException($"Save format {version}, expected {FormatVersion}.");
            string mission = reader.ReadString();
            int seed = reader.ReadInt32();
            int tick = reader.ReadInt32();
            ulong hash = reader.ReadUInt64();
            int length = reader.ReadInt32();
            if (length < 0)
                throw new InvalidDataException("Negative command log length.");
            byte[] log = reader.ReadBytes(length);
            if (log.Length != length || stream.Position != stream.Length)
                throw new InvalidDataException("Save is truncated or has trailing bytes.");
            return new SaveGame(mission, seed, tick, hash, log);
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("Save is truncated.", e);
        }
    }

    // Replays the commands from the mission's start up to Tick. Throws InvalidDataException when the
    // result does not match the recorded StateHash (another version of the rules or the data).
    public Simulation Restore(MatchSetup setup, CommandCodec codec)
    {
        var simulation = new Simulation(setup);
        foreach (ICommand command in codec.ReadLog(CommandLog))
        {
            if (command.Tick >= Tick)
                throw new InvalidDataException($"Command for tick {command.Tick} at or after the save tick {Tick}.");
            simulation.Enqueue(command);
        }
        while (simulation.State.Tick < Tick)
            simulation.Tick();
        if (simulation.StateHash != StateHash)
            throw new InvalidDataException("Replaying the save gives another match: the game's rules or data changed.");
        return simulation;
    }
}
