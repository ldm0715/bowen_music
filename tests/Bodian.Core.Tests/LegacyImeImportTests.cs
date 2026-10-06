using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Bodian.WinUI.Services;
using Xunit;

namespace Bodian.Core.Tests;

public sealed class LegacyImeImportTests
{
    [Fact]
    public void FindsNamedDelayImportWithoutAHardCodedAddress()
    {
        var bytes = CreateImage();
        WithImage(bytes, image => Assert.Equal(image + 0x400,
            LegacyImeCompatibility.FindDelayImport(image, "IMM32.dll", "ImmDisableLegacyIME")));
    }

    [Theory]
    [InlineData("OTHER.dll", "ImmDisableLegacyIME")]
    [InlineData("IMM32.dll", "MissingFunction")]
    public void UnknownImport_ReturnsNoSlot(string module, string function)
        => WithImage(CreateImage(), image => Assert.Equal(0,
            LegacyImeCompatibility.FindDelayImport(image, module, function)));

    [Fact]
    public void OutOfBoundsTable_IsRejected()
    {
        var bytes = CreateImage();
        Write32(bytes, 0x170, 0xFF0);
        WithImage(bytes, image => Assert.Equal(0,
            LegacyImeCompatibility.FindDelayImport(image, "IMM32.dll", "ImmDisableLegacyIME")));
    }

    [Fact]
    public void OrdinalImport_IsSkipped()
    {
        var bytes = CreateImage();
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x500), 0x8000000000000001);
        WithImage(bytes, image => Assert.Equal(0,
            LegacyImeCompatibility.FindDelayImport(image, "IMM32.dll", "ImmDisableLegacyIME")));
    }

    [Fact]
    public void UnsupportedDescriptor_IsRejected()
    {
        var bytes = CreateImage();
        Write32(bytes, 0x200, 0);
        WithImage(bytes, image => Assert.Equal(0,
            LegacyImeCompatibility.FindDelayImport(image, "IMM32.dll", "ImmDisableLegacyIME")));
    }

    private static byte[] CreateImage()
    {
        var bytes = new byte[4096];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0), 0x5A4D);
        Write32(bytes, 0x3C, 0x80);
        Write32(bytes, 0x80, 0x4550);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x98), 0x20B);
        Write32(bytes, 0xD0, bytes.Length);
        Write32(bytes, 0x170, 0x200);
        Write32(bytes, 0x174, 64);
        Write32(bytes, 0x200, 1);
        Write32(bytes, 0x204, 0x300);
        Write32(bytes, 0x20C, 0x400);
        Write32(bytes, 0x210, 0x500);
        Encoding.ASCII.GetBytes("IMM32.dll\0").CopyTo(bytes, 0x300);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(0x500), 0x600);
        Encoding.ASCII.GetBytes("ImmDisableLegacyIME\0").CopyTo(bytes, 0x602);
        return bytes;
    }

    private static void Write32(byte[] bytes, int offset, int value)
        => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);

    private static void WithImage(byte[] bytes, Action<nint> assert)
    {
        var image = Marshal.AllocHGlobal(bytes.Length);
        try { Marshal.Copy(bytes, 0, image, bytes.Length); assert(image); }
        finally { Marshal.FreeHGlobal(image); }
    }
}
