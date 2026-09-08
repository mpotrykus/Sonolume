using System.Globalization;

namespace Sonolume.Engine.Core;

/// <summary>How a zone composites over the zones below it wherever their rects overlap.</summary>
public enum BlendMode
{
    Normal,
    Additive,
    Multiply,
    Screen,
    Max,
}

public readonly record struct Rgb8(byte R, byte G, byte B)
{
    public static readonly Rgb8 Black = new(0, 0, 0);

    public Rgb8 Scale(float k)
    {
        k = Math.Clamp(k, 0f, 1f);
        return new Rgb8((byte)MathF.Round(R * k), (byte)MathF.Round(G * k), (byte)MathF.Round(B * k));
    }

    /// <summary>Composites <paramref name="top"/> over <paramref name="bottom"/> using the given mode.</summary>
    public static Rgb8 Blend(BlendMode mode, Rgb8 top, Rgb8 bottom) => mode switch
    {
        BlendMode.Additive => new Rgb8(AddByte(top.R, bottom.R), AddByte(top.G, bottom.G), AddByte(top.B, bottom.B)),
        BlendMode.Multiply => new Rgb8(MulByte(top.R, bottom.R), MulByte(top.G, bottom.G), MulByte(top.B, bottom.B)),
        BlendMode.Screen => new Rgb8(ScreenByte(top.R, bottom.R), ScreenByte(top.G, bottom.G), ScreenByte(top.B, bottom.B)),
        BlendMode.Max => new Rgb8(Math.Max(top.R, bottom.R), Math.Max(top.G, bottom.G), Math.Max(top.B, bottom.B)),
        _ => top,
    };

    private static byte AddByte(byte a, byte b) => (byte)Math.Min(255, a + b);
    private static byte MulByte(byte a, byte b) => (byte)(a * b / 255);
    private static byte ScreenByte(byte a, byte b) => (byte)(255 - (255 - a) * (255 - b) / 255);

    /// <summary>Linear interpolation between two colors; <paramref name="t"/> is clamped to 0..1.</summary>
    public static Rgb8 Lerp(Rgb8 a, Rgb8 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Rgb8(
            (byte)MathF.Round(a.R + (b.R - a.R) * t),
            (byte)MathF.Round(a.G + (b.G - a.G) * t),
            (byte)MathF.Round(a.B + (b.B - a.B) * t));
    }

    public string ToHex() => string.Create(6, this, static (span, c) => c.WriteHex(span));

    public void WriteHex(Span<char> dest)
    {
        WriteHexByte(dest, R);
        WriteHexByte(dest[2..], G);
        WriteHexByte(dest[4..], B);
    }

    private static void WriteHexByte(Span<char> dest, byte value)
    {
        const string digits = "0123456789ABCDEF";
        dest[0] = digits[value >> 4];
        dest[1] = digits[value & 0xF];
    }

    public static bool TryParseHex(ReadOnlySpan<char> text, out Rgb8 color)
    {
        color = Black;
        if (text.Length > 0 && text[0] == '#') text = text[1..];
        if (text.Length != 6) return false;
        if (!byte.TryParse(text[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)) return false;
        if (!byte.TryParse(text[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)) return false;
        if (!byte.TryParse(text[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;
        color = new Rgb8(r, g, b);
        return true;
    }
}

/// <summary>Hue, saturation, brightness, each 0..1. Hue wraps.</summary>
public readonly record struct Hsb(float H, float S, float B)
{
    public Rgb8 ToRgb8()
    {
        float h = H - MathF.Floor(H);
        float s = Math.Clamp(S, 0f, 1f);
        float b = Math.Clamp(B, 0f, 1f);

        float h6 = h * 6f;
        int sector = (int)MathF.Floor(h6) % 6;
        float f = h6 - MathF.Floor(h6);
        float p = b * (1f - s);
        float q = b * (1f - s * f);
        float t = b * (1f - s * (1f - f));

        (float r, float g, float bl) = sector switch
        {
            0 => (b, t, p),
            1 => (q, b, p),
            2 => (p, b, t),
            3 => (p, q, b),
            4 => (t, p, b),
            _ => (b, p, q),
        };
        return new Rgb8(ToByte(r), ToByte(g), ToByte(bl));
    }

    private static byte ToByte(float v) => (byte)MathF.Round(Math.Clamp(v, 0f, 1f) * 255f);
}
