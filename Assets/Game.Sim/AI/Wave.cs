using System.Collections.Generic;

namespace Game.Sim;

// A Legion wave on its way (legion-ai.md): its units and what they go for.
public sealed class Wave
{
    internal readonly List<int> _unitIds;

    internal Wave(int number, string target, int targetPlayerId, List<int> unitIds)
    {
        Number = number;
        Target = target;
        TargetPlayerId = targetPlayerId;
        _unitIds = unitIds;
    }

    public int Number { get; }

    // WaveStats.TargetEconomy or WaveStats.TargetBase.
    public string Target { get; }

    public int TargetPlayerId { get; internal set; }

    // Entity the wave is heading for now, 0 when it has none.
    public int TargetId { get; internal set; }

    // Alive members, ascending.
    public IReadOnlyList<int> UnitIds => _unitIds;
}
