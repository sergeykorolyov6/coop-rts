namespace Game.Sim;

// A unit in flight over a conveyor bridge: its type and distance from the input end.
internal struct BridgeItem
{
    public BridgeItem(int type, int pos)
    {
        Type = type;
        Pos = pos;
    }

    public int Type;
    public int Pos;
}
