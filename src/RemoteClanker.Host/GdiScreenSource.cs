using System.ComponentModel;
using System.Drawing.Imaging;

namespace RemoteClanker.Host;

internal sealed class GdiScreenSource : IScreenSource
{
    private readonly Rectangle screenBounds;
    private readonly Bitmap bitmap;
    private readonly Graphics graphics;
    private bool captured;

    public GdiScreenSource()
    {
        screenBounds = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("No se encontró un monitor principal");
        Frame = new FrameBuffer(screenBounds.Width, screenBounds.Height);
        bitmap = new Bitmap(Frame.Width, Frame.Height, Frame.Stride, PixelFormat.Format32bppRgb, Frame.Pointer);
        graphics = Graphics.FromImage(bitmap);
    }

    public string Name => "GDI";

    public FrameBuffer Frame { get; }

    // GDI has no change notification: the whole screen is reported dirty and the encoder's tile diff filters it.
    public CaptureResult Capture(int timeoutMs, List<Rectangle> dirtyRects)
    {
        dirtyRects.Clear();
        try
        {
            graphics.CopyFromScreen(screenBounds.Location, Point.Empty, screenBounds.Size);
        }
        catch (Win32Exception)
        {
            // Fails while the secure desktop (UAC prompt, lock screen) is active.
            Thread.Sleep(timeoutMs);
            return CaptureResult.NoChange;
        }

        dirtyRects.Add(Frame.Bounds);
        var result = captured ? CaptureResult.Updated : CaptureResult.Reset;
        captured = true;
        return result;
    }

    public void Dispose()
    {
        graphics.Dispose();
        bitmap.Dispose();
        Frame.Dispose();
    }
}
