namespace Game.Sim;

// One row of legion.csv: how strong the Black Legion is for a number of players (legion-ai.md).
public sealed class LegionStats
{
    internal LegionStats(JsonNode row)
    {
        Players = row.GetInt("players");
        StartPrimary = row.GetInt("start_primary");
        StartSecondary = row.GetInt("start_secondary");
        StartWorkers = row.GetInt("start_workers");
        WorkerLimit = row.GetInt("worker_limit");
        ArmyLimit = row.GetInt("army_limit");
        IncomePercent = row.GetInt("income_pct");
        WaveSizePercent = row.GetInt("wave_size_pct");
        WaveTimePercent = row.GetInt("wave_time_pct");
        RebuildDelayTicks = row.GetInt("rebuild_delay_ticks");
    }

    public int Players { get; }
    public int StartPrimary { get; }
    public int StartSecondary { get; }
    public int StartWorkers { get; }
    public int WorkerLimit { get; }

    // Fighting units alive plus those queued at factories.
    public int ArmyLimit { get; }

    // What the Legion's headquarters credits per 100 units it accepts.
    public int IncomePercent { get; }

    public int WaveSizePercent { get; }
    public int WaveTimePercent { get; }
    public int RebuildDelayTicks { get; }
}
