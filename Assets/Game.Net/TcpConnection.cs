using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net.Sockets;

namespace Game.Net;

// A message pipe over a TCP socket: every message goes as [int32 length, little-endian][bytes].
// Reads only what the socket already has, so polling it never blocks the frame.
public sealed class TcpConnection : IConnection
{
    private readonly Socket _socket;
    private readonly byte[] _chunk = new byte[64 * 1024];
    private readonly Queue<byte[]> _inbox = new Queue<byte[]>();
    private byte[] _buffer = new byte[64 * 1024];
    private int _count;
    private bool _closed;

    public TcpConnection(Socket socket)
    {
        _socket = socket;
        _socket.NoDelay = true;
        _socket.Blocking = true;
    }

    public bool IsOpen => !_closed;

    public void Send(byte[] message)
    {
        if (_closed)
            return;
        var frame = new byte[4 + message.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, message.Length);
        Buffer.BlockCopy(message, 0, frame, 4, message.Length);
        try
        {
            int sent = 0;
            while (sent < frame.Length)
                sent += _socket.Send(frame, sent, frame.Length - sent, SocketFlags.None);
        }
        catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
        {
            Close();
        }
    }

    public bool TryReceive(out byte[] message)
    {
        if (_inbox.Count == 0 && !_closed)
            Pump();
        if (_inbox.Count > 0)
        {
            message = _inbox.Dequeue();
            return true;
        }
        message = Array.Empty<byte>();
        return false;
    }

    public void Close()
    {
        if (_closed)
            return;
        _closed = true;
        try
        {
            _socket.Shutdown(SocketShutdown.Both);
        }
        catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
        {
            // Already gone: nothing to tell the other side.
        }
        _socket.Close();
    }

    private void Pump()
    {
        try
        {
            while (_socket.Available > 0)
            {
                int read = _socket.Receive(_chunk, 0, Math.Min(_chunk.Length, _socket.Available), SocketFlags.None);
                Append(read);
            }
            // Readable with nothing to read: the other side closed.
            if (_socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0)
                Close();
        }
        catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
        {
            Close();
        }
        Split();
    }

    private void Append(int read)
    {
        if (_count + read > _buffer.Length)
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _count + read));
        Buffer.BlockCopy(_chunk, 0, _buffer, _count, read);
        _count += read;
    }

    private void Split()
    {
        int offset = 0;
        while (_count - offset >= 4)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(_buffer, offset, 4));
            if (length < 0 || length > LockstepSettings.MaxMessageBytes)
            {
                Close();
                _count = 0;
                return;
            }
            if (_count - offset - 4 < length)
                break;
            var message = new byte[length];
            Buffer.BlockCopy(_buffer, offset + 4, message, 0, length);
            _inbox.Enqueue(message);
            offset += 4 + length;
        }
        if (offset > 0)
        {
            Buffer.BlockCopy(_buffer, offset, _buffer, 0, _count - offset);
            _count -= offset;
        }
    }
}
