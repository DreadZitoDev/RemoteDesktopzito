using System.Collections.Concurrent;
using RemoteClanker.Shared;

namespace RemoteClanker.Client;

internal sealed class MainForm : Form
{
    private readonly TextBox addressBox = new() { Width = 180, Text = $"127.0.0.1:{Protocol.DEFAULT_PORT}" };
    private readonly TextBox passwordBox = new() { Width = 120, UseSystemPasswordChar = true };
    private readonly Button connectButton = new() { Text = "Conectar", AutoSize = true };
    private readonly Label statusLabel = CreateLabel("Desconectado");
    private readonly RemoteView remoteView = new() { Dock = DockStyle.Fill };
    private readonly ConcurrentQueue<Action> uiUpdates = new();
    private RemoteClient? client;
    private int drainScheduled;

    public MainForm()
    {
        Text = "RemoteClanker";
        ClientSize = new Size(1280, 800);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(4) };
        toolbar.Controls.AddRange([CreateLabel("Host:"), addressBox, CreateLabel("Contraseña:"), passwordBox, connectButton, statusLabel]);

        Controls.Add(remoteView);
        Controls.Add(toolbar);

        connectButton.Click += async (_, _) => await ToggleConnectionAsync();
        passwordBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await ToggleConnectionAsync();
            }
        };
        remoteView.InputGenerated += (type, payload) => client?.Post(type, payload);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Disconnect("Desconectado");
        base.OnFormClosed(e);
    }

    private async Task ToggleConnectionAsync()
    {
        if (client != null)
        {
            Disconnect("Desconectado");
            return;
        }

        if (!TryParseAddress(addressBox.Text, out var host, out var port))
        {
            statusLabel.Text = "Dirección inválida (usa ip:puerto)";
            return;
        }

        connectButton.Enabled = false;
        statusLabel.Text = "Conectando...";
        try
        {
            var connected = await RemoteClient.ConnectAsync(host, port, passwordBox.Text);
            connected.ScreenReset += size => EnqueueUi(() =>
            {
                if (client == connected)
                {
                    remoteView.ResetScreen(size);
                }
            });
            connected.TilesReceived += tiles => EnqueueUi(() =>
            {
                if (client == connected)
                {
                    remoteView.ApplyTiles(tiles);
                }

                foreach (var tile in tiles)
                {
                    tile.Image.Dispose();
                }
            });
            connected.Disconnected += error => EnqueueUi(() => OnDisconnected(connected, error));
            client = connected;
            connected.Start();

            connectButton.Text = "Desconectar";
            statusLabel.Text = $"Conectado a {host}:{port}";
            remoteView.Focus();
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            connectButton.Enabled = true;
        }
    }

    // Frame updates are deltas, so none can be dropped: they are queued and applied in order,
    // with a single pending BeginInvoke draining everything that arrived in the meantime.
    private void EnqueueUi(Action update)
    {
        uiUpdates.Enqueue(update);
        if (Interlocked.Exchange(ref drainScheduled, 1) == 0)
        {
            RunOnUi(DrainUiUpdates);
        }
    }

    private void DrainUiUpdates()
    {
        Volatile.Write(ref drainScheduled, 0);
        while (uiUpdates.TryDequeue(out var update))
        {
            update();
        }
    }

    private void OnDisconnected(RemoteClient source, Exception? error)
    {
        if (client == source)
        {
            Disconnect(error != null ? $"Conexión perdida: {error.Message}" : "Conexión cerrada");
        }
    }

    private void Disconnect(string status)
    {
        var current = client;
        client = null;
        current?.Dispose();

        remoteView.ResetScreen(null);
        connectButton.Text = "Conectar";
        statusLabel.Text = status;
    }

    private void RunOnUi(Action action)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(action);
        }
    }

    private static bool TryParseAddress(string text, out string host, out int port)
    {
        var separator = text.LastIndexOf(':');
        host = (separator >= 0 ? text[..separator] : text).Trim();
        port = Protocol.DEFAULT_PORT;
        return host.Length > 0 && (separator < 0 || int.TryParse(text[(separator + 1)..], out port));
    }

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(6, 8, 3, 0),
    };
}
