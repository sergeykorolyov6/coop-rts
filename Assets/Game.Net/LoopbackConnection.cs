using System.Collections.Generic;

namespace Game.Net;

// Both ends of an in-memory connection: the host's own player talks to its Relay this way,
// and tests run whole matches without sockets.
public sealed class LoopbackConnection : IConnection
{
    private readonly Queue<byte[]> _inbox = new Queue<byte[]>();
    private LoopbackConnection? _other;
    private bool _closed;

    private LoopbackConnection() { }

    public static (LoopbackConnection A, LoopbackConnection B) CreatePair()
    {
        var a = new LoopbackConnection();
        var b = new LoopbackConnection();
        a._other = b;
        b._other = a;
        return (a, b);
    }

    public bool IsOpen => !_closed;

    public void Send(byte[] message)
    {
        if (_closed || _other == null || _other._closed)
            return;
        _other._inbox.Enqueue((byte[])message.Clone());
    }

    public bool TryReceive(out byte[] message)
    {
        if (_inbox.Count > 0)
        {
            message = _inbox.Dequeue();
            return true;
        }
        message = System.Array.Empty<byte>();
        return false;
    }

    public void Close()
    {
        _closed = true;
        if (_other != null)
            _other._closed = true;
    }
}
