using System;
using System.Collections.Generic;
using System.IO;
using Game.Sim;

namespace Game.Net;

// network.md: the host's side. Gives places in the lobby, starts the match, closes every turn
// once it has every playing player's commands for it and sends that turn to all, compares
// StateHash. The host's own player is an ordinary LockstepClient connected to it in memory,
// so the host plays by exactly the same rules as everyone else. The host may pick a save instead
// of a mission: then everyone restores it and the match goes on from its tick with its factions.
public sealed class Relay
{
    private sealed class Peer
    {
        public Peer(IConnection connection) => Connection = connection;

        public IConnection Connection { get; }
        public bool Greeted { get; set; }
        public bool Gone { get; set; }
        public string Name { get; set; } = "";
        public string FactionId { get; set; } = "";
        public int Slot { get; set; }
    }

    // One turn being collected: whose input came, and the commands in slot, then arrival order.
    private sealed class PendingTurn
    {
        public readonly List<int> Arrived = new List<int>();
        public readonly List<(int Slot, int Seq, byte[] Command)> Commands = new List<(int, int, byte[])>();
    }

    private readonly GameData _data;
    private readonly MissionCatalog _catalog;
    private readonly ulong _contentHash;
    private readonly CommandCodec _codec = GameCommands.CreateCodec();
    private readonly List<Peer> _peers = new List<Peer>();
    private readonly SortedDictionary<int, PendingTurn> _turns = new SortedDictionary<int, PendingTurn>();
    private readonly SortedDictionary<int, SortedDictionary<int, ulong>> _hashes = new SortedDictionary<int, SortedDictionary<int, ulong>>();
    private int _lastClosedTurn = LockstepSettings.DelayTurns - 1;
    private bool _desyncSent;
    private SaveGame? _save;
    private byte[]? _saveBytes;

    public Relay(GameData data, MissionCatalog catalog, ulong contentHash, string missionId)
    {
        _data = data;
        _catalog = catalog;
        _contentHash = contentHash;
        MissionId = catalog.Find(missionId) != null ? missionId : throw new ArgumentException($"Unknown mission \"{missionId}\".", nameof(missionId));
    }

    public string MissionId { get; private set; }
    public bool Started { get; private set; }

    // The save the match will go on from; null for a new match.
    public SaveGame? Save => _save;

    // The host's own player left (its connection closed): the match is over for everyone.
    public bool Closed { get; private set; }

    public int PlayerCount
    {
        get
        {
            int n = 0;
            foreach (Peer _ in Playing())
                n++;
            return n;
        }
    }

    // Commands that could not be read and were dropped (network.md, Fail cases).
    public int DroppedCommands { get; private set; }

    // The first connection is the host's own player (slot 1, the one who picks the mission).
    public void Add(IConnection connection) => _peers.Add(new Peer(connection));

    public void Poll()
    {
        if (Closed)
            return;
        for (int i = 0; i < _peers.Count; i++)
        {
            Peer peer = _peers[i];
            if (peer.Gone)
                continue;
            while (!peer.Gone && peer.Connection.TryReceive(out byte[] message))
                Handle(peer, message);
            if (!peer.Gone && !peer.Connection.IsOpen)
                Leave(peer);
        }
        if (!Started)
            _peers.RemoveAll(p => p.Gone);
    }

    private void Handle(Peer peer, byte[] message)
    {
        try
        {
            using BinaryReader reader = Message.Open(message, out MessageType type);
            if (!peer.Greeted)
            {
                if (type == MessageType.Hello)
                    Greet(peer, reader);
                return;
            }
            switch (type)
            {
                case MessageType.PickFaction:
                    PickFaction(peer, reader.ReadString());
                    break;
                case MessageType.PickMission:
                    PickMission(peer, reader.ReadString());
                    break;
                case MessageType.PickSave:
                    PickSave(peer, Message.ReadBytes(reader));
                    break;
                case MessageType.StartRequest:
                    StartMatch(peer, reader.ReadInt32());
                    break;
                case MessageType.Input:
                    Input(peer, reader);
                    break;
                case MessageType.Hash:
                    Hash(peer, reader.ReadInt32(), reader.ReadUInt64());
                    break;
            }
        }
        catch (Exception e) when (e is IOException || e is InvalidDataException || e is EndOfStreamException)
        {
            // A message that cannot be read: the peer is broken, not the match.
            Leave(peer);
        }
    }

    private void Greet(Peer peer, BinaryReader reader)
    {
        int version = reader.ReadInt32();
        ulong content = reader.ReadUInt64();
        string name = reader.ReadString();
        string? why = version != LockstepSettings.ProtocolVersion || content != _contentHash ? "Другая версия игры"
            : Started || PlayerCount >= LockstepSettings.MaxPlayers ? "Мест нет"
            : null;
        if (why != null)
        {
            peer.Connection.Send(Message.Build(MessageType.Reject, w => w.Write(why)));
            peer.Connection.Close();
            peer.Gone = true;
            return;
        }
        peer.Greeted = true;
        peer.Name = name;
        Renumber();
        peer.FactionId = DefaultFaction(peer.Slot);
        BroadcastLobby();
    }

    private void PickFaction(Peer peer, string factionId)
    {
        // A save fixes the factions: the bases on the map are already theirs.
        if (Started || _save != null || !_data.TryGetFaction(factionId, out FactionStats? f) || f!.Role != FactionRole.Player)
            return;
        peer.FactionId = factionId;
        BroadcastLobby();
    }

    private void PickMission(Peer peer, string missionId)
    {
        if (Started || peer.Slot != 1 || _catalog.Find(missionId) == null)
            return;
        MissionId = missionId;
        _save = null;
        _saveBytes = null;
        foreach (Peer p in _peers)
        {
            if (p.Greeted && !p.Gone)
                p.FactionId = DefaultFaction(p.Slot);
        }
        BroadcastLobby();
    }

    // save-load.md + network.md: a save of a mission whose places the lobby can fill; empty — back
    // to a new match of the mission.
    private void PickSave(Peer peer, byte[] bytes)
    {
        if (Started || peer.Slot != 1)
            return;
        if (bytes.Length == 0)
        {
            PickMission(peer, MissionId);
            return;
        }
        SaveGame save;
        try
        {
            save = SaveGame.Read(bytes);
        }
        catch (InvalidDataException)
        {
            return;
        }
        if (_catalog.Find(save.MissionId) == null)
            return;
        _save = save;
        _saveBytes = bytes;
        MissionId = save.MissionId;
        foreach (Peer p in Playing())
            p.FactionId = DefaultFaction(p.Slot);
        BroadcastLobby();
    }

    // network.md: only slot 1, and only with exactly as many players as the mission has places.
    private void StartMatch(Peer peer, int seed)
    {
        MissionStats? mission = _catalog.Find(MissionId);
        if (Started || peer.Slot != 1 || mission == null || PlayerCount != mission.Players)
            return;
        Started = true;
        var factions = new List<string>();
        foreach (Peer p in Playing())
            factions.Add(p.FactionId);
        int startTick = _save?.Tick ?? 0;
        int matchSeed = _save?.Seed ?? seed;
        byte[] saveBytes = _saveBytes ?? Array.Empty<byte>();
        _lastClosedTurn = LockstepSettings.FirstTurn(startTick) + LockstepSettings.DelayTurns - 1;
        Broadcast(Message.Build(MessageType.Start, w =>
        {
            w.Write(MissionId);
            w.Write(matchSeed);
            w.Write(factions.Count);
            foreach (string f in factions)
                w.Write(f);
            w.Write(startTick);
            Message.WriteBytes(w, saveBytes);
        }));
    }

    private void Input(Peer peer, BinaryReader reader)
    {
        int turn = reader.ReadInt32();
        int count = reader.ReadInt32();
        var commands = new List<byte[]>();
        for (int i = 0; i < count; i++)
            commands.Add(Message.ReadBytes(reader));
        if (!Started || turn <= _lastClosedTurn)
            return;
        if (!_turns.TryGetValue(turn, out PendingTurn? pending))
        {
            pending = new PendingTurn();
            _turns.Add(turn, pending);
        }
        if (pending.Arrived.Contains(peer.Slot))
            return;
        pending.Arrived.Add(peer.Slot);
        foreach (byte[] command in commands)
        {
            // Read once here: what nobody can read must not reach the other machines.
            try
            {
                _codec.ReadAs(command, peer.Slot, 0);
                pending.Commands.Add((peer.Slot, pending.Commands.Count, command));
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is EndOfStreamException || e is ArgumentException)
            {
                DroppedCommands++;
            }
        }
        CloseTurns();
    }

    // Turns go out strictly in order; a turn waits for every player still in the match.
    private void CloseTurns()
    {
        while (_turns.TryGetValue(_lastClosedTurn + 1, out PendingTurn? pending) && HasAll(pending.Arrived))
        {
            int turn = ++_lastClosedTurn;
            _turns.Remove(turn);
            // Slot order, then the order each player gave them: the same on every machine.
            pending.Commands.Sort((a, b) => a.Slot != b.Slot ? a.Slot.CompareTo(b.Slot) : a.Seq.CompareTo(b.Seq));
            Broadcast(Message.Build(MessageType.Turn, w =>
            {
                w.Write(turn);
                w.Write(pending.Commands.Count);
                foreach ((int slot, _, byte[] command) in pending.Commands)
                {
                    w.Write(slot);
                    Message.WriteBytes(w, command);
                }
            }));
        }
    }

    private void Hash(Peer peer, int tick, ulong hash)
    {
        if (!Started)
            return;
        if (!_hashes.TryGetValue(tick, out SortedDictionary<int, ulong>? byPlayer))
        {
            byPlayer = new SortedDictionary<int, ulong>();
            _hashes.Add(tick, byPlayer);
        }
        byPlayer[peer.Slot] = hash;
        CompareHashes();
    }

    private void CompareHashes()
    {
        var done = new List<int>();
        foreach (KeyValuePair<int, SortedDictionary<int, ulong>> entry in _hashes)
        {
            var arrived = new List<int>(entry.Value.Keys);
            if (!HasAll(arrived))
                continue;
            done.Add(entry.Key);
            ulong? first = null;
            bool same = true;
            foreach (Peer p in Playing())
            {
                ulong h = entry.Value[p.Slot];
                same &= first == null || first == h;
                first ??= h;
            }
            if (!same && !_desyncSent)
            {
                _desyncSent = true;
                int tick = entry.Key;
                Broadcast(Message.Build(MessageType.Desync, w => w.Write(tick)));
            }
        }
        foreach (int tick in done)
            _hashes.Remove(tick);
    }

    private void Leave(Peer peer)
    {
        peer.Gone = true;
        peer.Connection.Close();
        if (!peer.Greeted)
            return;
        if (peer.Slot == 1)
        {
            // The host's own player is gone: nobody collects turns any more.
            Closed = true;
            foreach (Peer p in _peers)
                p.Connection.Close();
            return;
        }
        if (!Started)
        {
            Renumber();
            BroadcastLobby();
            return;
        }
        int slot = peer.Slot;
        Broadcast(Message.Build(MessageType.PlayerLeft, w => w.Write(slot)));
        // Its turns are no longer awaited.
        CloseTurns();
        CompareHashes();
    }

    private bool HasAll(List<int> arrived)
    {
        foreach (Peer p in Playing())
        {
            if (!arrived.Contains(p.Slot))
                return false;
        }
        return true;
    }

    private IEnumerable<Peer> Playing()
    {
        foreach (Peer p in _peers)
        {
            if (p.Greeted && !p.Gone)
                yield return p;
        }
    }

    // In the lobby places stay 1..N in order of arrival, so the host is always 1.
    private void Renumber()
    {
        int slot = 1;
        foreach (Peer p in Playing())
            p.Slot = slot++;
    }

    private string DefaultFaction(int slot)
    {
        if (_save != null && slot - 1 < _save.Factions.Count && _save.Factions[slot - 1].Length > 0)
            return _save.Factions[slot - 1];
        MissionStats? mission = _catalog.Find(MissionId);
        if (mission != null && slot - 1 < mission.Factions.Count)
            return mission.Factions[slot - 1];
        return _data.DefaultFaction(FactionRole.Player)?.Id ?? "";
    }

    private void BroadcastLobby()
    {
        foreach (Peer peer in Playing())
        {
            int you = peer.Slot;
            int saveTick = _save?.Tick ?? -1;
            peer.Connection.Send(Message.Build(MessageType.Lobby, w =>
            {
                w.Write(you);
                w.Write(MissionId);
                w.Write(saveTick);
                var playing = new List<Peer>(Playing());
                w.Write(playing.Count);
                foreach (Peer p in playing)
                {
                    w.Write(p.Slot);
                    w.Write(p.Name);
                    w.Write(p.FactionId);
                }
            }));
        }
    }

    private void Broadcast(byte[] message)
    {
        foreach (Peer p in Playing())
            p.Connection.Send(message);
    }
}
