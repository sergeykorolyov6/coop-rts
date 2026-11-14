using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Game.Sim;

// save-load.md: a save is a recording — mission, seed, the faction of every start slot and every
// player command up to Tick — plus the StateHash at Tick to check that replaying it gives the same
// match. A replay is the same recording taken when the match ends, watched from tick 0.
// T5: a save also carries a snapshot of the state at Tick, so it loads at once; the recording
// stays as the fallback and for the replay.
public sealed class SaveGame
{
    private const string Magic = "COOPSAVE";

    // 3: a snapshot of the state after the command log. 2: factions of the start slots (chosen in
    // the network lobby, network.md). 1 and 2 are still read.
    public const int FormatVersion = 3;

    private readonly List<string> _factions;

    public SaveGame(string missionId, int seed, int tick, ulong stateHash, byte[] commandLog, IReadOnlyList<string>? factions = null, byte[]? snapshot = null)
    {
        MissionId = missionId;
        Seed = seed;
        Tick = tick;
        StateHash = stateHash;
        CommandLog = commandLog;
        _factions = factions != null ? new List<string>(factions) : new List<string>();
        Snapshot = snapshot ?? Array.Empty<byte>();
    }

    // Simulation.WriteSnapshot at Tick; empty in a replay and in formats 1 and 2.
    public byte[] Snapshot { get; }
    public bool HasSnapshot => Snapshot.Length > 0;

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

    // snapshot: false for a replay — it is watched from tick 0 and the file stays small.
    public static SaveGame Capture(Simulation simulation, string missionId, int seed, CommandCodec codec, bool snapshot = true)
    {
        var factions = new List<string>();
        foreach (int playerId in simulation.State.TeamPlayerIds)
            factions.Add(simulation.State.GetPlayer(playerId).Faction?.Id ?? "");
        return new SaveGame(missionId, seed, simulation.State.Tick, simulation.StateHash, codec.WriteLog(simulation.CommandLog), factions,
            snapshot ? simulation.WriteSnapshot() : null);
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
            writer.Write(Snapshot.Length);
            writer.Write(Snapshot);
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
            if (version < 1 || version > FormatVersion)
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
            if (log.Length != length)
                throw new InvalidDataException("Save is truncated.");
            byte[]? snapshot = null;
            if (version >= 3)
            {
                int size = reader.ReadInt32();
                if (size < 0)
                    throw new InvalidDataException("Negative snapshot length.");
                snapshot = reader.ReadBytes(size);
                if (snapshot.Length != size)
                    throw new InvalidDataException("Save is truncated.");
            }
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Save has trailing bytes.");
            return new SaveGame(mission, seed, tick, hash, log, factions, snapshot);
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

    // T5: the match straight from the snapshot, or null when the save has none or it does not fit
    // (another version of the game, damaged bytes, another StateHash) — then replay the recording.
    public Simulation? TryRestoreSnapshot(MatchSetup setup, CommandCodec codec)
    {
        if (!HasSnapshot)
            return null;
        try
        {
            Simulation simulation = Simulation.FromSnapshot(setup, Snapshot, codec.ReadLog(CommandLog));
            return Matches(simulation) ? simulation : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    // The match at Tick: from the snapshot when it fits, else by replaying the commands from the
    // mission's start. Throws InvalidDataException when the result does not match the recorded
    // StateHash (another version of the rules or the data).
    public Simulation Restore(MatchSetup setup, CommandCodec codec)
    {
        Simulation? restored = TryRestoreSnapshot(setup, codec);
        if (restored != null)
            return restored;
        Simulation simulation = BeginReplay(setup, codec);
        simulation.FastForward(Tick);
        if (!Matches(simulation))
            throw new InvalidDataException("Replaying the save gives another match: the game's rules or data changed.");
        return simulation;
    }
}
