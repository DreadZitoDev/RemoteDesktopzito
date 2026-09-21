using System.Runtime.InteropServices;

namespace RemoteClanker.Host;

// BGRA pixels in unmanaged memory so the pointer stays fixed for GDI+ bitmaps wrapping sub-regions.
internal sealed unsafe class FrameBuffer : IDisposable
{
    public const int BYTES_PER_PIXEL = 4;

    public FrameBuffer(int width, int height)
    {
        Width = width;
        Height = height;
        Stride = width * BYTES_PER_PIXEL;
        Pointer = (nint)NativeMemory.AllocZeroed((nuint)(Stride * height));
    }

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public nint Pointer { get; private set; }
    public Rectangle Bounds => new(0, 0, Width, Height);

    public nint PixelAddress(int x, int y) => Pointer + y * Stride + x * BYTES_PER_PIXEL;

    public Span<byte> GetRow(int x, int y, int width) => new((void*)PixelAddress(x, y), width * BYTES_PER_PIXEL);

    public void Dispose()
    {
        NativeMemory.Free((void*)Pointer);
        Pointer = 0;
    }
}
