namespace Game.Net;

// One reliable, ordered message pipe between the host and a player: TCP between machines
// (TcpConnection), in memory for the host's own player and tests (LoopbackConnection).
// Polled from the frame loop: nothing here blocks or raises callbacks on other threads.
public interface IConnection
{
    // False once either side closed or the link broke; messages already received can still be read.
    bool IsOpen { get; }

    void Send(byte[] message);

    bool TryReceive(out byte[] message);

    void Close();
}
