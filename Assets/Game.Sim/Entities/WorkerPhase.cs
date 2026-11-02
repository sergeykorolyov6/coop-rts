namespace Game.Sim;

// Stage of the mining / carrying loop (manual-mining.md, states).
public enum WorkerPhase : byte
{
    None,
    GoingToOre,
    Digging,
    Carrying,
}
