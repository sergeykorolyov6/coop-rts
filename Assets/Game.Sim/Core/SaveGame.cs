using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Game.Sim;

// save-load.md: a save is a recording — mission, seed, the faction of every start slot and every
// player command up to Tick — plus the StateHash at Tick to check that replaying it gives the same
// match. A replay is the same recording taken when the match ends, watched from tick 0.
public sealed class SaveGame
{
    private const string Magic = "COOPSAVE";

    // 2: factions of the start slots (chosen in the network lobby, network.md). 1 is still read.
    public const int FormatVersion = 2;

    private readonly List<string> _factions;

    public SaveGame(string missionId, int seed, int tick, ulong stateHash, byte[] commandLog, IReadOnlyList<string>? factions = null)
    {
        MissionId = missionId;
        Seed = seed;
        Tick = tick;
        StateHash = stateHash;
        CommandLog = commandLog;
        _factions = factions != null ? new List<string>(factions) : new List<string>();
    }

    public string MissionId { get; }
    public int Seed { get; }
    public int Tick { get; }
    public ulong StateHash { get; }

    // CommandCodec.WriteLog of Simulation.CommandLog.
    public byte[] CommandLog { get; }

    // Faction of slot 1, 2, … as the match had them; empty in format 1 — the mission's own.
    public IReadOnlyList<string> Factions => _factions;

    // The factions to launch with (MissionLauncher.CreateSetup): null — the mission's own.
    public IReadOnlyList<string>? FactionsOrNull => _factions.Count > 0 ? _factions : null;

    public static SaveGame Capture(Simulation simulation, string missionId, int seed, CommandCodec codec)
    {
        var factions = new List<string>();
        foreach (int playerId in simulation.State.TeamPlayerIds)
            factions.Add(simulation.State.GetPlayer(playerId).Faction?.Id ?? "");
        return new SaveGame(missionId, seed, simulation.State.Tick, simulation.StateHash, codec.WriteLog(simulation.CommandLog), factions);
    }

    public byte[] Write()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes(Magic));
            writer.Write(FormatVersion);
            writer.Write(MissionId);
            writer.Write(Seed);
            writer.Write(_factions.Count);
            foreach (string f in _factions)
                writer.Write(f);
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
            if (version != 1 && version != FormatVersion)
                throw new InvalidDataException($"Save format {version}, expected {FormatVersion}.");
            string mission = reader.ReadString();
            int seed = reader.ReadInt32();
            var factions = new List<string>();
            if (version >= 2)
            {
                int count = reader.ReadInt32();
                if (count < 0 || count > 64)
                    throw new InvalidDataException($"Bad faction count {count}.");
                for (int i = 0; i < count; i++)
                    factions.Add(reader.ReadString());
            }
            int tick = reader.ReadInt32();
            ulong hash = reader.ReadUInt64();
            int length = reader.ReadInt32();
            if (length < 0)
                throw new InvalidDataException("Negative command log length.");
            byte[] log = reader.ReadBytes(length);
            if (log.Length != length || stream.Position != stream.Length)
                throw new InvalidDataException("Save is truncated or has trailing bytes.");
            return new SaveGame(mission, seed, tick, hash, log, factions);
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("Save is truncated.", e);
        }
    }

    // The match at tick 0 with every recorded command waiting for its tick: run it to Tick to load
    // (FastForward, in parts if the frame must stay alive) or watch it tick by tick as a replay.
    public Simulation BeginReplay(MatchSetup setup, CommandCodec codec)
    {
        var simulation = new Simulation(setup);
        foreach (ICommand command in codec.ReadLog(CommandLog))
        {
            if (command.Tick >= Tick)
                throw new InvalidDataException($"Command for tick {command.Tick} at or after the save tick {Tick}.");
            simulation.Enqueue(command);
        }
        return simulation;
    }

    // At Tick and with the recorded StateHash: the recording played back to the same match.
    public bool Matches(Simulation simulation) =>
        simulation.State.Tick == Tick && simulation.StateHash == StateHash;

    // Replays the commands from the mission's start up to Tick. Throws InvalidDataException when the
    // result does not match the recorded StateHash (another version of the rules or the data).
    public Simulation Restore(MatchSetup setup, CommandCodec codec)
    {
        Simulation simulation = BeginReplay(setup, codec);
        simulation.FastForward(Tick);
        if (!Matches(simulation))
            throw new InvalidDataException("Replaying the save gives another match: the game's rules or data changed.");
        return simulation;
    }
}
