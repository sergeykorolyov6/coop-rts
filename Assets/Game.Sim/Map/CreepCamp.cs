using System.Collections.Generic;

namespace Game.Sim;

// A creep camp point of interest (creep-camp.md). It has no owner: once no creep is left
// it works for everyone — the deposit next to it is safe and the trophy is up for grabs.
public sealed class CreepCamp
{
    internal readonly List<int> _creepIds;
    internal readonly List<int> _attackers = new List<int>();

    internal CreepCamp(int index, CreepCampStats stats, int centerX, int centerY, List<int> creepIds)
    {
        Index = index;
        Stats = stats;
        CenterX = centerX;
        CenterY = centerY;
        _creepIds = creepIds;
    }

    public int Index { get; }
    public CreepCampStats Stats { get; }

    // Center cell; the trophy lies here once the camp is cleared.
    public int CenterX { get; }
    public int CenterY { get; }
    public int CenterMilliX => Grid.CellCenter(CenterX);
    public int CenterMilliY => Grid.CellCenter(CenterY);

    public CreepCampState State { get; internal set; }

    // Creep unit ids in the camp type's creep order (dead ones stay listed).
    public IReadOnlyList<int> CreepIds => _creepIds;

    public int TrophyPrimary { get; internal set; }
    public int TrophySecondary { get; internal set; }

    // Last tick any of its creeps took a blow.
    public int LastDamageTick { get; internal set; } = Entity.NeverTick;

    // Whoever hit its creeps since it last calmed down: they are targets even outside the radius.
    public IReadOnlyList<int> Attackers => _attackers;
}
