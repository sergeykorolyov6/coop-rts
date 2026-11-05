using System;
using System.Net;
using System.Net.Sockets;

namespace Game.Net;

// The host's listening socket; Relay gets every accepted connection.
public sealed class TcpServer : IDisposable
{
    private readonly TcpListener _listener;

    // port 0 picks a free one (tests).
    public TcpServer(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    public bool TryAccept(out IConnection? connection)
    {
        connection = null;
        if (!_listener.Pending())
            return false;
        connection = new TcpConnection(_listener.AcceptSocket());
        return true;
    }

    public void Dispose() => _listener.Stop();
}
