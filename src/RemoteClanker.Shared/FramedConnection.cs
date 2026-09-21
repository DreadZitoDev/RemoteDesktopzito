using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace RemoteClanker.Shared;

// Wire format: [type:1][length:4 LE][payload:length]
public sealed class FramedConnection : IDisposable
{
    private const int HEADER_SIZE = 5;

    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public FramedConnection(TcpClient client)
    {
        this.client = client;
        client.NoDelay = true;
        stream = client.GetStream();
    }

    public EndPoint? RemoteEndPoint => client.Client.RemoteEndPoint;

    public async Task SendAsync(MessageType type, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        var packet = new byte[HEADER_SIZE + payload.Length];
        packet[0] = (byte)type;
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(1), payload.Length);
        payload.Span.CopyTo(packet.AsSpan(HEADER_SIZE));

        await writeLock.WaitAsync(ct);
        try
        {
            await stream.WriteAsync(packet, ct);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<(MessageType Type, byte[] Payload)> ReceiveAsync(int maxPayload, CancellationToken ct = default)
    {
        var header = new byte[HEADER_SIZE];
        await stream.ReadExactlyAsync(header, ct);

        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1));
        if (length < 0 || length > maxPayload)
        {
            throw new InvalidDataException($"Tamaño de mensaje inválido: {length}");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, ct);
        return ((MessageType)header[0], payload);
    }

    public async Task<byte[]> ExpectAsync(MessageType expected, int maxPayload, CancellationToken ct = default)
    {
        var (type, payload) = await ReceiveAsync(maxPayload, ct);
        return type == expected ? payload : throw new InvalidDataException($"Se esperaba {expected} y llegó {type}");
    }

    public void Dispose()
    {
        stream.Dispose();
        client.Dispose();
        writeLock.Dispose();
    }
}
