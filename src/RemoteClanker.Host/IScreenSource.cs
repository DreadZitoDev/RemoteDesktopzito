using SharpGen.Runtime;

namespace RemoteClanker.Host;

internal enum CaptureResult
{
    NoChange,
    Updated,
    Reset,
}

internal interface IScreenSource : IDisposable
{
    string Name { get; }

    FrameBuffer Frame { get; }

    // Reset means Frame may have been reallocated or its content is otherwise unknown to the peer:
    // the caller must resend the screen size and a full image.
    CaptureResult Capture(int timeoutMs, List<Rectangle> dirtyRects);

    static IScreenSource Create(bool forceGdi)
    {
        if (forceGdi)
        {
            return new GdiScreenSource();
        }

        try
        {
            return DxgiScreenSource.Create();
        }
        catch (Exception ex) when (ex is SharpGenException or NotSupportedException or InvalidOperationException)
        {
            Console.WriteLine($"Desktop Duplication no disponible ({ex.Message}), se usa GDI");
            return new GdiScreenSource();
        }
    }
}
