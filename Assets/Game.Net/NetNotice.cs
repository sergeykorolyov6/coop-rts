namespace Game.Net;

public enum NetNoticeKind
{
    Rejected,
    PlayerLeft,
    Desync,
    HostLost,
}

// Something the player must be told (network.md, "Что нужно от UI"); Text only for Rejected.
public sealed class NetNotice
{
    public NetNotice(NetNoticeKind kind, int slot = 0, int tick = 0, string text = "")
    {
        Kind = kind;
        Slot = slot;
        Tick = tick;
        Text = text;
    }

    public NetNoticeKind Kind { get; }
    public int Slot { get; }
    public int Tick { get; }
    public string Text { get; }
}
