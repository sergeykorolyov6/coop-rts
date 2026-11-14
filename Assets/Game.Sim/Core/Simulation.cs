using System;
using System.Collections.Generic;

namespace Game.Sim;

// Fixed-step simulation. The world changes only inside Tick(), and only through commands.
public sealed class Simulation
{
    public const int TicksPerSecond = 20;

    private readonly SortedDictionary<int, List<PendingCommand>> _pending =
        new SortedDictionary<int, List<PendingCommand>>();
    private readonly List<SimEvent> _events = new List<SimEvent>();
    private readonly StateHasher _hasher = new StateHasher();
    private readonly List<ICommand> _log = new List<ICommand>();
    private long _nextSequence;

    public Simulation(MatchSetup setup)
    {
        State = new SimState(setup);
        StateHash = _hasher.Compute(State);
    }

    // save-load.md, T5: everything the match has changed, as bytes; FromSnapshot brings it back.
    // Taken between ticks only.
    public byte[] WriteSnapshot() => Snapshot.Write(State);

    // The match of this setup as the snapshot left it. log: every command of the match so far (the
    // replay recorded later starts at tick 0); those for ticks still ahead wait for their tick.
    // Throws InvalidDataException when the snapshot is of another match or version.
    public static Simulation FromSnapshot(MatchSetup setup, byte[] snapshot, IReadOnlyList<ICommand> log)
    {
        var simulation = new Simulation(setup);
        Snapshot.Read(simulation.State, snapshot);
        foreach (ICommand command in log)
        {
            if (command.Tick < simulation.State.Tick)
                simulation._log.Add(command);
            else if (simulation.State.HasPlayer(command.PlayerId))
                simulation.Enqueue(command);
            else
                throw new System.IO.InvalidDataException($"Command from unknown player {command.PlayerId}.");
        }
        simulation.StateHash = simulation._hasher.Compute(simulation.State);
        return simulation;
    }

    public SimState State { get; }

    // Events raised during the last Tick().
    public IReadOnlyList<SimEvent> Events => _events;

    // Hash of the state after the last Tick() (or of the initial state before the first one).
    public ulong StateHash { get; private set; }

    // Every command accepted by Enqueue, in arrival order: with the setup it is the whole match
    // (save-load.md). The Legion's own commands never come through here.
    public IReadOnlyList<ICommand> CommandLog => _log;

    // Commands may arrive in any order and ahead of time. Within a tick they execute by
    // PlayerId and then in the order each player issued them, so network arrival order
    // between players never matters.
    public void Enqueue(ICommand command)
    {
        if (command.Tick < State.Tick)
            throw new ArgumentException($"Command for tick {command.Tick} arrived after it; next tick is {State.Tick}.", nameof(command));
        if (!State.HasPlayer(command.PlayerId))
            throw new ArgumentException($"Command from unknown player {command.PlayerId}.", nameof(command));

        if (!_pending.TryGetValue(command.Tick, out var commands))
        {
            commands = new List<PendingCommand>();
            _pending.Add(command.Tick, commands);
        }
        commands.Add(new PendingCommand(command, _nextSequence++));
        _log.Add(command);
    }

    public void Tick() => Step(computeHash: true);

    // save-load.md: replays up to the tick without hashing the ticks in between; events of those
    // ticks are not kept. StateHash is that of the last tick.
    public void FastForward(int tick)
    {
        while (State.Tick < tick)
            Step(computeHash: State.Tick + 1 == tick);
    }

    private void Step(bool computeHash)
    {
        _events.Clear();
        int tick = State.Tick;

        if (_pending.TryGetValue(tick, out var commands))
        {
            _pending.Remove(tick);
            commands.Sort(ComparePending);
            foreach (PendingCommand pending in commands)
                pending.Command.Execute(State, _events);
        }

        LegionAiSystem.Tick(State, _events);
        CreepCampSystem.BeginTick(State, _events);
        UnitSystem.Tick(State, _events);
        CreepCampSystem.EndTick(State);
        TurretSystem.Tick(State);
        Combat.ApplyHits(State, _events);
        FactionTraits.Regenerate(State);
        DemolishSystem.Tick(State, _events);
        BuilderSystem.Tick(State, _events);
        FactorySystem.Tick(State, _events);
        DrillSystem.Tick(State);
        ConveyorSystem.Tick(State);
        Movement.Separate(State);
        Movement.PushOutOfBlockedCells(State);
        CaptureSystem.Tick(State, _events);
        State.RemoveDead();
        MissionRules.Check(State, _events);
        State.Fog.Update(State);

        State.Tick = tick + 1;
        if (computeHash)
            StateHash = _hasher.Compute(State);
    }

    private static int ComparePending(PendingCommand a, PendingCommand b)
    {
        int byPlayer = a.Command.PlayerId.CompareTo(b.Command.PlayerId);
        return byPlayer != 0 ? byPlayer : a.Sequence.CompareTo(b.Sequence);
    }

    private readonly struct PendingCommand
    {
        public PendingCommand(ICommand command, long sequence)
        {
            Command = command;
            Sequence = sequence;
        }

        public ICommand Command { get; }
        public long Sequence { get; }
    }
}
