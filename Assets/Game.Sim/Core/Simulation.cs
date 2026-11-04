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

    public void Tick()
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
