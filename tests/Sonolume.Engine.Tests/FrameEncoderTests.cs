using Sonolume.Engine.Core;
using Sonolume.Engine.Output;
using Sonolume.Engine.Render;
using Xunit;

namespace Sonolume.Engine.Tests;

public class FrameEncoderTests
{
    [Fact]
    public void Layout_Golden()
    {
        var layout = new Layout("abc12345", "Default Kit", new[]
        {
            new LayoutZone(0, "kick", "Kick", new RectF(0.04f, 0.56f, 0.28f, 0.38f), 1, 1),
            new LayoutZone(1, "pad", "Pad A:B|C", new RectF(0f, 0f, 1f, 0.5f), 8, 2),
        });

        Assert.Equal(
            "L1|abc12345|Default Kit|0:kick:Kick:0.04:0.56:0.28:0.38:1:1;1:pad:Pad A_B_C:0:0:1:0.5:8:2",
            FrameEncoder.EncodeLayout(layout));
    }

    [Fact]
    public void Frame_Golden()
    {
        var frame = new Frame(42, 0, new[]
        {
            new Region(0, "kick", new RectF(0, 0, 1, 1), 1, 1, new[] { new Rgb8(255, 0, 0) }),
            new Region(2, "hihat", new RectF(0, 0, 1, 1), 2, 1, new[] { new Rgb8(0, 16, 255), new Rgb8(1, 2, 3) }),
        }, false);

        Assert.Equal("S1|abc12345|42|0:0101FF0000;2:02010010FF010203", FrameEncoder.EncodeFrame(frame, "abc12345"));
    }

    [Fact]
    public void Clear_Golden()
    {
        Assert.Equal("C1|abc12345", FrameEncoder.EncodeClear("abc12345"));
    }

    [Fact]
    public void Color_HexRoundTrip()
    {
        var c = new Rgb8(0xAB, 0x00, 0x7F);
        Assert.Equal("AB007F", c.ToHex());
        Assert.True(Rgb8.TryParseHex("#ab007f", out var parsed));
        Assert.Equal(c, parsed);
    }

    [Fact]
    public void Hsb_PrimaryHues()
    {
        Assert.Equal(new Rgb8(255, 0, 0), new Hsb(0f, 1f, 1f).ToRgb8());
        Assert.Equal(new Rgb8(0, 255, 0), new Hsb(1f / 3f, 1f, 1f).ToRgb8());
        Assert.Equal(new Rgb8(0, 0, 255), new Hsb(2f / 3f, 1f, 1f).ToRgb8());
        Assert.Equal(new Rgb8(128, 128, 128), new Hsb(0.2f, 0f, 0.5f).ToRgb8());
        Assert.Equal(new Rgb8(255, 0, 0), new Hsb(1f, 1f, 1f).ToRgb8());
    }
}
