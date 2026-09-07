using System.Globalization;
using System.Text;
using Sonolume.Engine.Render;

namespace Sonolume.Engine.Output;

/// <summary>
/// Wire format for the SignalRGB effect. Version-prefixed, compact text:
///   L1|instance|projectName|zoneIndex:id:name:x:y:w:h:cw:ch;...
///   S1|instance|seq|zoneIndex:WWHHrrggbb...;zoneIndex:...
///   C1|instance
/// Pure functions so the protocol is covered by golden tests without a socket.
/// </summary>
public static class FrameEncoder
{
    public const int ProtocolVersion = 1;

    /// <summary>
    /// L1|instance|projectName|index:id:name:x:y:w:h:cw:ch;...
    /// Delimited rather than JSON: SignalRGB rewrites braces and quotes on the way into the effect.
    /// Names have the delimiter characters replaced; they are display-only.
    /// </summary>
    public static string EncodeLayout(Layout layout)
    {
        var sb = new StringBuilder(64 + layout.Zones.Count * 64);
        sb.Append('L').Append(ProtocolVersion).Append('|').Append(Safe(layout.InstanceId)).Append('|').Append(Safe(layout.ProjectName)).Append('|');
        for (int i = 0; i < layout.Zones.Count; i++)
        {
            var z = layout.Zones[i];
            if (i > 0) sb.Append(';');
            sb.Append(z.Index).Append(':').Append(Safe(z.Id)).Append(':').Append(Safe(z.Name)).Append(':');
            sb.Append(Num(z.Rect.X)).Append(':').Append(Num(z.Rect.Y)).Append(':').Append(Num(z.Rect.W)).Append(':').Append(Num(z.Rect.H)).Append(':');
            sb.Append(z.CellsW).Append(':').Append(z.CellsH);
        }
        return sb.ToString();
    }

    private static string Safe(string s)
    {
        if (s.IndexOfAny(['|', ':', ';', '%', '&', '=', '+', '#']) < 0) return s;
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] is '|' or ':' or ';' or '%' or '&' or '=' or '+' or '#') chars[i] = '_';
        return new string(chars);
    }

    public static string EncodeFrame(Frame frame, string instanceId)
    {
        int cellChars = 0;
        foreach (var r in frame.Regions) cellChars += r.Cells.Length * 6 + 12;
        var sb = new StringBuilder(24 + instanceId.Length + cellChars);
        sb.Append('S').Append(ProtocolVersion).Append('|').Append(instanceId).Append('|').Append(frame.Seq).Append('|');

        Span<char> hex = stackalloc char[6];
        for (int i = 0; i < frame.Regions.Count; i++)
        {
            var r = frame.Regions[i];
            if (i > 0) sb.Append(';');
            sb.Append(r.ZoneIndex).Append(':');
            AppendHexByte(sb, (byte)Math.Clamp(r.CellsW, 1, 255));
            AppendHexByte(sb, (byte)Math.Clamp(r.CellsH, 1, 255));
            foreach (var c in r.Cells)
            {
                c.WriteHex(hex);
                sb.Append(hex);
            }
        }
        return sb.ToString();
    }

    public static string EncodeClear(string instanceId) => $"C{ProtocolVersion}|{instanceId}";

    private static string Num(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static void AppendHexByte(StringBuilder sb, byte value)
    {
        const string digits = "0123456789ABCDEF";
        sb.Append(digits[value >> 4]).Append(digits[value & 0xF]);
    }

}
