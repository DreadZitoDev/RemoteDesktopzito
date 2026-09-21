using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using RemoteClanker.Shared;

namespace RemoteClanker.Client;

internal sealed class RemoteView : Control
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_SYSCHAR = 0x0106;
    private const int WM_MOUSEHWHEEL = 0x020E;

    private readonly Dictionary<(ushort VirtualKey, bool Extended), ushort> pressedKeys = [];
    private readonly HashSet<MouseButton> pressedButtons = [];
    private readonly ImageAttributes paintAttributes = new();
    private Bitmap? canvas;
    private (ushort X, ushort Y)? lastMousePosition;

    public event Action<MessageType, byte[]>? InputGenerated;

    public RemoteView()
    {
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.Selectable,
            true);
        TabStop = true;
        BackColor = Color.Black;
        ForeColor = Color.Gray;

        // Clamps bilinear sampling at the edges of a partial repaint so no seams appear between regions.
        paintAttributes.SetWrapMode(WrapMode.TileFlipXY);
    }

    public void ResetScreen(Size? size)
    {
        canvas?.Dispose();
        canvas = size is { } screen ? new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppPArgb) : null;
        lastMousePosition = null;
        Invalidate();
    }

    public void ApplyTiles(IEnumerable<DecodedTile> tiles)
    {
        if (canvas == null)
        {
            return;
        }

        using var graphics = Graphics.FromImage(canvas);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        foreach (var (bounds, image) in tiles)
        {
            graphics.DrawImage(image, bounds, 0, 0, bounds.Width, bounds.Height, GraphicsUnit.Pixel);
            Invalidate(ToViewRectangle(bounds));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (canvas == null)
        {
            TextRenderer.DrawText(e.Graphics, "Sin conexión", Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var bounds = GetImageBounds();
        var target = Rectangle.Intersect(e.ClipRectangle, bounds);
        if (target.IsEmpty)
        {
            return;
        }

        var scaleX = (float)canvas.Width / bounds.Width;
        var scaleY = (float)canvas.Height / bounds.Height;
        e.Graphics.InterpolationMode = InterpolationMode.Bilinear;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.DrawImage(canvas, target,
            (target.X - bounds.X) * scaleX, (target.Y - bounds.Y) * scaleY, target.Width * scaleX, target.Height * scaleY,
            GraphicsUnit.Pixel, paintAttributes);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            canvas?.Dispose();
            paintAttributes.Dispose();
        }

        base.Dispose(disposing);
    }

    // Returning false for key messages skips WinForms' dialog-key handling (Tab, arrows, Alt menu)
    // so every key reaches WndProc and gets forwarded.
    public override bool PreProcessMessage(ref Message msg) =>
        msg.Msg is not (WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP) && base.PreProcessMessage(ref msg);

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_KEYDOWN or WM_SYSKEYDOWN:
                HandleKey(m, up: false);
                return;
            case WM_KEYUP or WM_SYSKEYUP:
                HandleKey(m, up: true);
                return;
            case WM_SYSCHAR:
                return;
            case WM_MOUSEHWHEEL:
                Raise(MessageType.MouseWheel, InputCodec.EncodeMouseWheel(HighWord(m.WParam), true));
                m.Result = 0;
                return;
        }

        base.WndProc(ref m);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SendMouseMove(e.Location);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (ToRemoteButton(e.Button) is not { } button || !GetImageBounds().Contains(e.Location))
        {
            return;
        }

        SendMouseMove(e.Location);
        pressedButtons.Add(button);
        Raise(MessageType.MouseButton, InputCodec.EncodeMouseButton(button, true));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (ToRemoteButton(e.Button) is { } button && pressedButtons.Remove(button))
        {
            SendMouseMove(e.Location);
            Raise(MessageType.MouseButton, InputCodec.EncodeMouseButton(button, false));
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Raise(MessageType.MouseWheel, InputCodec.EncodeMouseWheel((short)e.Delta, false));
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        foreach (var ((virtualKey, extended), scanCode) in pressedKeys)
        {
            SendKey(virtualKey, scanCode, extended, up: true);
        }
        pressedKeys.Clear();
    }

    private void HandleKey(Message m, bool up)
    {
        var virtualKey = (ushort)m.WParam.ToInt64();
        var lParam = m.LParam.ToInt64();
        var scanCode = (ushort)((lParam >> 16) & 0xFF);
        var extended = (lParam & (1 << 24)) != 0;

        if (up)
        {
            pressedKeys.Remove((virtualKey, extended));
        }
        else
        {
            pressedKeys[(virtualKey, extended)] = scanCode;
        }

        SendKey(virtualKey, scanCode, extended, up);
    }

    private void SendKey(ushort virtualKey, ushort scanCode, bool extended, bool up)
    {
        var flags = (extended ? KeyFlags.Extended : KeyFlags.None) | (up ? KeyFlags.Up : KeyFlags.None);
        Raise(MessageType.Key, InputCodec.EncodeKey(virtualKey, scanCode, flags));
    }

    private void SendMouseMove(Point point)
    {
        if (!TryMapToRemote(point, out var position) || lastMousePosition == position)
        {
            return;
        }

        lastMousePosition = position;
        Raise(MessageType.MouseMove, InputCodec.EncodeMouseMove(position.X, position.Y));
    }

    private bool TryMapToRemote(Point point, out (ushort X, ushort Y) position)
    {
        var bounds = GetImageBounds();
        var dragging = pressedButtons.Count > 0;
        position = default;
        if (bounds.Width < 2 || bounds.Height < 2 || (!dragging && !bounds.Contains(point)))
        {
            return false;
        }

        position = (Normalize(point.X - bounds.X, bounds.Width), Normalize(point.Y - bounds.Y, bounds.Height));
        return true;
    }

    private Rectangle GetImageBounds()
    {
        if (canvas == null)
        {
            return Rectangle.Empty;
        }

        var scale = Math.Min((double)ClientSize.Width / canvas.Width, (double)ClientSize.Height / canvas.Height);
        var width = (int)(canvas.Width * scale);
        var height = (int)(canvas.Height * scale);
        return new Rectangle((ClientSize.Width - width) / 2, (ClientSize.Height - height) / 2, width, height);
    }

    private Rectangle ToViewRectangle(Rectangle remote)
    {
        var bounds = GetImageBounds();
        var scaleX = (double)bounds.Width / canvas!.Width;
        var scaleY = (double)bounds.Height / canvas.Height;
        var left = bounds.X + (int)Math.Floor(remote.Left * scaleX) - 1;
        var top = bounds.Y + (int)Math.Floor(remote.Top * scaleY) - 1;
        var right = bounds.X + (int)Math.Ceiling(remote.Right * scaleX) + 1;
        var bottom = bounds.Y + (int)Math.Ceiling(remote.Bottom * scaleY) + 1;
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    // SendInput's MOUSEEVENTF_ABSOLUTE expects coordinates normalized to 0..65535 across the screen.
    private static ushort Normalize(int offset, int length) =>
        (ushort)Math.Round(Math.Clamp(offset / (double)(length - 1), 0, 1) * ushort.MaxValue);

    private static short HighWord(nint value) => unchecked((short)((value.ToInt64() >> 16) & 0xFFFF));

    private static MouseButton? ToRemoteButton(MouseButtons button) => button switch
    {
        MouseButtons.Left => MouseButton.Left,
        MouseButtons.Right => MouseButton.Right,
        MouseButtons.Middle => MouseButton.Middle,
        MouseButtons.XButton1 => MouseButton.X1,
        MouseButtons.XButton2 => MouseButton.X2,
        _ => null,
    };

    private void Raise(MessageType type, byte[] payload)
    {
        if (canvas != null)
        {
            InputGenerated?.Invoke(type, payload);
        }
    }
}
