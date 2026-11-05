using System;
using System.Collections.Generic;
using System.IO;
using Game.Sim;

namespace Game.Net;

// network.md: one player's side of the lockstep, the host's own player included. In the lobby it
// follows what the host says; in the match it lets the sim run a turn only once that turn has
// come from the host, sends its own commands DelayTurns ahead and reports StateHash.
public sealed class LockstepClient
{
    private readonly IConnection _connection;
    private readonly CommandCodec _codec;
    private readonly List<byte[]> _outgoing = new List<byte[]>();
    private readonly SortedDictionary<int, List<(int Slot, byte[] Command)>> _turns = new SortedDictionary<int, List<(int, byte[])>>();
    private readonly List<LobbyEntry> _lobby = new List<LobbyEntry>();
    private readonly List<NetNotice> _notices = new List<NetNotice>();

    public LockstepClient(IConnection connection, string name, ulong contentHash, CommandCodec codec)
    {
        _connection = connection;
        _codec = codec;
        _connection.Send(Message.Build(MessageType.Hello, w =>
        {
            w.Write(LockstepSettings.ProtocolVersion);
            w.Write(contentHash);
            w.Write(name);
        }));
    }

    // 0 until the host gives a place.
    public int Slot { get; private set; }
    public string MissionId { get; private set; } = "";
    public IReadOnlyList<LobbyEntry> Lobby => _lobby;

    // Set once the host starts the match.
    public MatchStart? Start { get; private set; }

    // The connection to the host is gone (or the host refused): no more turns will come.
    public bool Disconnected { get; private set; }

    // The sim stands because a turn has not come yet ("Ожидание игроков").
    public bool Waiting { get; private set; }

    // Turns received and not yet run: more than DelayTurns means this machine lags behind.
    public int TurnsAhead => _turns.Count;

    public void PickFaction(string factionId) => _connection.Send(Message.Build(MessageType.PickFaction, w => w.Write(factionId)));

    public void PickMission(string missionId) => _connection.Send(Message.Build(MessageType.PickMission, w => w.Write(missionId)));

    public void RequestStart(int seed) => _connection.Send(Message.Build(MessageType.StartRequest, w => w.Write(seed)));

    // Notices since the last call, oldest first.
    public List<NetNotice> TakeNotices()
    {
        var taken = new List<NetNotice>(_notices);
        _notices.Clear();
        return taken;
    }

    public void Poll()
    {
        if (Disconnected)
            return;
        while (_connection.TryReceive(out byte[] message))
        {
            try
            {
                Handle(message);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is EndOfStreamException)
            {
                Lost();
                return;
            }
        }
        if (!_connection.IsOpen && !Disconnected)
            Lost();
    }

    public void Leave()
    {
        _connection.Close();
        Disconnected = true;
    }

    // A command of the local player: goes out with the next turn's input. Its PlayerId and Tick
    // do not matter — the host stamps the sender, the turn decides the tick.
    public void Submit(ICommand command) => _outgoing.Add(_codec.Encode(command));

    // One tick of the match if the lockstep allows it. At a turn's first tick: the turn must have
    // come (the first DelayTurns turns are empty by agreement); its commands go into the sim, and
    // the local commands leave for the turn DelayTurns ahead.
    public bool TryStep(Simulation sim)
    {
        int tick = sim.State.Tick;
        if (tick % LockstepSettings.TurnTicks == 0)
        {
            int turn = tick / LockstepSettings.TurnTicks;
            if (turn >= LockstepSettings.DelayTurns)
            {
                if (!_turns.TryGetValue(turn, out List<(int Slot, byte[] Command)>? commands))
                {
                    Waiting = true;
                    return false;
                }
                _turns.Remove(turn);
                foreach ((int slot, byte[] command) in commands)
                    sim.Enqueue(_codec.ReadAs(command, slot, tick));
            }
            SendInput(turn + LockstepSettings.DelayTurns);
        }
        Waiting = false;
        sim.Tick();
        if (sim.State.Tick % (LockstepSettings.TurnTicks * LockstepSettings.HashEveryTurns) == 0)
        {
            int at = sim.State.Tick;
            ulong hash = sim.StateHash;
            _connection.Send(Message.Build(MessageType.Hash, w =>
            {
                w.Write(at);
                w.Write(hash);
            }));
        }
        return true;
    }

    private void SendInput(int turn)
    {
        var commands = new List<byte[]>(_outgoing);
        _outgoing.Clear();
        _connection.Send(Message.Build(MessageType.Input, w =>
        {
            w.Write(turn);
            w.Write(commands.Count);
            foreach (byte[] c in commands)
                Message.WriteBytes(w, c);
        }));
    }

    private void Handle(byte[] message)
    {
        using BinaryReader reader = Message.Open(message, out MessageType type);
        switch (type)
        {
            case MessageType.Reject:
                _notices.Add(new NetNotice(NetNoticeKind.Rejected, text: reader.ReadString()));
                _connection.Close();
                Disconnected = true;
                break;
            case MessageType.Lobby:
                Slot = reader.ReadInt32();
                MissionId = reader.ReadString();
                _lobby.Clear();
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                    _lobby.Add(new LobbyEntry(reader.ReadInt32(), reader.ReadString(), reader.ReadString()));
                break;
            case MessageType.Start:
                string missionId = reader.ReadString();
                int seed = reader.ReadInt32();
                var factions = new List<string>();
                int n = reader.ReadInt32();
                for (int i = 0; i < n; i++)
                    factions.Add(reader.ReadString());
                Start = new MatchStart(missionId, seed, factions);
                break;
            case MessageType.Turn:
                int turn = reader.ReadInt32();
                var commands = new List<(int, byte[])>();
                int k = reader.ReadInt32();
                for (int i = 0; i < k; i++)
                    commands.Add((reader.ReadInt32(), Message.ReadBytes(reader)));
                _turns[turn] = commands;
                break;
            case MessageType.Desync:
                _notices.Add(new NetNotice(NetNoticeKind.Desync, tick: reader.ReadInt32()));
                break;
            case MessageType.PlayerLeft:
                _notices.Add(new NetNotice(NetNoticeKind.PlayerLeft, slot: reader.ReadInt32()));
                break;
        }
    }

    private void Lost()
    {
        Disconnected = true;
        _notices.Add(new NetNotice(NetNoticeKind.HostLost));
    }
}
