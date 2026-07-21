using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using Photobooth.Camera;
using Xunit;

namespace Photobooth.Tests.Unit;

public sealed class EvfDecoderTests
{
    private static byte[] MakeJpeg(int w = 4, int h = 4)
    {
        using var bmp = new Bitmap(w, h);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }

    [Fact]
    public void Decode_ValidJpeg_ReturnsFrozenBitmapSource()
    {
        var result = EvfDecoder.Decode(MakeJpeg());
        Assert.NotNull(result);
        Assert.True(result!.IsFrozen);
    }

    [Fact]
    public void Decode_ValidJpeg_HasCorrectDimensions()
    {
        var result = EvfDecoder.Decode(MakeJpeg(8, 6));
        Assert.NotNull(result);
        Assert.Equal(8, result!.PixelWidth);
        Assert.Equal(6, result.PixelHeight);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    public void Decode_WithRotation90Or270_SwapsDimensions(int rotationDegrees)
    {
        var result = EvfDecoder.Decode(MakeJpeg(8, 6), rotationDegrees);
        Assert.NotNull(result);
        Assert.Equal(6, result!.PixelWidth);
        Assert.Equal(8, result.PixelHeight);
    }

    [Fact]
    public void Decode_WithRotation180_PreservesDimensions()
    {
        var result = EvfDecoder.Decode(MakeJpeg(8, 6), 180);
        Assert.NotNull(result);
        Assert.Equal(8, result!.PixelWidth);
        Assert.Equal(6, result.PixelHeight);
    }

    [Fact]
    public void Decode_DefaultRotation_IsZero()
    {
        // No rotationDegrees argument — must behave exactly like Decode(data, 0)
        var result = EvfDecoder.Decode(MakeJpeg(8, 6));
        Assert.NotNull(result);
        Assert.Equal(8, result!.PixelWidth);
        Assert.Equal(6, result.PixelHeight);
    }

    [Fact]
    public void Decode_EmptyArray_ReturnsNull()
    {
        var result = EvfDecoder.Decode(Array.Empty<byte>());
        Assert.Null(result);
    }

    [Fact]
    public void Decode_InvalidBytes_ReturnsNull()
    {
        var result = EvfDecoder.Decode(new byte[] { 0xFF, 0xFE, 0x00, 0x01 });
        Assert.Null(result);
    }
}
