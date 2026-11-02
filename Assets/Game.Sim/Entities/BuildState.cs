namespace Game.Sim;

// "Запланирован" lives on the worker (PlannedBuild) until the site starts; "Разрушен" is removal.
public enum BuildState : byte
{
    Constructing,
    Frozen,
    Complete,
}
