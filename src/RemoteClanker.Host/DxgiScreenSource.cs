using System.Runtime.CompilerServices;
using SharpGen.Runtime;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace RemoteClanker.Host;

internal sealed class DxgiScreenSource : IScreenSource
{
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly IDXGIOutput1 output;
    private IDXGIOutputDuplication? duplication;
    private ID3D11Texture2D? staging;
    private RawRect[] dirtyBuffer = new RawRect[64];
    private OutduplMoveRect[] moveBuffer = new OutduplMoveRect[16];
    private bool needsFullCopy = true;

    private DxgiScreenSource(ID3D11Device device, ID3D11DeviceContext context, IDXGIOutput1 output)
    {
        this.device = device;
        this.context = context;
        this.output = output;
        Frame = CreateFrameBuffer();
        duplication = output.DuplicateOutput(device);
    }

    public string Name => "Desktop Duplication";

    public FrameBuffer Frame { get; private set; }

    public static DxgiScreenSource Create()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out var adapter).Success; adapterIndex++)
        {
            using (adapter)
            {
                for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out var candidate).Success; outputIndex++)
                {
                    using (candidate)
                    {
                        if (IsPrimary(candidate.Description))
                        {
                            return Create(adapter, candidate);
                        }
                    }
                }
            }
        }

        throw new NotSupportedException("No se encontró la salida DXGI del monitor principal");
    }

    public CaptureResult Capture(int timeoutMs, List<Rectangle> dirtyRects)
    {
        dirtyRects.Clear();
        if (duplication == null && !TryRecreateDuplication())
        {
            Thread.Sleep(timeoutMs);
            return CaptureResult.NoChange;
        }

        var result = duplication!.AcquireNextFrame((uint)timeoutMs, out var info, out var resource);
        if (result == Vortice.DXGI.ResultCode.WaitTimeout)
        {
            return CaptureResult.NoChange;
        }

        if (result == Vortice.DXGI.ResultCode.AccessLost)
        {
            ReleaseDuplication();
            return CaptureResult.NoChange;
        }

        result.CheckError();
        try
        {
            // LastPresentTime == 0 means only the pointer changed; the desktop image is the same.
            if (info.LastPresentTime == 0 && !needsFullCopy)
            {
                return CaptureResult.NoChange;
            }

            var reset = needsFullCopy;
            if (needsFullCopy)
            {
                dirtyRects.Add(Frame.Bounds);
                needsFullCopy = false;
            }
            else
            {
                CollectChangedRects(info, dirtyRects);
            }

            using var texture = resource.QueryInterface<ID3D11Texture2D>();
            CopyToFrame(texture, dirtyRects);
            return reset ? CaptureResult.Reset : CaptureResult.Updated;
        }
        finally
        {
            resource.Dispose();
            duplication.ReleaseFrame();
        }
    }

    public void Dispose()
    {
        ReleaseDuplication();
        staging?.Dispose();
        output.Dispose();
        context.Dispose();
        device.Dispose();
        Frame.Dispose();
    }

    private static DxgiScreenSource Create(IDXGIAdapter1 adapter, IDXGIOutput candidate)
    {
        if (candidate.Description.Rotation is not (ModeRotation.Identity or ModeRotation.Unspecified))
        {
            throw new NotSupportedException("Monitor rotado");
        }

        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0],
            out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        return new DxgiScreenSource(device, context, candidate.QueryInterface<IDXGIOutput1>());
    }

    private static bool IsPrimary(OutputDescription description) =>
        description.AttachedToDesktop && description.DesktopCoordinates.Left == 0 && description.DesktopCoordinates.Top == 0;

    private FrameBuffer CreateFrameBuffer()
    {
        var bounds = output.Description.DesktopCoordinates;
        return new FrameBuffer(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }

    // Duplication is lost on resolution changes, fullscreen apps and while the secure desktop is shown.
    private bool TryRecreateDuplication()
    {
        try
        {
            duplication = output.DuplicateOutput(device);
        }
        catch (SharpGenException)
        {
            return false;
        }

        var size = output.Description.DesktopCoordinates;
        if (size.Right - size.Left != Frame.Width || size.Bottom - size.Top != Frame.Height)
        {
            Frame.Dispose();
            Frame = CreateFrameBuffer();
            staging?.Dispose();
            staging = null;
        }

        needsFullCopy = true;
        return true;
    }

    private void ReleaseDuplication()
    {
        duplication?.Dispose();
        duplication = null;
    }

    private void CollectChangedRects(OutduplFrameInfo info, List<Rectangle> rects)
    {
        if (info.TotalMetadataBufferSize == 0)
        {
            return;
        }

        foreach (var move in ReadMetadata(ref moveBuffer, duplication!.GetFrameMoveRects))
        {
            rects.Add(Clip(move.DestinationRect));
        }

        foreach (var dirty in ReadMetadata(ref dirtyBuffer, duplication!.GetFrameDirtyRects))
        {
            rects.Add(Clip(dirty));
        }
    }

    private delegate Result MetadataReader<T>(uint bufferSize, T[] buffer, out uint requiredSize);

    private static ReadOnlySpan<T> ReadMetadata<T>(ref T[] buffer, MetadataReader<T> read)
    {
        var elementSize = (uint)Unsafe.SizeOf<T>();
        var result = read((uint)buffer.Length * elementSize, buffer, out var required);
        if (result == Vortice.DXGI.ResultCode.MoreData)
        {
            buffer = new T[required / elementSize];
            result = read((uint)buffer.Length * elementSize, buffer, out required);
        }

        result.CheckError();
        return buffer.AsSpan(0, (int)(required / elementSize));
    }

    private Rectangle Clip(RawRect rect) =>
        Rectangle.Intersect(Frame.Bounds, Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));

    private unsafe void CopyToFrame(ID3D11Texture2D texture, List<Rectangle> rects)
    {
        staging ??= device.CreateTexture2D(texture.Description with
        {
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
            MipLevels = 1,
            ArraySize = 1,
            SampleDescription = new SampleDescription(1, 0),
        });

        context.CopyResource(staging, texture);
        var mapped = context.Map(staging, 0, MapMode.Read);
        try
        {
            foreach (var rect in rects)
            {
                for (var y = rect.Top; y < rect.Bottom; y++)
                {
                    var source = new ReadOnlySpan<byte>(
                        (byte*)mapped.DataPointer + y * mapped.RowPitch + rect.X * FrameBuffer.BYTES_PER_PIXEL,
                        rect.Width * FrameBuffer.BYTES_PER_PIXEL);
                    source.CopyTo(Frame.GetRow(rect.X, y, rect.Width));
                }
            }
        }
        finally
        {
            context.Unmap(staging, 0);
        }
    }
}
