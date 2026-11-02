namespace Game.Sim;

// conveyors.md, "Склад на линии" (decision 28): takes from lines and drills, gives to
// neighbouring lines and bridges like a drill; never credits a wallet.
public sealed class Storage
{
    public int Primary { get; internal set; }
    public int Secondary { get; internal set; }
    public int Total => Primary + Secondary;

    internal int OutputCursor { get; set; }

    internal void Add(int type)
    {
        if (type == Resources.Secondary)
            Secondary++;
        else
            Primary++;
    }
}
