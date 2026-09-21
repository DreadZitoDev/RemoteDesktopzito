using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Threading.Channels;
using RemoteClanker.Shared;

namespace RemoteClanker.Client;

internal sealed class RemoteClient : IDisposable
{
    private const int CONNECT_TIMEOUT_SECONDS = 10;

    private readonly FramedConnection connection;
    private readonly CancellationTokenSource cts = new();
    private readonly Channel<(MessageType Type, byte[] Payload)> outgoing =
        Channel.CreateUnbounded<(MessageType, byte[])>(new UnboundedChannelOptions { SingleReader = true });

    public event Action<Size>? ScreenReset;
    public event Action<IReadOnlyList<DecodedTile>>? TilesReceived;
    public event Action<Exception?>? Disconnected;

    private RemoteClient(FramedConnection connection) => this.connection = connection;

    public static async Task<RemoteClient> ConnectAsync(string host, int port, string password)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(CONNECT_TIMEOUT_SECONDS));
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host, port, timeout.Token);
            var connection = new FramedConnection(tcp);
            var nonce = await connection.ExpectAsync(MessageType.Challenge, Protocol.MAX_AUTH_PAYLOAD, timeout.Token);
            await connection.SendAsync(MessageType.AuthResponse, Auth.ComputeProof(password, nonce), timeout.Token);
            var result = await connection.ExpectAsync(MessageType.AuthResult, Protocol.MAX_AUTH_PAYLOAD, timeout.Token);
            return result is [1] ? new RemoteClient(connection) : throw new AuthenticationException("Contraseña incorrecta");
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    public void Start() => Task.Run(RunAsync);

    // Inputs go through a single-reader channel so key down/up order is preserved on the wire.
    public void Post(MessageType type, byte[] payload) => outgoing.Writer.TryWrite((type, payload));

    public void Dispose()
    {
        cts.Cancel();
        outgoing.Writer.TryComplete();
        connection.Dispose();
    }

    private async Task RunAsync()
    {
        var sending = SendLoopAsync(cts.Token);
        var receiving = ReceiveLoopAsync(cts.Token);

        var finished = await Task.WhenAny(sending, receiving);
        await cts.CancelAsync();
        await Task.WhenAll(sending, receiving).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Disconnected?.Invoke(finished.Exception?.GetBaseException());
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        await foreach (var (type, payload) in outgoing.Reader.ReadAllAsync(ct))
        {
            await connection.SendAsync(type, payload, ct);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (true)
        {
            var (type, payload) = await connection.ReceiveAsync(Protocol.MAX_FRAME_PAYLOAD, ct);
            switch (type)
            {
                case MessageType.ScreenInfo:
                    var (width, height) = FrameCodec.DecodeScreenInfo(payload);
                    ScreenReset?.Invoke(new Size(width, height));
                    break;
                case MessageType.FrameUpdate:
                    TilesReceived?.Invoke(FrameCodec.DecodeFrameUpdate(payload).AsParallel().Select(DecodeTile).ToArray());
                    break;
                default:
                    throw new InvalidDataException($"Mensaje inesperado: {type}");
            }
        }
    }

    private static DecodedTile DecodeTile(EncodedTile tile)
    {
        var jpeg = MemoryMarshal.TryGetArray(tile.Jpeg, out var segment)
            ? segment
            : new ArraySegment<byte>(tile.Jpeg.ToArray());
        using var stream = new MemoryStream(jpeg.Array!, jpeg.Offset, jpeg.Count, writable: false);
        using var image = Image.FromStream(stream);
        var (x, y, width, height) = tile.Bounds;
        return new DecodedTile(new Rectangle(x, y, width, height), new Bitmap(image));
    }
}

internal sealed record DecodedTile(Rectangle Bounds, Bitmap Image);
