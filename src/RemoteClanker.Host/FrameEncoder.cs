using System.Drawing.Imaging;
using RemoteClanker.Shared;

namespace RemoteClanker.Host;

// Splits the screen into a tile grid, keeps a copy of what the client already has, and only encodes
// tiles whose pixels actually differ. Changed tiles in a row are merged into one JPEG strip.
internal sealed class FrameEncoder : IDisposable
{
    private const int TILE_SIZE = 128;
    private const long JPEG_QUALITY = 70;

    private readonly ImageCodecInfo jpegCodec =
        ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    private FrameBuffer? sent;

    public List<EncodedTile> Encode(FrameBuffer current, IReadOnlyList<Rectangle> dirtyRects, bool full)
    {
        if (sent == null || sent.Width != current.Width || sent.Height != current.Height)
        {
            sent?.Dispose();
            sent = new FrameBuffer(current.Width, current.Height);
            full = true;
        }

        var strips = FindChangedStrips(current, sent, full ? [current.Bounds] : dirtyRects, full);
        return strips.AsParallel().Select(strip => EncodeStrip(current, strip)).ToList();
    }

    public void Dispose() => sent?.Dispose();

    private static List<Rectangle> FindChangedStrips(FrameBuffer current, FrameBuffer sent, IReadOnlyList<Rectangle> dirtyRects, bool full)
    {
        var columns = (current.Width + TILE_SIZE - 1) / TILE_SIZE;
        var rows = (current.Height + TILE_SIZE - 1) / TILE_SIZE;
        var candidates = new bool[columns * rows];
        foreach (var rect in dirtyRects.Where(rect => !rect.IsEmpty))
        {
            for (var row = rect.Top / TILE_SIZE; row <= (rect.Bottom - 1) / TILE_SIZE; row++)
            {
                for (var column = rect.Left / TILE_SIZE; column <= (rect.Right - 1) / TILE_SIZE; column++)
                {
                    candidates[row * columns + column] = true;
                }
            }
        }

        var strips = new List<Rectangle>();
        for (var row = 0; row < rows; row++)
        {
            Rectangle? strip = null;
            for (var column = 0; column < columns; column++)
            {
                var tile = Rectangle.Intersect(current.Bounds, new Rectangle(column * TILE_SIZE, row * TILE_SIZE, TILE_SIZE, TILE_SIZE));
                var changed = candidates[row * columns + column] && (full || !TileEquals(current, sent, tile));
                if (changed)
                {
                    CopyTile(current, sent, tile);
                    strip = strip is { } open ? Rectangle.Union(open, tile) : tile;
                }
                else if (strip is { } closed)
                {
                    strips.Add(closed);
                    strip = null;
                }
            }

            if (strip is { } last)
            {
                strips.Add(last);
            }
        }

        return strips;
    }

    private static bool TileEquals(FrameBuffer a, FrameBuffer b, Rectangle tile)
    {
        for (var y = tile.Top; y < tile.Bottom; y++)
        {
            if (!a.GetRow(tile.X, y, tile.Width).SequenceEqual(b.GetRow(tile.X, y, tile.Width)))
            {
                return false;
            }
        }

        return true;
    }

    private static void CopyTile(FrameBuffer source, FrameBuffer destination, Rectangle tile)
    {
        for (var y = tile.Top; y < tile.Bottom; y++)
        {
            source.GetRow(tile.X, y, tile.Width).CopyTo(destination.GetRow(tile.X, y, tile.Width));
        }
    }

    private EncodedTile EncodeStrip(FrameBuffer frame, Rectangle strip)
    {
        using var bitmap = new Bitmap(strip.Width, strip.Height, frame.Stride, PixelFormat.Format32bppRgb, frame.PixelAddress(strip.X, strip.Y));
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JPEG_QUALITY);
        using var stream = new MemoryStream();
        bitmap.Save(stream, jpegCodec, parameters);
        return new EncodedTile(new TileRect(strip.X, strip.Y, strip.Width, strip.Height), stream.ToArray());
    }
}
