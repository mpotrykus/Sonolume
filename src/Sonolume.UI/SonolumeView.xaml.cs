using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Sonolume.Engine.Input;
using Sonolume.Engine.Mappings;
using Sonolume.Engine.Model;
using Sonolume.Engine.Output;
using Sonolume.Transport;

namespace Sonolume.UI;

public partial class SonolumeView : UserControl
{
    private static readonly Brush MutedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA3)));
    private static readonly Brush ColorPickerBackdropBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x2B)));

    // Mouse clicks carry no velocity, so the virtual keyboard reports a fixed comfortable strike instead of always-max.
    private const float VirtualKeyboardVelocity = 0.85f;

    private enum SelectionKind { None, Zone, Group }

    private readonly SonolumeSession session;
    private readonly DispatcherTimer previewTimer;
    private readonly DispatcherTimer editorTimer;

    private Project? projectSnapshot;

    /// <summary>Last canvas size seen from SignalRGB, from either an automatic import or a live probe by
    /// <see cref="ManualSignalRgbImport_Click"/>. Used as its fallback prefill when SignalRGB can't be reached
    /// live (it rarely changes between devices). Null until the first successful read.</summary>
    private (float Width, float Height)? lastSignalRgbCanvasSize;

    /// <summary>Last-resort prefill for the manual-import canvas fields when SignalRGB has never been reached
    /// this session - an arbitrary but plausible starting point, not a real SignalRGB default.</summary>
    private static readonly (float Width, float Height) DefaultSignalRgbCanvasSize = (320, 200);

    private SelectionKind currentKind = SelectionKind.None;
    private string? currentId;
    private bool isRefreshingLists;
    private bool suppressCombo;
    private bool suppressInvertToggles;
    private bool suppressParamsRefresh;
    private Action<ParamId, float>? paramsActiveOnChange;
    private Point zoneDragStart;
    private bool zoneDragArmed;
    private bool effectSectionExpanded;
    private bool zoneGeometryExpanded;
    private bool keyboardExpanded;

    /// <summary>Live-bindable controls in the currently-open params panel, so <see cref="RefreshOpenPanel"/> can
    /// push external changes (a CC/macro moving, another view editing the same zone) into them every tick without
    /// rebuilding the whole panel - which would drop an in-progress drag. Rebuilt whenever <see cref="RebuildParamsPanel"/>
    /// runs (selection change); cleared on <see cref="ClearSelection"/>.</summary>
    private readonly Dictionary<ParamId, (Slider Slider, TextBlock ValueText)> paramSliders = new();
    /// <summary>Same purpose as <see cref="paramSliders"/>, for the params rendered as a note-duration dropdown
    /// (EffectSpeed) instead of a slider.</summary>
    private readonly Dictionary<ParamId, ComboBox> paramCombos = new();
    private ColorPickerControl? liveColorPicker;

    /// <summary>Effect chosen in the picker for the current selection before anything is learned; once a
    /// Trigger/Gate mapping exists its own EffectId is shown (and edited) instead. Reset whenever the selected
    /// zone/group changes.</summary>
    private string pendingEffectId = SolidEffect.TypeName;

    /// <summary>The effect-type combo built by <see cref="BuildEffectRow"/> for the current selection, so
    /// <see cref="RefreshEffectKeyControls"/> can keep its current selection (the effect-type macro can change the
    /// live EffectId out from under it) in sync every tick, the same way <see cref="paramSliders"/> does for the
    /// continuous params. Cleared on selection change/<see cref="ClearSelection"/>.</summary>
    private ComboBox? liveEffectCombo;
    private TargetRef? liveEffectTarget;
    private Action<string>? liveEffectOnChanged;
    private bool suppressEffectCombo;

    /// <summary>The blend combo built by <see cref="BuildBlendRow"/> for the current zone, kept live the same way
    /// <see cref="liveEffectCombo"/> is - the blend macro can change the live <see cref="Zone.Blend"/> out from
    /// under it. Zone-only (there is no Group.Blend); cleared on selection change/<see cref="ClearSelection"/>.</summary>
    private ComboBox? liveBlendCombo;
    private TargetRef? liveBlendTarget;
    private bool suppressBlendCombo;

    private bool suppressChannelCombo;
    private bool suppressKeyOctaveCombo;

    private sealed record EffectOption(string Id, string Label, string Range);

    // Order here must match EffectRegistry's registration order - Engine.SelectEffect picks by dividing 0..1 into
    // one equal slice per effect in that same order, and Range below assumes it lines up with this array's index.
    private static readonly EffectOption[] EffectOptions = BuildEffectOptions();

    private static EffectOption[] BuildEffectOptions()
    {
        (string Id, string Label)[] raw =
        [
            (SolidEffect.TypeName, "Solid"),
            (FlashEffect.TypeName, "Flash"),
            (WaveEffect.TypeName, "Wave"),
            (PulseEffect.TypeName, "Pulse"),
            (StrobeEffect.TypeName, "Strobe"),
            (ChaseEffect.TypeName, "Chase"),
            (RippleEffect.TypeName, "Ripple"),
            (SparkleEffect.TypeName, "Sparkle"),
            (RainbowEffect.TypeName, "Rainbow"),
        ];
        return raw.Select((o, i) => new EffectOption(o.Id, o.Label, EqualSliceRangeText(i, raw.Length))).ToArray();
    }

    private sealed record NoteDurationOption(string Label, string Range);

    private sealed record BlendOption(BlendMode Mode, string Label, string Range);

    // Order here must match Engine.SelectBlend's BlendModes array (Enum.GetValues<BlendMode> declaration order) -
    // it picks by dividing 0..1 into one equal slice per mode in that same order, same as EffectOptions/SelectEffect.
    private static readonly BlendOption[] BlendOptions = Enum.GetValues<BlendMode>()
        .Select((mode, i) => new BlendOption(mode, mode.ToString(), EqualSliceRangeText(i, Enum.GetValues<BlendMode>().Length)))
        .ToArray();

    private sealed record ChannelOption(int Value, string Label);

    private static readonly ChannelOption[] ChannelOptions =
    [
        new(SourceAddress.Any, "Any"),
        .. Enumerable.Range(0, 16).Select(channel => new ChannelOption(channel, $"CH {channel + 1}")),
    ];

    private sealed record KeyOctaveOption(int Note, string Label);

    // Every "C" note MIDI actually has room for: note 0 (C-1) through note 120 (C9, the last multiple of 12 that
    // still fits in the 0-127 range) - one below the highest root note, C10, would need note 132.
    private static readonly KeyOctaveOption[] KeyOctaveOptions =
        Enumerable.Range(0, 11).Select(octave => new KeyOctaveOption(octave * 12, $"C{octave - 1}")).ToArray();

    private Window OwnerWindow => Window.GetWindow(this) ?? throw new InvalidOperationException("SonolumeView is not hosted in a Window.");

    public SonolumeView(SonolumeSession session)
    {
        this.session = session;
        InitializeComponent();

        ZoneChannelCombo.ItemsSource = ChannelOptions;
        ZoneChannelCombo.SelectedIndex = 0;
        GroupChannelCombo.ItemsSource = ChannelOptions;
        GroupChannelCombo.SelectedIndex = 0;

        ZoneKeyOctaveCombo.ItemsSource = KeyOctaveOptions;
        GroupKeyOctaveCombo.ItemsSource = KeyOctaveOptions;

        Preview.Editable = true;
        Preview.ZoneClicked += id => session.SelectedZoneId = id;
        Preview.ZoneRectCommitted += CommitZoneRect;
        Preview.ZoneRotationCommitted += CommitZoneRotation;

        VirtualKeyboard.NoteOn += note => session.Enqueue(MidiEvent.NoteOn(0, note, VirtualKeyboardVelocity, Clock.Now()));
        VirtualKeyboard.NoteOff += note => session.Enqueue(MidiEvent.NoteOff(0, note, 0f, Clock.Now()));
        VirtualKeyboard.NoteHovered += note => KeyboardNoteText.Text = note is { } n ? FormatNoteName(n) : "";
        VirtualKeyboard.RangeChanged += RefreshKeyboardRange;
        RefreshKeyboardRange();

        previewTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        previewTimer.Tick += (_, _) => RefreshPreview();

        editorTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        editorTimer.Tick += (_, _) => RefreshEditor();

        Loaded += (_, _) => { RefreshPreview(); RefreshEditor(); previewTimer.Start(); editorTimer.Start(); };
        Unloaded += (_, _) => { previewTimer.Stop(); editorTimer.Stop(); };
    }

    // Opt-in for hosts with a translucent backdrop of their own (e.g. a Mica window):
    // the DAW plugin editor keeps the fully opaque default since it has no such backdrop.
    public void UseTranslucentPanels(byte alpha)
    {
        var top = WithAlpha((Color)FindResource("PanelBackgroundTopColor"), alpha);
        var bottom = WithAlpha((Color)FindResource("PanelBackgroundColor"), alpha);
        var brush = Freeze(new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1)));
        foreach (var border in new[] { LeftPanelBorder, CenterPanelBorder, RightPanelBorder })
            border.Background = brush;
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private void CommitZoneRect(string id, RectF rect)
    {
        try
        {
            session.UpdateZone(id, z => z.Rect = rect);
        }
        catch (InvalidDataException)
        {
            // The zone was deleted (e.g. via the zone list) while the drag was in flight; nothing to update.
        }
    }

    private void CommitZoneRotation(string id, float rotation)
    {
        try
        {
            session.UpdateZone(id, z => z.Rotation = rotation);
        }
        catch (InvalidDataException)
        {
            // The zone was deleted (e.g. via the zone list) while the drag was in flight; nothing to update.
        }
    }

    private void RefreshPreview()
    {
        var snapshot = session.Runner.LatestSnapshot;
        Preview.Snapshot = snapshot;
        Preview.SelectedZoneId = session.SelectedZoneId;

        if (snapshot is not null)
        {
            if (!ProjectNameBox.IsFocused) ProjectNameBox.Text = snapshot.ProjectName;
        }

        UpdateSinkStatus(session.Sink.Status);
    }

    private void UpdateSinkStatus(SinkStatus status)
    {
        StatusDot.Fill = status.State == SinkState.Connected ? LatencyBrush(status.LastSendMs) : Brushes.Transparent;
        SignalRgbStatusPanel.ToolTip = status.State switch
        {
            SinkState.Connected => $"SignalRGB: connected ({LatencyLabel(status.LastSendMs)} latency)",
            SinkState.Offline => "SignalRGB: offline (is SignalRGB running?)",
            SinkState.Error => $"SignalRGB: {status.Message}",
            _ => "SignalRGB: connecting...",
        };
        StatusMsText.Text = $"{status.LastSendMs:0.0} ms";
    }

    private Brush LatencyBrush(double lastSendMs) => lastSendMs switch
    {
        <= 20 => (Brush)FindResource("SuccessBrush"),
        <= 50 => (Brush)FindResource("WarningBrush"),
        _ => (Brush)FindResource("DangerBrush"),
    };

    private static string LatencyLabel(double lastSendMs) => lastSendMs switch
    {
        <= 20 => "good",
        <= 50 => "elevated",
        _ => "high",
    };

    // --- Zone/group editor: bottom + right panels ---

    private void RefreshEditor()
    {
        var project = session.GetProjectCopy();
        projectSnapshot = project;

        UndoButton.IsEnabled = session.CanUndo;
        RedoButton.IsEnabled = session.CanRedo;
        SaveMenuItem.IsEnabled = session.HasUnsavedChanges;

        var zoneKeyLabels = BuildKeyLabels(project.Mappings, TargetKind.Zone);
        var groupKeyLabels = BuildKeyLabels(project.Mappings, TargetKind.Group);

        isRefreshingLists = true;
        ReconcileList(ZoneList,
            project.Zones.OrderByDescending(z => z.ZIndex).Select(z => new ZoneRow(z, zoneKeyLabels.GetValueOrDefault(z.Id))),
            currentKind == SelectionKind.Zone ? currentId : null, r => r.Id);
        ReconcileList(GroupList,
            project.Groups.Select(g => new GroupRow(g, groupKeyLabels.GetValueOrDefault(g.Id))),
            currentKind == SelectionKind.Group ? currentId : null, r => r.Id);
        isRefreshingLists = false;

        SyncExternalSelection(project);
        RefreshOpenPanel(project);
    }

    /// <summary>Picks up a selection made elsewhere (the always-visible canvas, which shares
    /// <see cref="SonolumeSession.SelectedZoneId"/>) so the zone list and detail panels follow it.</summary>
    private void SyncExternalSelection(Project project)
    {
        string? sessionZoneId = session.SelectedZoneId;
        string? localZoneId = currentKind == SelectionKind.Zone ? currentId : null;
        if (sessionZoneId == localZoneId) return;

        if (sessionZoneId is not null && project.FindZone(sessionZoneId) is not null)
        {
            SelectZone(sessionZoneId);
        }
        else if (currentKind == SelectionKind.Zone)
        {
            ClearSelection();
            isRefreshingLists = true;
            ZoneList.SelectedItem = null;
            isRefreshingLists = false;
        }
    }

    private static void ReconcileList<T>(ListBox listBox, IEnumerable<T> items, string? selectedId, Func<T, string> idOf)
    {
        var list = items.ToList();
        listBox.ItemsSource = list;
        if (selectedId is not null) listBox.SelectedItem = list.FirstOrDefault(i => idOf(i) == selectedId);
    }

    private void RefreshOpenPanel(Project project)
    {
        if (currentKind == SelectionKind.Zone)
        {
            var zone = project.FindZone(currentId!);
            if (zone is null) { ClearSelection(); return; }

            suppressInvertToggles = true;
            ZoneInvertXToggle.IsChecked = zone.InvertX;
            ZoneInvertYToggle.IsChecked = zone.InvertY;
            suppressInvertToggles = false;

            SetIfNotFocused(ZoneNameBox, zone.Name);
            SetIfNotFocused(ZoneXBox, FmtPercent(zone.Rect.X));
            SetIfNotFocused(ZoneYBox, FmtPercent(zone.Rect.Y));
            SetIfNotFocused(ZoneWBox, FmtPercent(zone.Rect.W));
            SetIfNotFocused(ZoneHBox, FmtPercent(zone.Rect.H));
            SetIfNotFocused(ZoneCellsWBox, zone.CellsW.ToString());
            SetIfNotFocused(ZoneRotationBox, Fmt(zone.Rotation));
            SetIfNotFocused(ZoneCellsHBox, zone.CellsH.ToString());

            if (!ZoneGroupCombo.IsDropDownOpen)
            {
                suppressCombo = true;
                SetGroupCombo(ZoneGroupCombo, project, zone.GroupId, excludeId: null);
                suppressCombo = false;
            }

            RefreshKeyControls(TargetRef.Zone(zone.Id), TargetKind.Zone, project, ZoneKeyOctaveCombo, ZoneChannelCombo);
            RefreshParamsLive(zone.Params);
            RefreshEffectKeyControls(TargetRef.Zone(zone.Id), project);
            RefreshBlendKeyControls(TargetRef.Zone(zone.Id), zone.Blend);
        }
        else if (currentKind == SelectionKind.Group)
        {
            var group = project.FindGroup(currentId!);
            if (group is null) { ClearSelection(); return; }

            SetIfNotFocused(GroupNameBox, group.Name);

            if (!GroupParentCombo.IsDropDownOpen)
            {
                suppressCombo = true;
                SetGroupCombo(GroupParentCombo, project, group.ParentId, excludeId: group.Id);
                suppressCombo = false;
            }

            RefreshKeyControls(TargetRef.Group(group.Id), TargetKind.Group, project, GroupKeyOctaveCombo, GroupChannelCombo);
            RefreshParamsLive(group.Params);
            RefreshEffectKeyControls(TargetRef.Group(group.Id), project);
        }
    }

    /// <summary>Keeps the effect-type combo built by <see cref="BuildEffectRow"/> in sync every editor tick: its
    /// current selection, after the effect-type macro changes the target's live EffectId out from under it.
    /// Skipped while the dropdown is open, so this never yanks it out from under a browsing user.</summary>
    private void RefreshEffectKeyControls(TargetRef target, Project project)
    {
        if (liveEffectCombo is null || liveEffectTarget != target) return;
        if (liveEffectCombo.IsDropDownOpen) return;

        string effectId = project.Mappings
            .FirstOrDefault(m => m.Target == target && m.Mode is MappingMode.Trigger or MappingMode.Gate)?.EffectId ?? pendingEffectId;
        var selected = Array.Find(EffectOptions, o => o.Id == effectId) ?? EffectOptions[0];

        if (Equals(liveEffectCombo.SelectedItem, selected)) return;

        suppressEffectCombo = true;
        liveEffectCombo.SelectedItem = selected;
        suppressEffectCombo = false;
        liveEffectOnChanged?.Invoke(selected.Id);
    }

    /// <summary>Keeps the blend combo built by <see cref="BuildBlendRow"/> in sync every editor tick, the same way
    /// <see cref="RefreshEffectKeyControls"/> does for the effect-type combo: its current selection, after the
    /// blend macro changes the live <see cref="Zone.Blend"/> out from under it. Skipped while the dropdown is open.</summary>
    private void RefreshBlendKeyControls(TargetRef target, BlendMode currentBlend)
    {
        if (liveBlendCombo is null || liveBlendTarget != target) return;
        if (liveBlendCombo.IsDropDownOpen) return;
        if (liveBlendCombo.SelectedItem is BlendOption { Mode: var selected } && selected == currentBlend) return;

        suppressBlendCombo = true;
        liveBlendCombo.SelectedItem = Array.Find(BlendOptions, o => o.Mode == currentBlend);
        suppressBlendCombo = false;
    }

    /// <summary>Pushes the target's current param values into the sliders/color wheel built by
    /// <see cref="RebuildParamsPanel"/>, every editor tick - so a value changing from outside this view (a CC/macro
    /// moving, or the same zone open in another window) is visible without needing to reselect the zone. Skips any
    /// control the user currently has their mouse down on, so this never fights an in-progress drag; guarded by
    /// <see cref="suppressParamsRefresh"/> so pushing the value in doesn't loop back through onChange and re-send
    /// the same value to the session on every tick.</summary>
    private void RefreshParamsLive(ParamSet values)
    {
        suppressParamsRefresh = true;
        foreach (var (id, (slider, valueText)) in paramSliders)
        {
            if (slider.IsMouseCaptureWithin) continue;
            float raw = values[id];
            if (Approximately((float)slider.Value, raw)) continue;
            slider.Value = raw;
            valueText.Text = FmtSlider(id, ParamInfos.Of(id), raw);
        }
        foreach (var (id, combo) in paramCombos)
        {
            if (combo.IsDropDownOpen) continue;
            int index = NoteDuration.IndexOf(values[id]);
            if (combo.SelectedIndex != index) combo.SelectedIndex = index;
        }
        if (liveColorPicker is { IsMouseCaptured: false } picker)
        {
            picker.SetColor(values[ParamId.Hue], values[ParamId.Saturation], values[ParamId.Brightness]);
        }
        suppressParamsRefresh = false;
    }

    /// <summary>Keeps a zone/group's "Key" row - the octave combo and the channel combo - in sync with its current
    /// mapping(s), the same way <see cref="RefreshOpenPanel"/> keeps every other field in sync; both mirror the
    /// Key mapping's <see cref="SourceAddress"/> directly, the same live-editing pattern as
    /// <see cref="RefreshBlendKeyControls"/> - each skipped while its own dropdown is open, guarded by
    /// <see cref="suppressKeyOctaveCombo"/>/<see cref="suppressChannelCombo"/> so pushing the value in doesn't loop
    /// back through <see cref="KeyOctaveCombo_SelectionChanged"/>/<see cref="ChannelCombo_SelectionChanged"/>.</summary>
    private void RefreshKeyControls(TargetRef target, TargetKind kind, Project project, ComboBox octaveCombo, ComboBox channelCombo)
    {
        var keyMapping = project.Mappings.FirstOrDefault(m => m.Enabled && m.Target == target && m.Mode is MappingMode.Trigger or MappingMode.Gate);

        if (!octaveCombo.IsDropDownOpen)
        {
            suppressKeyOctaveCombo = true;
            octaveCombo.SelectedItem = keyMapping is null ? null : ClosestKeyOctaveOption(keyMapping.Source.Number);
            suppressKeyOctaveCombo = false;
            octaveCombo.IsEnabled = keyMapping is not null;
        }

        if (!channelCombo.IsDropDownOpen)
        {
            int? channel = keyMapping?.Source.Channel;
            suppressChannelCombo = true;
            channelCombo.SelectedItem = ChannelOptions.FirstOrDefault(o => o.Value == (channel ?? SourceAddress.Any)) ?? ChannelOptions[0];
            suppressChannelCombo = false;
            channelCombo.IsEnabled = channel is not null;
        }
    }

    // A pre-existing Key mapping's note is always an exact "C" (see DefaultMacros.KeySourceOf) unless it predates
    // this convention (an older save learned onto some other note) - fall back to the nearest "C" rather than
    // leaving the combo with no selection at all.
    private static KeyOctaveOption ClosestKeyOctaveOption(int note) =>
        KeyOctaveOptions.FirstOrDefault(o => o.Note == note)
        ?? KeyOctaveOptions.OrderBy(o => Math.Abs(o.Note - note)).First();

    private void KeyOctaveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressKeyOctaveCombo) return;
        if (CurrentTarget() is not { } target) return;
        if (sender is ComboBox { SelectedItem: KeyOctaveOption option }) session.SetKeyOctave(target, option.Note);
    }

    private void ChannelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressChannelCombo) return;
        if (CurrentTarget() is not { } target) return;
        if (sender is ComboBox { SelectedItem: ChannelOption option }) session.SetKeyChannel(target, option.Value);
    }

    private TargetRef? CurrentTarget() => (currentKind, currentId) switch
    {
        (SelectionKind.Zone, { } id) => TargetRef.Zone(id),
        (SelectionKind.Group, { } id) => TargetRef.Group(id),
        _ => null,
    };

    private void ClearSelection()
    {
        currentKind = SelectionKind.None;
        currentId = null;
        session.SelectedZoneId = null;
        UpdateRemoveButtonsEnabled();
        ZoneIdentityPanel.Visibility = Visibility.Collapsed;
        GroupIdentityPanel.Visibility = Visibility.Collapsed;
        ZoneGeometryBorder.Visibility = Visibility.Collapsed;
        EmptyFieldsPanel.Visibility = Visibility.Visible;
        ParamsPanel.Children.Clear();
        ParamsTitle.Visibility = Visibility.Collapsed;
        ParamsActiveCheckBox.Visibility = Visibility.Collapsed;
        ParamsActiveCheckBox.Checked -= ParamsActiveCheckBox_Changed;
        ParamsActiveCheckBox.Unchecked -= ParamsActiveCheckBox_Changed;
        paramsActiveOnChange = null;
        paramSliders.Clear();
        paramCombos.Clear();
        liveColorPicker = null;
        liveEffectCombo = null;
        liveEffectTarget = null;
        liveEffectOnChanged = null;
        liveBlendCombo = null;
        liveBlendTarget = null;
    }

    // --- Selection ---

    private void ZoneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshingLists) return;
        if (ZoneList.SelectedItem is not ZoneRow row) return;
        isRefreshingLists = true;
        GroupList.SelectedItem = null;
        isRefreshingLists = false;
        currentKind = SelectionKind.Zone;
        currentId = row.Id;
        LoadZonePanel(row.Zone, projectSnapshot!);
        UpdateRemoveButtonsEnabled();
    }

    private void UpdateRemoveButtonsEnabled()
    {
        RemoveZoneButton.IsEnabled = currentKind == SelectionKind.Zone;
        RemoveGroupButton.IsEnabled = currentKind == SelectionKind.Group;
    }

    // --- Zone list drag-and-drop reordering (sets ZIndex to match the on-screen order) ---

    private void ZoneList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        zoneDragStart = e.GetPosition(ZoneList);
        zoneDragArmed = ZoneAt(zoneDragStart) is not null;
    }

    private void ZoneList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!zoneDragArmed || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(ZoneList);
        if (Math.Abs(pos.X - zoneDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - zoneDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        zoneDragArmed = false;
        if (ZoneAt(zoneDragStart) is not { } zone) return;
        DragDrop.DoDragDrop(ZoneList, zone.Id, DragDropEffects.Move);
    }

    private void ZoneList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(string)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void ZoneList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(string)) is not string draggedId) return;
        var list = (ZoneList.ItemsSource as IEnumerable<ZoneRow>)?.ToList();
        if (list is null) return;

        int fromIndex = list.FindIndex(r => r.Id == draggedId);
        if (fromIndex < 0) return;

        var target = ZoneAt(e.GetPosition(ZoneList));
        int toIndex = target is not null ? list.FindIndex(r => r.Id == target.Id) : list.Count - 1;
        if (toIndex == fromIndex) return;

        var moved = list[fromIndex];
        list.RemoveAt(fromIndex);
        if (toIndex > fromIndex) toIndex--;
        list.Insert(toIndex, moved);

        try
        {
            session.ReorderZones(list.Select(r => r.Id).ToList());
        }
        catch (Exception ex)
        {
            AppDialog.ShowMessage(OwnerWindow, ex.Message, "Reorder zones");
        }
        RefreshEditor();
    }

    /// <summary>The zone row (if any) that contains <paramref name="position"/>, in <see cref="ZoneList"/>
    /// coordinates. The list is displayed top-to-bottom in descending ZIndex order, so this doubles as
    /// "topmost layer under the cursor".</summary>
    private ZoneRow? ZoneAt(Point position)
    {
        var hit = VisualTreeHelper.HitTest(ZoneList, position)?.VisualHit;
        while (hit is not null && hit is not ListBoxItem) hit = VisualTreeHelper.GetParent(hit);
        return (hit as ListBoxItem)?.DataContext as ZoneRow;
    }

    private void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshingLists) return;
        if (GroupList.SelectedItem is not GroupRow row) return;
        isRefreshingLists = true;
        ZoneList.SelectedItem = null;
        isRefreshingLists = false;
        currentKind = SelectionKind.Group;
        currentId = row.Id;
        LoadGroupPanel(row.Group, projectSnapshot!);
        UpdateRemoveButtonsEnabled();
    }

    private void SelectZone(string id)
    {
        var row = (ZoneList.ItemsSource as IEnumerable<ZoneRow>)?.FirstOrDefault(r => r.Id == id);
        isRefreshingLists = true;
        ZoneList.SelectedItem = row;
        GroupList.SelectedItem = null;
        isRefreshingLists = false;
        if (row is null) return;
        currentKind = SelectionKind.Zone;
        currentId = id;
        LoadZonePanel(row.Zone, projectSnapshot!);
        UpdateRemoveButtonsEnabled();
    }

    private void SelectGroup(string id)
    {
        var row = (GroupList.ItemsSource as IEnumerable<GroupRow>)?.FirstOrDefault(r => r.Id == id);
        isRefreshingLists = true;
        GroupList.SelectedItem = row;
        ZoneList.SelectedItem = null;
        isRefreshingLists = false;
        if (row is null) return;
        currentKind = SelectionKind.Group;
        currentId = id;
        LoadGroupPanel(row.Group, projectSnapshot!);
        UpdateRemoveButtonsEnabled();
    }

    // --- Zone panel ---

    private void LoadZonePanel(Zone zone, Project project)
    {
        ZoneIdentityPanel.Visibility = Visibility.Visible;
        GroupIdentityPanel.Visibility = Visibility.Collapsed;
        ZoneGeometryBorder.Visibility = Visibility.Visible;
        ZoneGeometryHeader.Margin = new Thickness(-12, -12, -12, zoneGeometryExpanded ? 10 : -12);
        ZoneGeometryContent.Visibility = zoneGeometryExpanded ? Visibility.Visible : Visibility.Collapsed;
        ZoneGeometryChevron.Text = zoneGeometryExpanded ? "" : "";
        EmptyFieldsPanel.Visibility = Visibility.Collapsed;
        session.SelectedZoneId = zone.Id;
        pendingEffectId = SolidEffect.TypeName;

        suppressInvertToggles = true;
        ZoneInvertXToggle.IsChecked = zone.InvertX;
        ZoneInvertYToggle.IsChecked = zone.InvertY;
        suppressInvertToggles = false;

        ZoneNameBox.Text = zone.Name;
        ZoneXBox.Text = FmtPercent(zone.Rect.X);
        ZoneYBox.Text = FmtPercent(zone.Rect.Y);
        ZoneWBox.Text = FmtPercent(zone.Rect.W);
        ZoneHBox.Text = FmtPercent(zone.Rect.H);
        ZoneCellsWBox.Text = zone.CellsW.ToString();
        ZoneCellsHBox.Text = zone.CellsH.ToString();
        ZoneRotationBox.Text = Fmt(zone.Rotation);

        suppressCombo = true;
        SetGroupCombo(ZoneGroupCombo, project, zone.GroupId, excludeId: null);
        suppressCombo = false;

        string zoneId = zone.Id;
        ParamsTitle.Text = zone.Name;
        ParamsTitle.Visibility = Visibility.Visible;
        RebuildParamsPanel(ParamsPanel, zone.Params, TargetRef.Zone(zoneId), (id, raw) => session.SetParam(TargetRef.Zone(zoneId), id, raw),
            BuildBlendRow(zone, TargetRef.Zone(zoneId), mode => CommitZoneUpdate(zoneId, z => z.Blend = mode)));

        RefreshKeyControls(TargetRef.Zone(zoneId), TargetKind.Zone, project, ZoneKeyOctaveCombo, ZoneChannelCombo);
    }

    private void ZoneGeometryHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        zoneGeometryExpanded = !zoneGeometryExpanded;
        ZoneGeometryHeader.Margin = new Thickness(-12, -12, -12, zoneGeometryExpanded ? 10 : -12);
        ZoneGeometryContent.Visibility = zoneGeometryExpanded ? Visibility.Visible : Visibility.Collapsed;
        ZoneGeometryChevron.Text = zoneGeometryExpanded ? "" : "";
    }

    private void KeyboardHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        keyboardExpanded = !keyboardExpanded;
        var visibility = keyboardExpanded ? Visibility.Visible : Visibility.Collapsed;
        VirtualKeyboard.Visibility = visibility;
        KeyboardOctaveRow.Visibility = visibility;
        KeyboardChevron.Text = keyboardExpanded ? "" : "";
    }

    private void OctaveDown_Click(object sender, RoutedEventArgs e) => VirtualKeyboard.ShiftOctave(-1);

    private void OctaveUp_Click(object sender, RoutedEventArgs e) => VirtualKeyboard.ShiftOctave(1);

    private void RefreshKeyboardRange()
    {
        KeyboardRangeText.Text = $"{FormatNoteName(VirtualKeyboard.LowNote)}–{FormatNoteName(VirtualKeyboard.HighNote)}";
        OctaveDownButton.IsEnabled = VirtualKeyboard.CanShiftDown;
        OctaveUpButton.IsEnabled = VirtualKeyboard.CanShiftUp;
    }

    private static string FormatNoteName(int note) => FormatSource(SourceAddress.Note(note).ToString());

    private void CommitZoneUpdate(string zoneId, Action<Zone> apply)
    {
        try
        {
            session.UpdateZone(zoneId, apply);
        }
        catch (Exception ex)
        {
            AppDialog.ShowMessage(OwnerWindow, ex.Message, "Update zone");
        }
        RefreshEditor();
    }

    private void AddZone_Click(object sender, RoutedEventArgs e)
    {
        var project = projectSnapshot ?? session.GetProjectCopy();
        string id = Guid.NewGuid().ToString("N")[..8];
        var zone = new Zone { Id = id, Name = $"Zone {project.Zones.Count + 1}", Rect = new(0.1f, 0.1f, 0.3f, 0.3f), ZIndex = project.Zones.Count };
        try
        {
            session.AddZone(zone);
        }
        catch (Exception ex)
        {
            AppDialog.ShowMessage(OwnerWindow, ex.Message, "Add zone");
            return;
        }
        RefreshEditor();
        SelectZone(id);
    }

    private void RemoveZone_Click(object sender, RoutedEventArgs e)
    {
        if (currentKind != SelectionKind.Zone || currentId is null) return;
        var project = projectSnapshot ?? session.GetProjectCopy();
        var zone = project.FindZone(currentId);
        if (zone is null) return;

        int mappingCount = project.Mappings.Count(m => m.Target.Kind == TargetKind.Zone && m.Target.Id == zone.Id);
        string impact = mappingCount > 0 ? $"\n\nThis will also remove {mappingCount} mapping(s) that target it." : "";
        if (!AppDialog.ShowConfirm(OwnerWindow, $"Delete zone '{zone.Name}'?{impact}", "Delete zone", confirmText: "Delete", destructive: true)) return;

        session.RemoveZone(zone.Id);
        ClearSelection();
        RefreshEditor();
    }

    private void ZoneField_Commit(object sender, RoutedEventArgs e) => CommitZoneFields();

    private void ZoneField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitZoneFields();
        e.Handled = true;
        Keyboard.ClearFocus();
    }

    private void CommitZoneFields()
    {
        if (currentKind != SelectionKind.Zone || currentId is null) return;
        var zone = projectSnapshot?.FindZone(currentId);
        if (zone is null) return;

        float x = ParsePercentOr(ZoneXBox.Text, zone.Rect.X);
        float y = ParsePercentOr(ZoneYBox.Text, zone.Rect.Y);
        float w = ParsePercentOr(ZoneWBox.Text, zone.Rect.W);
        float h = ParsePercentOr(ZoneHBox.Text, zone.Rect.H);
        int cellsW = Math.Max(1, ParseIntOr(ZoneCellsWBox.Text, zone.CellsW));
        int cellsH = Math.Max(1, ParseIntOr(ZoneCellsHBox.Text, zone.CellsH));
        float rotation = ParseOr(ZoneRotationBox.Text, zone.Rotation);
        string name = ZoneNameBox.Text;

        bool changed = name != zone.Name || cellsW != zone.CellsW || cellsH != zone.CellsH
            || !Approximately(x, zone.Rect.X) || !Approximately(y, zone.Rect.Y)
            || !Approximately(w, zone.Rect.W) || !Approximately(h, zone.Rect.H)
            || !Approximately(rotation, zone.Rotation);
        if (!changed) return;

        string id = currentId;
        try
        {
            session.UpdateZone(id, z =>
            {
                z.Name = name;
                z.Rect = new(x, y, w, h);
                z.CellsW = cellsW;
                z.CellsH = cellsH;
                z.Rotation = rotation;
            });
        }
        catch (Exception ex)
        {
            AppDialog.ShowMessage(OwnerWindow, ex.Message, "Update zone");
        }
        RefreshEditor();
    }

    private void ZoneInvertToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (suppressInvertToggles || currentKind != SelectionKind.Zone || currentId is null) return;
        bool invertX = ZoneInvertXToggle.IsChecked == true;
        bool invertY = ZoneInvertYToggle.IsChecked == true;
        var zone = projectSnapshot?.FindZone(currentId);
        if (zone is not null && zone.InvertX == invertX && zone.InvertY == invertY) return;

        string id = currentId;
        try
        {
            session.UpdateZone(id, z => { z.InvertX = invertX; z.InvertY = invertY; });
        }
        catch (Exception ex)
        {
            AppDialog.ShowMessage(OwnerWindow, ex.Message, "Update zone");
        }
        RefreshEditor();
    }

    private void ZoneGroupCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressCombo || currentKind != SelectionKind.Zone || currentId is null) return;
        if (ZoneGroupCombo.SelectedItem is not GroupOption option) return;
        var zone = projectSnapshot?.FindZone(currentId);
        if (zone is not null && zone.GroupId == option.Id) return;

        string id = currentId;
        try
        {
            session.UpdateZone(id, z => z.GroupId = option.Id);
        }
        catch (Exception ex)
        {
            AppDialog.ShowMessage(OwnerWindow, ex.Message, "Update zone");
        }
        RefreshEditor();
    }

    // --- Group panel ---

    private void LoadGroupPanel(Group group, Project project)
    {
        GroupIdentityPanel.Visibility = Visibility.Visible;
        ZoneIdentityPanel.Visibility = Visibility.Collapsed;
        ZoneGeometryBorder.Visibility = Visibility.Collapsed;
        EmptyFieldsPanel.Visibility = Visibility.Collapsed;
        session.SelectedZoneId = null;
        pendingEffectId = SolidEffect.TypeName;
        liveBlendCombo = null;
        liveBlendTarget = null;

        GroupNameBox.Text = group.Name;

        suppressCombo = true;
        SetGroupCombo(GroupParentCombo, project, group.ParentId, excludeId: group.Id);
        suppressCombo = false;

        string groupId = group.Id;
        ParamsTitle.Text = group.Name;
        ParamsTitle.Visibility = Visibility.Visible;
        RebuildParamsPanel(ParamsPanel, group.Params, TargetRef.Group(groupId), (id, raw) => session.SetParam(TargetRef.Group(groupId), id, raw));

        RefreshKeyControls(TargetRef.Group(groupId), TargetKind.Group, project, GroupKeyOctaveCombo, GroupChannelCombo);
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var project = projectSnapshot ?? session.GetProjectCopy();
        string id = Guid.NewGuid().ToString("N")[..8];
        var group = new Group { Id = id, Name = $"Group {project.Groups.Count + 1}" };
        try
        {
            session.AddGroup(group);
        }
        catch (Exception ex)
        {
            AppDialog.ShowMessage(OwnerWindow, ex.Message, "Add group");
            return;
        }
        RefreshEditor();
        SelectGroup(id);
    }

    private void RemoveGroup_Click(object sender, RoutedEventArgs e)
    {
        if (currentKind != SelectionKind.Group || currentId is null) return;
        var project = projectSnapshot ?? session.GetProjectCopy();
        var group = project.FindGroup(currentId);
        if (group is null) return;

        int zoneCount = project.Zones.Count(z => z.GroupId == group.Id);
        int childCount = project.Groups.Count(g => g.ParentId == group.Id);
        int mappingCount = project.Mappings.Count(m => m.Target.Kind == TargetKind.Group && m.Target.Id == group.Id);
        var parts = new List<string>();
        if (zoneCount > 0) parts.Add($"{zoneCount} zone(s) will be ungrouped");
        if (childCount > 0) parts.Add($"{childCount} child group(s) will be moved to top level");
        if (mappingCount > 0) parts.Add($"{mappingCount} mapping(s) will be removed");
        string impact = parts.Count > 0 ? "\n\n" + string.Join("; ", parts) + "." : "";

        if (!AppDialog.ShowConfirm(OwnerWindow, $"Delete group '{group.Name}'?{impact}", "Delete group", confirmText: "Delete", destructive: true)) return;

        session.RemoveGroup(group.Id);
        ClearSelection();
        RefreshEditor();
    }

    private void GroupField_Commit(object sender, RoutedEventArgs e) => CommitGroupFields();

    private void GroupField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitGroupFields();
        e.Handled = true;
        Keyboard.ClearFocus();
    }

    private void CommitGroupFields()
    {
        if (currentKind != SelectionKind.Group || currentId is null) return;
        var group = projectSnapshot?.FindGroup(currentId);
        if (group is null) return;

        string name = GroupNameBox.Text;
        if (name == group.Name) return;

        string id = currentId;
        try
        {
            session.UpdateGroup(id, g => g.Name = name);
        }
        catch (Exception ex)
        {
            AppDialog.ShowMessage(OwnerWindow, ex.Message, "Update group");
        }
        RefreshEditor();
    }

    private void GroupParentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressCombo || currentKind != SelectionKind.Group || currentId is null) return;
        if (GroupParentCombo.SelectedItem is not GroupOption option) return;
        var group = projectSnapshot?.FindGroup(currentId);
        if (group is not null && group.ParentId == option.Id) return;

        string id = currentId;
        try
        {
            session.UpdateGroup(id, g => g.ParentId = option.Id);
        }
        catch (Exception ex)
        {
            AppDialog.ShowMessage(OwnerWindow, ex.Message, "Update group");
        }
        RefreshEditor();
    }

    // --- Project toolbar ---

    private void ProjectNameBox_Commit(object sender, RoutedEventArgs e) => CommitProjectName();

    private void ProjectNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitProjectName();
        e.Handled = true;
        Keyboard.ClearFocus();
    }

    private void CommitProjectName()
    {
        string name = ProjectNameBox.Text;
        if (string.IsNullOrWhiteSpace(name) || name == projectSnapshot?.Name) return;
        session.RenameProject(name);
        RefreshEditor();
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!AppDialog.ShowConfirm(OwnerWindow, "Start a new empty project? Any unsaved changes will be lost.", "New project", confirmText: "Start New", destructive: true)) return;
        session.NewProject("Untitled");
        ClearSelection();
        RefreshEditor();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        ProjectFileDialogs.Open(OwnerWindow, session);
        ClearSelection();
        RefreshEditor();
    }

    /// <summary>Rebuilds the "Open Recent" submenu each time it's opened, since <see cref="RecentFiles.Load"/>
    /// prunes files that no longer exist and picks up anything opened/saved elsewhere since the menu was built.</summary>
    private void OpenRecentMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        OpenRecentMenuItem.Items.Clear();
        var paths = RecentFiles.Load();
        if (paths.Count == 0)
        {
            OpenRecentMenuItem.Items.Add(new MenuItem { Header = "(No recent files)", Style = (Style)FindResource("AppMenuItemStyle"), IsEnabled = false });
            return;
        }
        foreach (string path in paths)
        {
            var item = new MenuItem { Header = System.IO.Path.GetFileName(path), ToolTip = path, Style = (Style)FindResource("AppMenuItemStyle") };
            item.Click += (_, _) => OpenRecentFile_Click(path);
            OpenRecentMenuItem.Items.Add(item);
        }
    }

    private void OpenRecentFile_Click(string path)
    {
        ProjectFileDialogs.OpenPath(OwnerWindow, session, path);
        ClearSelection();
        RefreshEditor();
    }

    private async void ImportSignalRgb_Click(object sender, RoutedEventArgs e)
    {
        ImportSignalRgbMenuItem.IsEnabled = false;
        try
        {
            using var client = new SignalRgbMcpClient();
            var result = await session.ImportSignalRgbLayoutAsync(client);
            ClearSelection();
            RefreshEditor();

            if (result.CanvasWidth > 0 && result.CanvasHeight > 0)
                lastSignalRgbCanvasSize = (result.CanvasWidth, result.CanvasHeight);

            int count = result.Devices.Count;
            string message = $"Imported {count} device{(count == 1 ? "" : "s")} from SignalRGB's current layout.";
            if (result.SkippedMultiComponent.Count > 0)
            {
                message += "\n\nSkipped (SignalRGB doesn't expose an individual position for each of these controllers' several lighting components) - use Manual Import for these:\n"
                    + string.Join('\n', result.SkippedMultiComponent.Select(name => $"• {name}"));
            }
            AppDialog.ShowMessage(OwnerWindow, message, "Import from SignalRGB");
        }
        catch (Exception ex)
        {
            AppDialog.ShowMessage(OwnerWindow, $"Could not import SignalRGB's layout:\n{ex.Message}", "Import from SignalRGB");
        }
        finally
        {
            ImportSignalRgbMenuItem.IsEnabled = true;
        }
    }

    /// <summary>Opens <see cref="ManualSignalRgbImportDialog"/> and adds the entered zone the same way
    /// <see cref="AddZone_Click"/> does - for a device/component the automatic import skipped (see its
    /// "Skipped" list) because SignalRGB can't report an individual position for it. Prefills the canvas fields
    /// with a fresh read from SignalRGB where possible, falling back to the last known size and then to
    /// <see cref="DefaultSignalRgbCanvasSize"/> if SignalRGB can't be reached at all.</summary>
    private async void ManualSignalRgbImport_Click(object sender, RoutedEventArgs e)
    {
        ManualSignalRgbImportMenuItem.IsEnabled = false;
        (float Width, float Height) canvasSize;
        try
        {
            using var client = new SignalRgbMcpClient();
            canvasSize = await client.TryGetCanvasSizeAsync() is { } live
                ? live
                : lastSignalRgbCanvasSize ?? DefaultSignalRgbCanvasSize;
        }
        catch (Exception)
        {
            canvasSize = lastSignalRgbCanvasSize ?? DefaultSignalRgbCanvasSize;
        }
        finally
        {
            ManualSignalRgbImportMenuItem.IsEnabled = true;
        }
        lastSignalRgbCanvasSize = canvasSize;

        var entered = ManualSignalRgbImportDialog.Show(OwnerWindow, canvasSize.Width, canvasSize.Height);
        if (entered is null) return;

        var project = projectSnapshot ?? session.GetProjectCopy();
        string id = Guid.NewGuid().ToString("N")[..8];
        var zone = new Zone { Id = id, Name = entered.Name, Rect = entered.Rect, Rotation = entered.Rotation, ZIndex = project.Zones.Count };
        try
        {
            session.AddZone(zone);
        }
        catch (Exception ex)
        {
            AppDialog.ShowMessage(OwnerWindow, ex.Message, "Manual import");
            return;
        }
        RefreshEditor();
        SelectZone(id);
    }

    /// <summary>Opens the hamburger menu's ContextMenu below the button - a Button's ContextMenu doesn't open on
    /// a plain left click by default (only right-click/Apps-key), so this opens it explicitly instead. Custom
    /// placement right-aligns the menu to the button so it expands leftward, since the button docks at the
    /// window's right edge.</summary>
    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var menu = button.ContextMenu!;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Custom;
        menu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            new[] { new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height), PopupPrimaryAxis.None) };
        menu.IsOpen = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => ProjectFileDialogs.Save(OwnerWindow, session);

    private void SaveAs_Click(object sender, RoutedEventArgs e) => ProjectFileDialogs.SaveAs(OwnerWindow, session);

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        session.Undo();
        RefreshEditor();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        session.Redo();
        RefreshEditor();
    }

    private void SonolumeView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete
            && currentKind == SelectionKind.Zone && e.OriginalSource is not TextBoxBase)
        {
            RemoveZone_Click(sender, e);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.Z) { session.Undo(); RefreshEditor(); e.Handled = true; }
        else if (e.Key == Key.Y) { session.Redo(); RefreshEditor(); e.Handled = true; }
        else if (e.Key == Key.S) { ProjectFileDialogs.Save(OwnerWindow, session); e.Handled = true; }
    }

    // --- Shared helpers ---

    private void RebuildParamsPanel(StackPanel panel, ParamSet values, TargetRef target, Action<ParamId, float> onChange, UIElement? blendRow = null)
    {
        panel.Children.Clear();
        paramSliders.Clear();
        paramCombos.Clear();
        liveColorPicker = null;
        liveEffectCombo = null;
        liveEffectOnChanged = null;

        ParamsActiveCheckBox.Checked -= ParamsActiveCheckBox_Changed;
        ParamsActiveCheckBox.Unchecked -= ParamsActiveCheckBox_Changed;
        ParamsActiveCheckBox.IsChecked = values[ParamId.Active] >= 0.5f;
        paramsActiveOnChange = onChange;
        ParamsActiveCheckBox.Checked += ParamsActiveCheckBox_Changed;
        ParamsActiveCheckBox.Unchecked += ParamsActiveCheckBox_Changed;
        ParamsActiveCheckBox.Visibility = Visibility.Visible;

        var colorGroup = new StackPanel();
        colorGroup.Children.Add(BuildColorWheel(values, onChange));
        colorGroup.Children.Add(new Border
        {
            Style = (Style)panel.FindResource("GroupBorderStyle"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 16),
            Child = BuildColorSliders(values, target, onChange),
        });
        var posXRow = BuildSliderRow(ParamId.PosX, values, target, onChange);
        var posYRow = BuildSliderRow(ParamId.PosY, values, target, onChange);
        var rotationRow = BuildSliderRow(ParamId.EffectRotation, values, target, onChange);
        var speedRow = BuildSliderRow(ParamId.EffectSpeed, values, target, onChange);
        var decayRow = BuildSliderRow(ParamId.EffectDecay, values, target, onChange);
        var effectRow = BuildEffectRow(target, id =>
        {
            colorGroup.Visibility = id == RainbowEffect.TypeName ? Visibility.Collapsed : Visibility.Visible;
            var posVisibility = id == RippleEffect.TypeName ? Visibility.Visible : Visibility.Collapsed;
            posXRow.Visibility = posVisibility;
            posYRow.Visibility = posVisibility;
            rotationRow.Visibility = id == WaveEffect.TypeName ? Visibility.Visible : Visibility.Collapsed;
            speedRow.Visibility = id is SolidEffect.TypeName or FlashEffect.TypeName ? Visibility.Collapsed : Visibility.Visible;
            decayRow.Visibility = id is SolidEffect.TypeName or RainbowEffect.TypeName ? Visibility.Collapsed : Visibility.Visible;
        });

        var rotationRowElement = (FrameworkElement)rotationRow;
        rotationRowElement.Margin = new Thickness(rotationRowElement.Margin.Left, rotationRowElement.Margin.Top, rotationRowElement.Margin.Right, 0);

        var modulationRows = new StackPanel();
        modulationRows.Children.Add(BuildSliderRow(ParamId.EffectIntensity, values, target, onChange));
        modulationRows.Children.Add(speedRow);
        modulationRows.Children.Add(decayRow);
        modulationRows.Children.Add(posXRow);
        modulationRows.Children.Add(posYRow);
        modulationRows.Children.Add(rotationRow);

        var modulationGroup = new Border
        {
            Style = (Style)panel.FindResource("GroupBorderStyle"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 16),
            Child = modulationRows,
        };

        var rows = new List<UIElement> { effectRow };
        if (blendRow is not null) rows.Add(blendRow);
        rows.Add(colorGroup);
        rows.Add(modulationGroup);

        panel.Children.Add(BuildCollapsibleParamGroup(panel, "Effect", rows.ToArray()));
    }

    /// <summary>Picks which effect the target's Trigger/Gate mapping uses. Live effect-type switching is a
    /// Select-mode mapping (see <see cref="MappingMode.Select"/>) fixed to a note in the target's octave (see
    /// <see cref="DefaultMacros.EffectTypeSourceOf"/>, and <see cref="BuildMacroLabel"/>) the moment a zone/group
    /// is created (see <see cref="SonolumeSession.AddZone"/>/<see cref="SonolumeSession.AddGroup"/>) so it's
    /// already live with no setup. Built fresh each time the panel is rebuilt (on selection change), like the
    /// color and blend rows, but kept live afterward by <see cref="RefreshEffectKeyControls"/> - unlike them, the
    /// macro firing changes the target's EffectId (and thus what this row should show) out from under it.</summary>
    private UIElement BuildEffectRow(TargetRef target, Action<string> onEffectIdChanged)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var label = new TextBlock { Text = "Type", Width = 60, VerticalAlignment = VerticalAlignment.Center, Foreground = MutedBrush };

        var proj = projectSnapshot ?? session.GetProjectCopy();
        var macroLabel = BuildMacroLabel(DefaultMacros.EffectTypeSourceOf(proj, target));

        var combo = new ComboBox { ItemsSource = EffectOptions, ItemTemplateSelector = OptionSelector };

        string effectId = proj.Mappings
            .FirstOrDefault(m => m.Target == target && m.Mode is MappingMode.Trigger or MappingMode.Gate)?.EffectId ?? pendingEffectId;
        combo.SelectedItem = Array.Find(EffectOptions, o => o.Id == effectId) ?? EffectOptions[0];

        // Idempotent (no-op once already Gate with this EffectId) - catches mappings that predate Gate becoming
        // the default (the built-in kit's kick/snare/hihat, or anything learned before that change) so hold-to-
        // sustain works the moment this panel is opened, not only after re-picking the effect.
        session.SetEffect(target, effectId);
        onEffectIdChanged(effectId);

        combo.SelectionChanged += (_, _) =>
        {
            if (suppressEffectCombo) return; // a live macro/relabel update, not the user picking an effect
            if (combo.SelectedItem is not EffectOption option) return;
            pendingEffectId = option.Id;
            session.SetEffect(target, option.Id);
            onEffectIdChanged(option.Id);
        };

        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(macroLabel, Dock.Right);
        row.Children.Add(label);
        row.Children.Add(macroLabel);
        row.Children.Add(combo);

        liveEffectCombo = combo;
        liveEffectTarget = target;
        liveEffectOnChanged = onEffectIdChanged;
        return row;
    }

    private Border BuildCollapsibleParamGroup(FrameworkElement owner, string title, params UIElement[] rows)
    {
        var content = new StackPanel { Visibility = effectSectionExpanded ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 16, 0, 16) };
        foreach (var row in rows) content.Children.Add(row);

        var chevron = new TextBlock
        {
            Text = effectSectionExpanded ? "" : "",
            FontFamily = (FontFamily)owner.FindResource("IconFontFamily"),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = MutedBrush,
            Margin = new Thickness(6, 0, 0, 0),
        };
        DockPanel.SetDock(chevron, Dock.Right);

        var icon = new TextBlock
        {
            Text = "\uE945",
            FontFamily = (FontFamily)owner.FindResource("IconFontFamily"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = MutedBrush,
            Margin = new Thickness(0, 0, 6, 0),
        };

        var headerText = new TextBlock { Text = title };
        if (owner.TryFindResource("SectionHeaderTextStyle") is Style headerStyle) headerText.Style = headerStyle;

        var headerRow = new DockPanel();
        headerRow.Children.Add(chevron);
        headerRow.Children.Add(icon);
        headerRow.Children.Add(headerText);

        var header = new Border
        {
            Margin = new Thickness(-12, -12, -12, effectSectionExpanded ? 10 : -12),
            CornerRadius = new CornerRadius(8, 8, 6, 6),
            Cursor = Cursors.Hand,
            Child = headerRow,
        };
        if (owner.TryFindResource("CollapsibleHeaderBorderStyle") is Style headerBorderStyle) header.Style = headerBorderStyle;
        header.MouseLeftButtonUp += (_, _) =>
        {
            effectSectionExpanded = !effectSectionExpanded;
            content.Visibility = effectSectionExpanded ? Visibility.Visible : Visibility.Collapsed;
            header.Margin = new Thickness(-12, -12, -12, effectSectionExpanded ? 10 : -12);
            chevron.Text = effectSectionExpanded ? "" : "";
        };

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(content);

        var border = new Border { Margin = new Thickness(0, 12, 0, 12), Padding = new Thickness(12), Child = stack };
        if (owner.TryFindResource("GroupBorderStyle") is Style borderStyle) border.Style = borderStyle;
        return border;
    }

    private static string DisplayName(ParamId id) => id switch
    {
        ParamId.EffectIntensity => "Intensity",
        ParamId.EffectSpeed => "Speed",
        ParamId.EffectDecay => "Decay",
        ParamId.PaletteIndex => "Palette",
        ParamId.PosX => "X",
        ParamId.PosY => "Y",
        ParamId.EffectRotation => "Rotation",
        _ => id.ToString(),
    };

    private static string? DisplayIcon(ParamId id) => id switch
    {
        ParamId.Hue => "",
        ParamId.Saturation => "",
        ParamId.Brightness => "",
        ParamId.EffectIntensity => "",
        ParamId.EffectSpeed => "",
        ParamId.EffectDecay => "",
        ParamId.PosX or ParamId.PosY => "",
        _ => null,
    };

    /// <summary>EffectSpeed is a discrete note-duration selection (<see cref="NoteDuration"/>), not a continuous
    /// value, so it gets a dropdown of labeled steps instead of the slider every other param uses.</summary>
    private UIElement BuildNoteDurationRow(ParamId id, ParamSet values, TargetRef target, Action<ParamId, float> onChange)
    {
        var info = ParamInfos.Of(id);
        var stack = new StackPanel();

        var headerRow = new DockPanel();
        if (DisplayIcon(id) is string glyph)
        {
            var icon = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)FindResource("IconFontFamily"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = MutedBrush,
                Margin = new Thickness(0, 0, 5, 0),
            };
            DockPanel.SetDock(icon, Dock.Left);
            headerRow.Children.Add(icon);
        }
        var label = new TextBlock { Text = DisplayName(id), VerticalAlignment = VerticalAlignment.Center, Foreground = MutedBrush };
        DockPanel.SetDock(label, Dock.Left);
        headerRow.Children.Add(label);

        var stepOptions = new NoteDurationOption[NoteDuration.Steps.Length];
        for (int i = 0; i < stepOptions.Length; i++) stepOptions[i] = new NoteDurationOption(NoteDuration.Steps[i].Label, NoteDurationRangeText(i));

        var combo = new ComboBox
        {
            Margin = new Thickness(0, 8, 0, 0),
            ItemsSource = stepOptions,
            ItemTemplateSelector = OptionSelector,
            SelectedIndex = NoteDuration.IndexOf(values[id]),
        };

        var resetButton = new Button
        {
            Content = new TextBlock
            {
                Text = "",
                FontFamily = (FontFamily)FindResource("IconFontFamily"),
                FontSize = 11,
                Foreground = MutedBrush,
            },
            Style = (Style)FindResource("PlainIconButtonStyle"),
            Padding = new Thickness(4, 2, 4, 2),
            ToolTip = "Reset to default",
            IsEnabled = !Approximately(values[id], info.Default),
        };
        resetButton.Click += (_, _) =>
        {
            string before = session.ExportProjectJson();
            combo.SelectedIndex = NoteDuration.IndexOf(info.Default);
            session.CommitLiveEdit(before);
        };
        DockPanel.SetDock(resetButton, Dock.Right);
        headerRow.Children.Add(resetButton);

        var proj = projectSnapshot ?? session.GetProjectCopy();
        var macroLabel = BuildMacroLabel(DefaultMacros.ParamSourceOf(proj, target, id));
        DockPanel.SetDock(macroLabel, Dock.Right);
        headerRow.Children.Add(macroLabel);

        combo.SelectionChanged += (_, _) =>
        {
            if (suppressParamsRefresh) return; // RefreshParamsLive echoing a live value, not the user picking one
            if (combo.SelectedIndex < 0) return;
            float value = NoteDuration.ValueFor(combo.SelectedIndex);
            resetButton.IsEnabled = !Approximately(value, info.Default);
            string before = session.ExportProjectJson();
            onChange(id, value);
            session.CommitLiveEdit(before);
        };

        stack.Children.Add(headerRow);
        stack.Children.Add(combo);

        var container = new Border { Padding = new Thickness(4, 8, 4, 8), Margin = new Thickness(0, 0, 0, 4), Child = stack };
        paramCombos[id] = combo;
        return container;
    }

    private UIElement BuildSliderRow(ParamId id, ParamSet values, TargetRef target, Action<ParamId, float> onChange)
    {
        if (id == ParamId.EffectSpeed) return BuildNoteDurationRow(id, values, target, onChange);

        var info = ParamInfos.Of(id);
        var stack = new StackPanel();

        var headerRow = new DockPanel();
        if (DisplayIcon(id) is string glyph)
        {
            var icon = new TextBlock
            {
                Text = glyph,
                FontFamily = (FontFamily)FindResource("IconFontFamily"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = MutedBrush,
                Margin = new Thickness(0, 0, 5, 0),
            };
            DockPanel.SetDock(icon, Dock.Left);
            headerRow.Children.Add(icon);
        }
        var label = new TextBlock { Text = DisplayName(id), VerticalAlignment = VerticalAlignment.Center, Foreground = MutedBrush };
        DockPanel.SetDock(label, Dock.Left);
        headerRow.Children.Add(label);

        var slider = new Slider
        {
            Minimum = info.Min,
            Maximum = info.Max,
            Value = values[id],
            Margin = new Thickness(0, 8, 0, 0),
            IsEnabled = id is not ParamId.PaletteIndex, // no effect reads this yet
        };
        if (id == ParamId.EffectDecay)
        {
            slider.IsSnapToTickEnabled = true;
            slider.TickFrequency = 0.01f;
        }
        else if (id == ParamId.EffectRotation)
        {
            slider.IsSnapToTickEnabled = true;
            slider.TickFrequency = 1f;
        }

        Button? resetButton = null;
        if (id is not ParamId.PaletteIndex) // no effect reads this yet - not worth mapping either
        {
            resetButton = new Button
            {
                Content = new TextBlock
                {
                    Text = "",
                    FontFamily = (FontFamily)FindResource("IconFontFamily"),
                    FontSize = 11,
                    Foreground = MutedBrush,
                },
                Style = (Style)FindResource("PlainIconButtonStyle"),
                Padding = new Thickness(4, 2, 4, 2),
                ToolTip = "Reset to default",
                IsEnabled = !Approximately(values[id], info.Default),
            };
            resetButton.Click += (_, _) =>
            {
                string before = session.ExportProjectJson();
                slider.Value = info.Default;
                session.CommitLiveEdit(before);
            };
            DockPanel.SetDock(resetButton, Dock.Right);
            headerRow.Children.Add(resetButton);

            var proj = projectSnapshot ?? session.GetProjectCopy();
            var macroLabel = BuildMacroLabel(DefaultMacros.ParamSourceOf(proj, target, id));
            DockPanel.SetDock(macroLabel, Dock.Right);
            headerRow.Children.Add(macroLabel);
        }

        var hoverText = new TextBlock { Foreground = MutedBrush, Text = FmtSlider(id, info, values[id]) };
        var hoverBorder = new Border
        {
            Background = Brushes.Black,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Child = hoverText,
        };
        var hoverPopup = new Popup
        {
            PlacementTarget = slider,
            Placement = PlacementMode.Relative,
            IsHitTestVisible = false,
            AllowsTransparency = true,
            Child = hoverBorder,
        };

        // The theme's Slider template (DarkTheme.xaml) names its Track "PART_Track" - the contract Slider's own
        // template-lookup requires - so Track.Thumb gets us the actual dragged thumb without a manual visual-tree walk.
        void UpdateHoverPosition()
        {
            slider.ApplyTemplate();
            if (slider.Template.FindName("PART_Track", slider) is not Track track) return;
            var thumb = track.Thumb;
            var topLeft = thumb.TranslatePoint(new Point(0, 0), slider);
            hoverPopup.HorizontalOffset = topLeft.X + thumb.ActualWidth / 2 - hoverBorder.ActualWidth / 2;
            hoverPopup.VerticalOffset = topLeft.Y - hoverBorder.ActualHeight - 4;
        }

        // The Slider template's Thumb still raises Drag* even though Track consumes DragDelta itself, so this is
        // the only reliable "is the user actively dragging" signal - IsMouseCaptureWithin also fires on a plain click.
        string? dragBeforeSnapshot = null;
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) =>
        {
            dragBeforeSnapshot = session.ExportProjectJson();
            hoverPopup.IsOpen = true;
            UpdateHoverPosition();
        }), true);
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) =>
        {
            hoverPopup.IsOpen = false;
            if (dragBeforeSnapshot is not null) session.CommitLiveEdit(dragBeforeSnapshot);
            dragBeforeSnapshot = null;
        }), true);

        slider.ValueChanged += (_, e) =>
        {
            float value = id switch
            {
                ParamId.EffectDecay => SnapToStep((float)e.NewValue, 0.01f),
                ParamId.EffectRotation => SnapToStep((float)e.NewValue, 1f),
                _ => (float)e.NewValue,
            };
            hoverText.Text = FmtSlider(id, info, value);
            if (resetButton is not null) resetButton.IsEnabled = !Approximately(value, info.Default);
            if (hoverPopup.IsOpen) UpdateHoverPosition();
            if (suppressParamsRefresh) return; // this move came from RefreshOpenPanel echoing a live value, not the user
            onChange(id, value);
        };

        stack.Children.Add(headerRow);
        stack.Children.Add(slider);

        var container = new Border { Padding = new Thickness(4, 8, 4, 8), Margin = new Thickness(0, 0, 0, 4), Child = stack };

        if (id is not ParamId.PaletteIndex) paramSliders[id] = (slider, hoverText); // PaletteIndex's slider is disabled anyway
        return container;
    }

    private static readonly Brush MacroLabelPillBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x38)));

    /// <summary>Shows which note drives a param or select mapping - fixed by <see cref="DefaultMacros"/>, never
    /// user-editable, so this is a plain pill badge where a picker combo used to sit. Fixed width (rather than
    /// sized to its content) so every row's pill lines up regardless of note/channel text length. Null only if the
    /// target hasn't been synced to the convention yet (shouldn't happen once a zone/group has been added).</summary>
    private static Border BuildMacroLabel(SourceAddress? source) => new()
    {
        Child = new TextBlock
        {
            // "ch1" -> "CH1": only this pill's rendering, not SourceAddress.ToString() itself (the Key label and
            // everything else that reads a source's name keeps the lowercase form).
            Text = source?.ToString().Replace(" ch", " CH", StringComparison.Ordinal) ?? "—",
            TextAlignment = TextAlignment.Center,
            Foreground = MutedBrush,
        },
        Background = MacroLabelPillBrush,
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(8, 2, 8, 2),
        Margin = new Thickness(6, 0, 0, 0),
        Width = 72,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Picks the zone's blend mode - the note driving live blend switching is fixed at
    /// <see cref="DefaultMacros.BlendSourceOf"/> (see <see cref="BuildMacroLabel"/>), the same pairing as
    /// <see cref="BuildEffectRow"/>'s "Type" row. Built fresh whenever the zone panel is rebuilt (see
    /// <see cref="LoadZonePanel"/>), then kept live by <see cref="RefreshBlendKeyControls"/>.</summary>
    private UIElement BuildBlendRow(Zone zone, TargetRef target, Action<BlendMode> onChange)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var label = new TextBlock { Text = "Blend", Width = 60, VerticalAlignment = VerticalAlignment.Center, Foreground = MutedBrush };
        var proj = projectSnapshot ?? session.GetProjectCopy();
        var macroLabel = BuildMacroLabel(DefaultMacros.BlendSourceOf(proj, target));
        var combo = new ComboBox
        {
            ItemsSource = BlendOptions,
            ItemTemplateSelector = OptionSelector,
            SelectedItem = Array.Find(BlendOptions, o => o.Mode == zone.Blend),
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (suppressBlendCombo) return; // a live macro update, not the user picking a blend mode
            if (combo.SelectedItem is BlendOption option) onChange(option.Mode);
        };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(macroLabel, Dock.Right);
        row.Children.Add(label);
        row.Children.Add(macroLabel);
        row.Children.Add(combo);

        liveBlendCombo = combo;
        liveBlendTarget = target;
        return row;
    }

    private void ParamsActiveCheckBox_Changed(object sender, RoutedEventArgs e) =>
        paramsActiveOnChange?.Invoke(ParamId.Active, ParamsActiveCheckBox.IsChecked == true ? 1f : 0f);

    private UIElement BuildColorWheel(ParamSet values, Action<ParamId, float> onChange)
    {
        var picker = new ColorPickerControl { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        picker.SetColor(values[ParamId.Hue], values[ParamId.Saturation], values[ParamId.Brightness]);

        var backdrop = new Ellipse { Fill = ColorPickerBackdropBrush };

        var constraint = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        constraint.Children.Add(picker);

        var host = new Grid { Margin = new Thickness(0, 4, 0, 16) };
        host.Children.Add(backdrop);
        host.Children.Add(constraint);

        // The picker's own max footprint (200 square + hue bar) is nearly as wide as the diameter that "fits
        // the container" gives us, so without shrinking it the corners would touch the circle edge. Sizing
        // the constraint box to 70% of the diameter leaves visible padding on all sides.
        host.SizeChanged += (_, e) =>
        {
            double diameter = e.NewSize.Width;
            if (diameter <= 0) return;
            backdrop.Width = diameter;
            backdrop.Height = diameter;
            double inner = diameter * 0.7;
            constraint.Width = inner;
            constraint.Height = inner;
        };

        picker.ColorChanged += (h, s, b) =>
        {
            onChange(ParamId.Hue, h);
            onChange(ParamId.Saturation, s);
            onChange(ParamId.Brightness, b);
        };
        liveColorPicker = picker;

        return host;
    }

    // Sliders (not just the color wheel above) so Hue/Saturation/Brightness are directly draggable and get
    // the same fixed note label as every other continuous param, instead of only being reachable through the
    // wheel. The two stay in sync: RefreshOpenPanel pushes live values into both every tick (see paramSliders,
    // liveColorPicker) whenever a mapped note or another view is what's driving the change.
    private UIElement BuildColorSliders(ParamSet values, TargetRef target, Action<ParamId, float> onChange)
    {
        var stack = new StackPanel();

        var brightnessRow = (FrameworkElement)BuildSliderRow(ParamId.Brightness, values, target, onChange);
        brightnessRow.Margin = new Thickness(brightnessRow.Margin.Left, brightnessRow.Margin.Top, brightnessRow.Margin.Right, 0);

        stack.Children.Add(BuildSliderRow(ParamId.Hue, values, target, onChange));
        stack.Children.Add(BuildSliderRow(ParamId.Saturation, values, target, onChange));
        stack.Children.Add(brightnessRow);
        return stack;
    }

    private static void SetGroupCombo(ComboBox combo, Project project, string? selectedId, string? excludeId)
    {
        var exclude = excludeId is null ? new HashSet<string>() : DescendantsOf(project, excludeId);
        if (excludeId is not null) exclude.Add(excludeId);

        var options = new List<GroupOption> { new(null, "(none)") };
        options.AddRange(project.Groups.Where(g => !exclude.Contains(g.Id)).Select(g => new GroupOption(g.Id, g.Name)));

        combo.ItemsSource = options;
        combo.SelectedItem = options.FirstOrDefault(o => o.Id == selectedId) ?? options[0];
    }

    private static HashSet<string> DescendantsOf(Project project, string groupId)
    {
        var result = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(groupId);
        while (stack.Count > 0)
        {
            string current = stack.Pop();
            foreach (var g in project.Groups)
            {
                if (g.ParentId == current && result.Add(g.Id)) stack.Push(g.Id);
            }
        }
        return result;
    }

    private static void SetIfNotFocused(TextBox box, string value)
    {
        if (!box.IsFocused && box.Text != value) box.Text = value;
    }

    private static float ParseOr(string text, float fallback) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static float ParsePercentOr(string text, float fallback01) =>
        ParseOr(text, fallback01 * 100f) / 100f;

    private static int ParseIntOr(string text, int fallback) => int.TryParse(text, out var v) ? v : fallback;

    private static bool Approximately(float a, float b) => Math.Abs(a - b) < 0.0001f;

    private static float SnapToStep(float value, float step) => MathF.Round(value / step) * step;

    private static string Fmt(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FmtPercent(float v01) => (v01 * 100f).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>Renders a normalized 0..1 boundary as a 7-bit MIDI CC value (0-127) - the resolution most control
    /// surfaces actually send, even though the engine itself carries every source (CC, pitch bend, velocity) at
    /// 14-bit precision internally (see <see cref="MidiEvent"/>). A hint in these units, not raw 0..1 or a percent,
    /// is what matches the number a MIDI controller/DAW shows the user for a plain CC knob.</summary>
    private static int ToMidi127(float v01) => (int)MathF.Round(Math.Clamp(v01, 0f, 1f) * 127f);

    /// <summary>The MIDI-side (0-127) window that lands on option <paramref name="index"/> of <paramref name="count"/>
    /// equal slices - mirrors <see cref="Engine.Engine.SelectEffect"/>/SelectBlend's own slicing so the hint shown
    /// next to each dropdown option matches what actually picks it live.</summary>
    private static string EqualSliceRangeText(int index, int count) =>
        $"{ToMidi127(index / (float)count)}–{ToMidi127((index + 1) / (float)count)}";

    /// <summary>The MIDI-side (0-127) window that lands on step <paramref name="index"/> of
    /// <see cref="NoteDuration.Steps"/> - mirrors <see cref="NoteDuration.IndexOf"/>'s round-to-nearest boundary
    /// (not equal slices, since that method rounds rather than floors) so the hint matches what picks it live.</summary>
    private static string NoteDurationRangeText(int index)
    {
        int lastIndex = NoteDuration.Steps.Length - 1;
        int fromFastest = lastIndex - index;
        float lo = Math.Max(0f, (fromFastest - 0.5f) / lastIndex);
        float hi = Math.Min(1f, (fromFastest + 0.5f) / lastIndex);
        return $"{ToMidi127(lo)}–{ToMidi127(hi)}";
    }

    /// <summary>Dropdown item look for the effect Type/Speed combos: the option's label with its MIDI-side trigger
    /// range in muted text pinned to the right. Only for the open popup list - the closed selection box uses
    /// <see cref="OptionLabelOnlyTemplate"/> instead (picked by <see cref="OptionTemplateSelector"/>), since it has
    /// no room to right-align anything against (its <c>ContentPresenter</c> sizes to content, not the combo's width).</summary>
    private static readonly DataTemplate OptionWithRangeTemplate = BuildOptionTemplate(showRange: true);

    private static readonly DataTemplate OptionLabelOnlyTemplate = BuildOptionTemplate(showRange: false);

    /// <summary>Picks <see cref="OptionWithRangeTemplate"/> for items in the open popup list and
    /// <see cref="OptionLabelOnlyTemplate"/> for the closed combo's own selection box - the only hook that tells
    /// these two rendering sites apart, since WPF has no settable way to give the closed box its own
    /// <see cref="ComboBox.ItemTemplate"/>. In both cases <paramref name="container"/> is a plain
    /// <c>ContentPresenter</c> (DarkTheme.xaml's ComboBoxItem template wraps its content in one, same as the
    /// ComboBox's own closed-box "ContentSite") - not a <see cref="ComboBoxItem"/> itself - so the only way to tell
    /// them apart is whose template that presenter came from: a popup row's presenter belongs to the
    /// <see cref="ComboBoxItem"/> template, the closed box's belongs to the ComboBox template directly.</summary>
    private sealed class OptionTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container) =>
            container is FrameworkElement { TemplatedParent: ComboBoxItem } ? OptionWithRangeTemplate : OptionLabelOnlyTemplate;
    }

    private static readonly DataTemplateSelector OptionSelector = new OptionTemplateSelector();

    private static DataTemplate BuildOptionTemplate(bool showRange)
    {
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding("Label"));
        label.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

        if (!showRange) return new DataTemplate { VisualTree = label };

        var range = new FrameworkElementFactory(typeof(TextBlock));
        range.SetBinding(TextBlock.TextProperty, new Binding("Range"));
        range.SetValue(DockPanel.DockProperty, Dock.Right);
        range.SetValue(TextBlock.ForegroundProperty, MutedBrush);
        range.SetValue(TextBlock.FontSizeProperty, 11.0);
        range.SetValue(TextBlock.MarginProperty, new Thickness(16, 0, 0, 0));
        range.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);

        var panel = new FrameworkElementFactory(typeof(DockPanel));
        panel.AppendChild(range);
        panel.AppendChild(label);

        return new DataTemplate { VisualTree = panel };
    }

    private static string FmtSlider(ParamId id, ParamInfo info, float v) => id switch
    {
        ParamId.EffectDecay => string.Create(CultureInfo.InvariantCulture, $"{v * 1000:0}ms"),
        _ when info.Min == 0f && info.Max == 1f => string.Create(CultureInfo.InvariantCulture, $"{v * 100:0}%"),
        _ => Fmt(v) + info.Unit,
    };

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private sealed class GroupOption
    {
        public GroupOption(string? id, string name)
        {
            Id = id;
            Name = name;
        }

        public string? Id { get; }

        public string Name { get; }
    }

    /// <summary>Zone list row view-model: the zone plus its mapped key(s), formatted the same way as the
    /// canvas's top-right zone label.</summary>
    private sealed class ZoneRow
    {
        public ZoneRow(Zone zone, string? keyLabel)
        {
            Zone = zone;
            KeyLabel = keyLabel;
        }

        public Zone Zone { get; }

        public string Id => Zone.Id;

        public string Name => Zone.Name;

        public string? KeyLabel { get; }
    }

    /// <summary>Group list row view-model: the group plus its mapped key(s), if any.</summary>
    private sealed class GroupRow
    {
        public GroupRow(Group group, string? keyLabel)
        {
            Group = group;
            KeyLabel = keyLabel;
        }

        public Group Group { get; }

        public string Id => Group.Id;

        public string Name => Group.Name;

        public string? KeyLabel { get; }
    }

    /// <summary>Target id -> the key(s)/controller(s) mapped to it, matching <c>PreviewControl</c>'s canvas label.</summary>
    private static Dictionary<string, string> BuildKeyLabels(IEnumerable<Mapping> mappings, TargetKind kind)
    {
        var labels = new Dictionary<string, string>();
        foreach (var m in mappings)
        {
            // Only the note-triggered "Key" mapping belongs here - Set-mode macro mappings (see DefaultMacros)
            // and Select-mode effect-type CC mappings (see BuildEffectRow) aren't part of this label.
            if (!m.Enabled || m.Target.Kind != kind || m.Mode is MappingMode.Set or MappingMode.Select) continue;
            string source = FormatSource(m.Source.ToString());
            if (labels.TryGetValue(m.Target.Id, out var existing))
            {
                if (!existing.Contains(source, StringComparison.Ordinal)) labels[m.Target.Id] = $"{existing}, {source}";
            }
            else
            {
                labels[m.Target.Id] = source;
            }
        }
        return labels;
    }

    // Always trims the trailing " chN"/" ch*" channel suffix SourceAddress.ToString() adds - the Key row's
    // Channel combo is the one place that shows the channel, so it'd be redundant noise here.
    private static string FormatSource(string source)
    {
        int i = source.LastIndexOf(" ch", StringComparison.Ordinal);
        return i < 0 ? source : source[..i];
    }
}
