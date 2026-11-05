using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Game.Net;
using Game.Sim;

namespace Game.Presentation
{
    // network.md: this machine's part in a network game, from the lobby to the end of the match.
    // A host runs the Relay (and the TCP server) and plays through an in-memory client like any
    // player; a guest connects by address. Lives in GameSession, so it survives the scene reload
    // that starts the match. Polled every frame by SimRunner.
    public sealed class NetSession
    {
        private const float ConnectTimeoutSeconds = 10f;

        private readonly TcpServer? _server;
        private readonly Relay? _relay;
        private readonly string _name;
        private readonly ulong _content;
        private TcpConnector? _connector;
        private float _connectingFor;

        private NetSession(TcpServer? server, Relay? relay, LockstepClient? client, TcpConnector? connector, string name, ulong content)
        {
            _server = server;
            _relay = relay;
            Client = client;
            _connector = connector;
            _name = name;
            _content = content;
        }

        // Null while a guest is still connecting.
        public LockstepClient? Client { get; private set; }

        public bool IsHost => _relay != null;
        public int Port { get; private set; }
        public bool Connecting => _connector != null;

        // Why the connection failed; the session is dead then.
        public string? Error { get; private set; }

        // Throws SocketException when the port is taken.
        public static NetSession Host(GameData data, MissionCatalog catalog, ulong content, string missionId, int port, string name)
        {
            var server = new TcpServer(port);
            var relay = new Relay(data, catalog, content, missionId);
            (LoopbackConnection hostSide, LoopbackConnection own) = LoopbackConnection.CreatePair();
            relay.Add(hostSide);
            var client = new LockstepClient(own, name, content, SimRunner.Codec);
            return new NetSession(server, relay, client, null, name, content) { Port = server.Port };
        }

        public static NetSession Join(string address, int port, string name, ulong content) =>
            new NetSession(null, null, null, new TcpConnector(address, port), name, content) { Port = port };

        public void Poll(float deltaSeconds)
        {
            if (_connector != null)
            {
                _connectingFor += deltaSeconds;
                if (_connector.IsDone)
                {
                    IConnection? connection = _connector.Connection;
                    if (connection != null)
                        Client = new LockstepClient(connection, _name, _content, SimRunner.Codec);
                    else
                        Error = $"Не удалось подключиться: {_connector.Error}";
                    _connector = null;
                }
                else if (_connectingFor > ConnectTimeoutSeconds)
                {
                    _connector.Cancel();
                    _connector = null;
                    Error = "Не удалось подключиться: хост не отвечает";
                }
            }
            if (_server != null && _relay != null)
            {
                while (_server.TryAccept(out IConnection? accepted))
                    _relay.Add(accepted!);
                _relay.Poll();
            }
            Client?.Poll();
            // The host's own messages to the relay go out in this frame, not the next one.
            _relay?.Poll();
        }

        public void Close()
        {
            _connector?.Cancel();
            Client?.Leave();
            _relay?.Poll();
            _server?.Dispose();
        }

        // The host's addresses in the local network, to tell the others.
        public static List<string> LocalAddresses()
        {
            var result = new List<string>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;
                    foreach (UnicastIPAddressInformation a in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork)
                            result.Add(a.Address.ToString());
                    }
                }
            }
            catch (Exception e) when (e is NetworkInformationException || e is PlatformNotSupportedException)
            {
                // No list: the player looks the address up in the system.
            }
            return result;
        }
    }
}
