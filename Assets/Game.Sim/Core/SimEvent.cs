namespace Game.Sim;

// The only output of the simulation besides its state ("place taken", "drill attacked", ...).
// Presentation shows events; it never derives them from state on its own.
public abstract class SimEvent
{
    protected SimEvent(int tick)
    {
        Tick = tick;
    }

    public int Tick { get; }
}
