using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using RemoteClanker.Host;
using RemoteClanker.Shared;

// Without per-monitor DPI awareness, capture and SendInput coordinates get scaled on high-DPI screens.
Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
Console.OutputEncoding = System.Text.Encoding.UTF8;

var port = GetArg("--port") is { } portArg ? int.Parse(portArg) : Protocol.DEFAULT_PORT;
var forceGdi = args.Contains("--gdi");
var password = GetArg("--password") ?? RandomNumberGenerator.GetString("abcdefghjkmnpqrstuvwxyz23456789", 8);

var listener = new TcpListener(IPAddress.Any, port);
listener.Start();

Console.WriteLine($"RemoteClanker Host escuchando en el puerto {port}");
foreach (var address in LocalAddresses())
{
    Console.WriteLine($"  {address}:{port}");
}
Console.WriteLine($"Contraseña: {password}");
Console.WriteLine("Ctrl+C para salir.");

while (true)
{
    var tcp = await listener.AcceptTcpClientAsync();
    Console.WriteLine($"Conexión entrante desde {tcp.Client.RemoteEndPoint}");
    try
    {
        await new HostSession(tcp, password, forceGdi).RunAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error en la sesión: {ex.Message}");
    }
}

string? GetArg(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static IEnumerable<IPAddress> LocalAddresses() =>
    Dns.GetHostAddresses(Dns.GetHostName())
        .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address));
