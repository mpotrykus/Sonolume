using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sonolume.Engine.Core;

namespace Sonolume.Transport;

public sealed record SignalRgbDeviceLayout(string Uid, string Name, RectF Rect, float Rotation);

/// <summary>Result of a layout read: devices with a single, unambiguous canvas rect, the names of any
/// multi-component controllers SignalRGB couldn't report a meaningful position for (see
/// <see cref="SignalRgbMcpClient.ReadDeviceLayoutsAsync"/>), and the canvas size they were normalized against
/// (0 if no device yielded one) - handy to prefill a manual entry for one of the skipped controllers, since the
/// canvas size is shared by every device and rarely changes.</summary>
public sealed record SignalRgbImportResult(IReadOnlyList<SignalRgbDeviceLayout> Devices, IReadOnlyList<string> SkippedMultiComponent, float CanvasWidth, float CanvasHeight);

/// <summary>
/// Reads the current device layout from SignalRGB's local MCP server (127.0.0.1:16037/mcp, JSON-RPC 2.0 over
/// HTTP - the same "SignalRgb Core" server signalctl.ps1 in the signalrgb-cli scratch project talks to), so a
/// canvas laid out in SignalRGB's own Layout editor can be imported as Sonolume zones. Read-only: calls only
/// read_devices/read_components/device_position, all free (not SignalRGB Pro-gated), and never writes device state.
/// </summary>
public sealed class SignalRgbMcpClient(string endpoint = "http://127.0.0.1:16037/mcp") : IDisposable
{
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(5) };

    // "uid:1532:021e:7-44A42BA-0-3, name:Razer Ornata Chroma, type:Keyboard, enabled:true, ..."
    // The leading "// canvas count: ..., visible canvas dimensions: [...]" line deliberately doesn't match.
    private static readonly Regex DeviceLineRegex = new(@"^uid:(?<uid>[^,]+), name:(?<name>[^,]+),", RegexOptions.Compiled);

    // "device position: [126, 101], rotation: 0, size: [22, 6], scale: [3.22727, 2.5], visible canvas size: [320, 200]"
    private static readonly Regex PositionRegex = new(
        @"device position: \[(?<x>-?[\d.]+),\s*(?<y>-?[\d.]+)\], rotation: (?<rot>-?[\d.]+), size: \[(?<w>-?[\d.]+),\s*(?<h>-?[\d.]+)\], scale: \[(?<xs>[\d.]+),\s*(?<ys>[\d.]+)\], visible canvas size: \[(?<cw>[\d.]+),\s*(?<ch>[\d.]+)\]",
        RegexOptions.Compiled);

    // "controller:ThermalTake LedBox, channel:Channel 1, number_of_components:1"
    private static readonly Regex ComponentLineRegex = new(
        @"^controller:(?<controller>.+), channel:(?<channel>[^,]+), number_of_components:(?<count>\d+)$",
        RegexOptions.Compiled);

    /// <summary>
    /// Reads every enabled device's canvas position/size/rotation and normalizes it into Sonolume's 0..1 layout
    /// space (<see cref="RectF"/>) using the canvas size reported alongside each device's own position - no
    /// separate lookup needed. Position is assumed to be the device's top-left corner (not center) and rotation
    /// degrees are assumed clockwise around the rect's center, matching <see cref="Zone.Rotation"/>'s convention;
    /// SignalRGB doesn't document either, so this is the best-effort reading of its Layout panel's X/Y/Width/
    /// Height/Rotation fields - visually nudge a zone afterward if an import lands rotated or offset from what
    /// SignalRGB's own Layout tab shows.
    ///
    /// A controller wired to more than one populated channel (e.g. a fan-hub lighting controller with several
    /// fans attached) reports a meaningless placeholder from device_position - SignalRGB has nowhere to hang
    /// "the" position of a device that is actually several independently-positioned components, and this MCP API
    /// has no tool to read a child component's own position (confirmed: device_position only accepts a top-level
    /// device name/uid; every per-channel identifier format tried against a live instance failed). Such
    /// controllers are reported in <see cref="SignalRgbImportResult.SkippedMultiComponent"/> instead of being
    /// imported as a bogus single zone; a controller with exactly one populated channel is unambiguous and its
    /// device_position is the real position of that one component, so it imports normally.
    /// </summary>
    /// <exception cref="HttpRequestException">SignalRGB isn't running, or its MCP server is disabled.</exception>
    public async Task<SignalRgbImportResult> ReadDeviceLayoutsAsync(CancellationToken ct = default)
    {
        var componentCounts = await ReadComponentCountsAsync(ct);
        var deviceLines = (await CallToolAsync("read_devices", null, ct)).Split('\n');
        var layouts = new List<SignalRgbDeviceLayout>();
        var skipped = new List<string>();
        float canvasWidth = 0, canvasHeight = 0;
        foreach (var line in deviceLines)
        {
            var dm = DeviceLineRegex.Match(line);
            if (!dm.Success) continue;
            string uid = dm.Groups["uid"].Value;
            string name = dm.Groups["name"].Value;

            if (componentCounts.TryGetValue(name, out int count) && count != 1)
            {
                skipped.Add(name);
                continue;
            }

            string posText = await CallToolAsync("device_position", new Dictionary<string, object> { ["name"] = uid }, ct);
            var pm = PositionRegex.Match(posText);
            if (!pm.Success) continue;

            float cw = ParseFloat(pm, "cw"), ch = ParseFloat(pm, "ch");
            if (cw <= 0 || ch <= 0) continue;
            canvasWidth = cw;
            canvasHeight = ch;

            float x = ParseFloat(pm, "x"), y = ParseFloat(pm, "y");
            float w = ParseFloat(pm, "w") * ParseFloat(pm, "xs");
            float h = ParseFloat(pm, "h") * ParseFloat(pm, "ys");

            layouts.Add(new SignalRgbDeviceLayout(uid, name, new RectF(x / cw, y / ch, w / cw, h / ch), ParseFloat(pm, "rot")));
        }
        return new SignalRgbImportResult(layouts, skipped, canvasWidth, canvasHeight);
    }

    /// <summary>Cheap standalone read of just the shared canvas size, for prefilling the manual-entry dialog
    /// without a full device scan: one <c>read_devices</c> call plus a single <c>device_position</c> call against
    /// whichever device it lists first (canvas size comes bundled with every device's position and is shared
    /// across all of them). Returns null if SignalRGB reports no devices at all.</summary>
    /// <exception cref="HttpRequestException">SignalRGB isn't running, or its MCP server is disabled.</exception>
    public async Task<(float Width, float Height)?> TryGetCanvasSizeAsync(CancellationToken ct = default)
    {
        var deviceLines = (await CallToolAsync("read_devices", null, ct)).Split('\n');
        foreach (var line in deviceLines)
        {
            var dm = DeviceLineRegex.Match(line);
            if (!dm.Success) continue;

            string posText = await CallToolAsync("device_position", new Dictionary<string, object> { ["name"] = dm.Groups["uid"].Value }, ct);
            var pm = PositionRegex.Match(posText);
            if (!pm.Success) continue;

            float cw = ParseFloat(pm, "cw"), ch = ParseFloat(pm, "ch");
            if (cw > 0 && ch > 0) return (cw, ch);
        }
        return null;
    }

    /// <summary>Total populated-component count per controller name, summed across its channels (e.g. a fan hub
    /// with 4 occupied channels sums to 4). Devices with no channels at all (a plain keyboard, RAM, ...) simply
    /// don't appear here - callers should treat an absent name as an unambiguous single device, not as zero.</summary>
    private async Task<Dictionary<string, int>> ReadComponentCountsAsync(CancellationToken ct)
    {
        string text = await CallToolAsync("read_components", null, ct);
        var totals = new Dictionary<string, int>();
        foreach (var line in text.Split('\n'))
        {
            var m = ComponentLineRegex.Match(line);
            if (!m.Success) continue;
            string controller = m.Groups["controller"].Value;
            int count = int.Parse(m.Groups["count"].Value, CultureInfo.InvariantCulture);
            totals[controller] = totals.GetValueOrDefault(controller) + count;
        }
        return totals;
    }

    private static float ParseFloat(Match m, string group) => float.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture);

    private async Task<string> CallToolAsync(string name, Dictionary<string, object>? arguments, CancellationToken ct)
    {
        string payload = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name, arguments = arguments ?? new Dictionary<string, object>() },
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"SignalRGB MCP error {error.GetProperty("code").GetInt32()}: {error.GetProperty("message").GetString()}");

        // "content" is an array with one entry per line (not one entry holding an embedded multi-line string) -
        // read_devices in particular returns one entry per device plus a leading canvas-info line.
        return string.Join('\n', root.GetProperty("result").GetProperty("content").EnumerateArray()
            .Select(c => c.GetProperty("text").GetString() ?? ""));
    }

    public void Dispose() => client.Dispose();
}
