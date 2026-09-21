using System.Runtime.InteropServices;
using RemoteClanker.Shared;
using static RemoteClanker.Host.NativeMethods;

namespace RemoteClanker.Host;

internal sealed class InputInjector
{
    private static readonly int INPUT_SIZE = Marshal.SizeOf<INPUT>();

    private readonly HashSet<MouseButton> pressedButtons = [];
    private readonly Dictionary<(ushort VirtualKey, bool Extended), ushort> pressedKeys = [];

    private readonly Rectangle screenBounds = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("No se encontró un monitor principal");

    // The client sends 0..65535 relative to the captured (primary) screen; SendInput needs it relative to
    // the whole virtual desktop, since plain ABSOLUTE is unreliable with several monitors.
    public void MoveMouse(ushort x, ushort y)
    {
        var desktop = SystemInformation.VirtualScreen;
        var pixelX = screenBounds.X + x * (screenBounds.Width - 1) / (double)ushort.MaxValue;
        var pixelY = screenBounds.Y + y * (screenBounds.Height - 1) / (double)ushort.MaxValue;
        SendMouse(
            ToAbsolute(pixelX - desktop.X, desktop.Width),
            ToAbsolute(pixelY - desktop.Y, desktop.Height),
            MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
            0);
    }

    public void SetMouseButton(MouseButton button, bool down)
    {
        var (flags, data) = button switch
        {
            MouseButton.Left => (down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP, 0),
            MouseButton.Right => (down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP, 0),
            MouseButton.Middle => (down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP, 0),
            MouseButton.X1 => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, XBUTTON1),
            MouseButton.X2 => (down ? MOUSEEVENTF_XDOWN : MOUSEEVENTF_XUP, XBUTTON2),
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, null),
        };

        SendMouse(0, 0, flags, data);
        _ = down ? pressedButtons.Add(button) : pressedButtons.Remove(button);
    }

    public void Wheel(short delta, bool horizontal) =>
        SendMouse(0, 0, horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL, delta);

    public void SetKey(ushort virtualKey, ushort scanCode, KeyFlags keyFlags)
    {
        var extended = keyFlags.HasFlag(KeyFlags.Extended);
        var up = keyFlags.HasFlag(KeyFlags.Up);
        var scan = scanCode != 0 ? scanCode : (ushort)MapVirtualKey(virtualKey, MAPVK_VK_TO_VSC);
        var flags = (extended ? KEYEVENTF_EXTENDEDKEY : 0) | (up ? KEYEVENTF_KEYUP : 0);

        Send(new INPUT
        {
            Type = INPUT_KEYBOARD,
            Data = new InputUnion { Keyboard = new KEYBDINPUT { VirtualKey = virtualKey, ScanCode = scan, Flags = flags } },
        });

        if (up)
        {
            pressedKeys.Remove((virtualKey, extended));
        }
        else
        {
            pressedKeys[(virtualKey, extended)] = scan;
        }
    }

    // A dropped connection must not leave keys or buttons stuck down on the host.
    public void ReleaseAll()
    {
        foreach (var ((virtualKey, extended), scan) in pressedKeys.ToArray())
        {
            SetKey(virtualKey, scan, KeyFlags.Up | (extended ? KeyFlags.Extended : KeyFlags.None));
        }

        foreach (var button in pressedButtons.ToArray())
        {
            SetMouseButton(button, false);
        }
    }

    private static int ToAbsolute(double pixel, int length) => (int)Math.Round(pixel * ushort.MaxValue / (length - 1));

    private static void SendMouse(int x, int y, uint flags, int data) => Send(new INPUT
    {
        Type = INPUT_MOUSE,
        Data = new InputUnion { Mouse = new MOUSEINPUT { X = x, Y = y, MouseData = data, Flags = flags } },
    });

    private static void Send(INPUT input) => SendInput(1, [input], INPUT_SIZE);
}
