using System.Buffers.Binary;

namespace RemoteClanker.Shared;

public readonly record struct TileRect(int X, int Y, int Width, int Height);

public readonly record struct EncodedTile(TileRect Bounds, ReadOnlyMemory<byte> Jpeg);

// FrameUpdate layout: [count:2] then per tile [x:2][y:2][width:2][height:2][length:4][jpeg:length]
public static class FrameCodec
{
    private const int TILE_HEADER_SIZE = 12;

    public static byte[] EncodeScreenInfo(int width, int height)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), (ushort)height);
        return buffer;
    }

    public static (int Width, int Height) DecodeScreenInfo(ReadOnlySpan<byte> data) =>
        (BinaryPrimitives.ReadUInt16LittleEndian(data), BinaryPrimitives.ReadUInt16LittleEndian(data[2..]));

    public static byte[] EncodeFrameUpdate(IReadOnlyList<EncodedTile> tiles)
    {
        var buffer = new byte[2 + tiles.Sum(tile => TILE_HEADER_SIZE + tile.Jpeg.Length)];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)tiles.Count);
        var offset = 2;

        foreach (var (bounds, jpeg) in tiles)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], (ushort)bounds.X);
            BinaryPrimitives.WriteUInt16LittleEndian(span[(offset + 2)..], (ushort)bounds.Y);
            BinaryPrimitives.WriteUInt16LittleEndian(span[(offset + 4)..], (ushort)bounds.Width);
            BinaryPrimitives.WriteUInt16LittleEndian(span[(offset + 6)..], (ushort)bounds.Height);
            BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 8)..], jpeg.Length);
            jpeg.Span.CopyTo(span[(offset + TILE_HEADER_SIZE)..]);
            offset += TILE_HEADER_SIZE + jpeg.Length;
        }

        return buffer;
    }

    public static List<EncodedTile> DecodeFrameUpdate(byte[] payload)
    {
        var span = payload.AsSpan();
        var count = BinaryPrimitives.ReadUInt16LittleEndian(span);
        var tiles = new List<EncodedTile>(count);
        var offset = 2;

        for (var i = 0; i < count; i++)
        {
            var bounds = new TileRect(
                BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]),
                BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 2)..]),
                BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 4)..]),
                BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 6)..]));
            var length = BinaryPrimitives.ReadInt32LittleEndian(span[(offset + 8)..]);
            tiles.Add(new EncodedTile(bounds, payload.AsMemory(offset + TILE_HEADER_SIZE, length)));
            offset += TILE_HEADER_SIZE + length;
        }

        return tiles;
    }
}
