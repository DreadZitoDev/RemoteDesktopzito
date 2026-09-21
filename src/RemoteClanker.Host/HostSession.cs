using System.Diagnostics;
using System.Net.Sockets;
using RemoteClanker.Shared;

namespace RemoteClanker.Host;

internal sealed class HostSession(TcpClient tcp, string password, bool forceGdi)
{
    private const int MAX_FPS = 30;
    private const int CAPTURE_TIMEOUT_MS = 100;
    private const int AUTH_TIMEOUT_SECONDS = 10;
    private const int FAILED_AUTH_DELAY_MS = 1000;

    public async Task RunAsync()
    {
        using var connection = new FramedConnection(tcp);
        var endPoint = connection.RemoteEndPoint;

        if (!await AuthenticateAsync(connection))
        {
            Console.WriteLine($"[{endPoint}] Contraseña incorrecta, conexión rechazada");
            await Task.Delay(FAILED_AUTH_DELAY_MS);
            return;
        }

        Console.WriteLine($"[{endPoint}] Cliente autenticado");

        var injector = new InputInjector();
        using var cts = new CancellationTokenSource();
        var streaming = StreamScreenAsync(connection, cts.Token);
        var receiving = ReceiveInputAsync(connection, injector, cts.Token);

        var finished = await Task.WhenAny(streaming, receiving);
        await cts.CancelAsync();
        await Task.WhenAll(streaming, receiving).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        injector.ReleaseAll();

        Console.WriteLine($"[{endPoint}] Desconectado: {finished.Exception?.GetBaseException().Message ?? "fin de la sesión"}");
    }

    private async Task<bool> AuthenticateAsync(FramedConnection connection)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(AUTH_TIMEOUT_SECONDS));
        var nonce = Auth.CreateNonce();

        await connection.SendAsync(MessageType.Challenge, nonce, timeout.Token);
        var proof = await connection.ExpectAsync(MessageType.AuthResponse, Protocol.MAX_AUTH_PAYLOAD, timeout.Token);
        var accepted = Auth.Verify(password, nonce, proof);
        await connection.SendAsync(MessageType.AuthResult, new[] { accepted ? (byte)1 : (byte)0 }, timeout.Token);
        return accepted;
    }

    private async Task StreamScreenAsync(FramedConnection connection, CancellationToken ct)
    {
        using var source = IScreenSource.Create(forceGdi);
        using var encoder = new FrameEncoder();
        Console.WriteLine($"Captura: {source.Name}, {source.Frame.Width}x{source.Frame.Height}");

        var dirtyRects = new List<Rectangle>();
        var frameInterval = TimeSpan.FromSeconds(1.0 / MAX_FPS);
        while (true)
        {
            var started = Stopwatch.GetTimestamp();
            var result = source.Capture(CAPTURE_TIMEOUT_MS, dirtyRects);
            ct.ThrowIfCancellationRequested();
            if (result == CaptureResult.NoChange)
            {
                continue;
            }

            if (result == CaptureResult.Reset)
            {
                await connection.SendAsync(MessageType.ScreenInfo, FrameCodec.EncodeScreenInfo(source.Frame.Width, source.Frame.Height), ct);
            }

            var tiles = encoder.Encode(source.Frame, dirtyRects, full: result == CaptureResult.Reset);
            if (tiles.Count > 0)
            {
                await connection.SendAsync(MessageType.FrameUpdate, FrameCodec.EncodeFrameUpdate(tiles), ct);
            }

            var remaining = frameInterval - Stopwatch.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, ct);
            }
        }
    }

    private static async Task ReceiveInputAsync(FramedConnection connection, InputInjector injector, CancellationToken ct)
    {
        while (true)
        {
            var (type, payload) = await connection.ReceiveAsync(Protocol.MAX_INPUT_PAYLOAD, ct);
            switch (type)
            {
                case MessageType.MouseMove:
                    var (x, y) = InputCodec.DecodeMouseMove(payload);
                    injector.MoveMouse(x, y);
                    break;
                case MessageType.MouseButton:
                    var (button, down) = InputCodec.DecodeMouseButton(payload);
                    injector.SetMouseButton(button, down);
                    break;
                case MessageType.MouseWheel:
                    var (delta, horizontal) = InputCodec.DecodeMouseWheel(payload);
                    injector.Wheel(delta, horizontal);
                    break;
                case MessageType.Key:
                    var (virtualKey, scanCode, flags) = InputCodec.DecodeKey(payload);
                    injector.SetKey(virtualKey, scanCode, flags);
                    break;
                default:
                    throw new InvalidDataException($"Mensaje inesperado: {type}");
            }
        }
    }
}
