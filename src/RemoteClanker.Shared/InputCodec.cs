using System.Buffers.Binary;

namespace RemoteClanker.Shared;

public static class InputCodec
{
    public static byte[] EncodeMouseMove(ushort x, ushort y)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, x);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), y);
        return buffer;
    }

    public static (ushort X, ushort Y) DecodeMouseMove(ReadOnlySpan<byte> data) =>
        (BinaryPrimitives.ReadUInt16LittleEndian(data), BinaryPrimitives.ReadUInt16LittleEndian(data[2..]));

    public static byte[] EncodeMouseButton(MouseButton button, bool down) => [(byte)button, down ? (byte)1 : (byte)0];

    public static (MouseButton Button, bool Down) DecodeMouseButton(ReadOnlySpan<byte> data) =>
        ((MouseButton)data[0], data[1] != 0);

    public static byte[] EncodeMouseWheel(short delta, bool horizontal)
    {
        var buffer = new byte[3];
        BinaryPrimitives.WriteInt16LittleEndian(buffer, delta);
        buffer[2] = horizontal ? (byte)1 : (byte)0;
        return buffer;
    }

    public static (short Delta, bool Horizontal) DecodeMouseWheel(ReadOnlySpan<byte> data) =>
        (BinaryPrimitives.ReadInt16LittleEndian(data), data[2] != 0);

    public static byte[] EncodeKey(ushort virtualKey, ushort scanCode, KeyFlags flags)
    {
        var buffer = new byte[5];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, virtualKey);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), scanCode);
        buffer[4] = (byte)flags;
        return buffer;
    }

    public static (ushort VirtualKey, ushort ScanCode, KeyFlags Flags) DecodeKey(ReadOnlySpan<byte> data) =>
        (BinaryPrimitives.ReadUInt16LittleEndian(data), BinaryPrimitives.ReadUInt16LittleEndian(data[2..]), (KeyFlags)data[4]);
}
