using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Game.Net;

// Connecting to a host without blocking the frame: poll IsDone, then take Connection or Error.
public sealed class TcpConnector
{
    private readonly Socket _socket;
    private readonly Task _task;

    public TcpConnector(string host, int port)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _task = _socket.ConnectAsync(host, port);
    }

    public bool IsDone => _task.IsCompleted;

    // After IsDone: the connection, or null and Error says why.
    public IConnection? Connection => _task.Status == TaskStatus.RanToCompletion ? new TcpConnection(_socket) : null;

    public string? Error => _task.Exception?.GetBaseException().Message;

    public void Cancel()
    {
        try
        {
            _socket.Close();
        }
        catch (ObjectDisposedException)
        {
            // Closed already.
        }
    }
}
