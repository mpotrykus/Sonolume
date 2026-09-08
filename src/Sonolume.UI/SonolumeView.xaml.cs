using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Sonolume.Engine.Core;
using Sonolume.Engine.Effects;
using Sonolume.Engine.Mappings;
using Sonolume.Engine.Model;
using Sonolume.Engine.Output;

namespace Sonolume.UI;

public partial class SonolumeView : UserControl
{
    private static readonly Brush MutedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA3)));

    private enum SelectionKind { None, Zone, Group }

    private readonly SonolumeSession session;
    private readonly DispatcherTimer previewTimer;
    private readonly DispatcherTimer editorTimer;

    private Project? projectSnapshot;
    private SelectionKind currentKind = SelectionKind.None;
    private string? currentId;
    private bool isRefreshingLists;
    private bool suppressCombo;
    private bool suppressInvertToggles;
    private Action<ParamId, float>? paramsActiveOnChange;
    private Point zoneDragStart;
    private bool zoneDragArmed;

    /// <summary>Effect chosen in the picker for the current selection before anything is learned; once a
    /// Trigger/Gate mapping exists its own EffectId is shown (and edited) instead. Reset whenever the selected
    /// zone/group changes.</summary>
    private string pendingEffectId = FlashEffect.TypeName;

    private sealed record EffectOption(string Id, string Label);

    private static readonly EffectOption[] EffectOptions =
    [
        new(SolidEffect.TypeName, "Solid"),
        new(FlashEffect.TypeName, "Flash"),
        new(WaveEffect.TypeName, "Wave"),
        new(PulseEffect.TypeName, "Pulse"),
        new(StrobeEffect.TypeName, "Strobe"),
        new(ChaseEffect.TypeName, "Chase"),
        new(RippleEffect.TypeName, "Ripple"),
        new(SparkleEffect.TypeName, "Sparkle"),
        new(RainbowEffect.TypeName, "Rainbow"),
    ];

    private Window OwnerWindow => Window.GetWindow(this) ?? throw new InvalidOperationException("SonolumeView is not hosted in a Window.");

    public SonolumeView(SonolumeSession session)
    {
        this.session = session;
        InitializeComponent();

        Preview.Editable = true;
        Preview.ZoneClicked += id => session.SelectedZoneId = id;
        Preview.ZoneRectCommitted += CommitZoneRect;

        previewTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        previewTimer.Tick += (_, _) => RefreshPreview();

        editorTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        editorTimer.Tick += (_, _) => RefreshEditor();

        Loaded += (_, _) => { RefreshPreview(); RefreshEditor(); previewTimer.Start(); editorTimer.Start(); };
        Unloaded += (_, _) => { previewTimer.Stop(); editorTimer.Stop(); };
    }

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

    private void RefreshPreview()
    {
        var snapshot = session.Runner.LatestSnapshot;
        Preview.Snapshot = snapshot;
        Preview.SelectedZoneId = session.SelectedZoneId;

        if (snapshot is not null)
        {
            if (!ProjectNameBox.IsFocused) ProjectNameBox.Text = snapshot.ProjectName;
        }

        StatusText.Text = Describe(session.Sink.Status);
        MidiText.Text = $"MIDI: {session.Runner.ProcessedEvents} events" + (session.Runner.DroppedEvents > 0 ? $", {session.Runner.DroppedEvents} dropped" : "");
    }

    private static string Describe(SinkStatus status) => status.State switch
    {
        SinkState.Connected => $"SignalRGB: connected  {status.LastSendMs:0.0} ms  {status.FramesSent} frames" + (status.FramesDropped > 0 ? $"  ({status.FramesDropped} dropped)" : ""),
        SinkState.Offline => "SignalRGB: offline (is SignalRGB running?)",
        SinkState.Error => $"SignalRGB: {status.Message}",
        _ => "SignalRGB: connecting...",
    };

    // --- Zone/group editor: bottom + right panels ---

    private void RefreshEditor()
    {
        session.PollLearn();

        var project = session.GetProjectCopy();
        projectSnapshot = project;

        UndoButton.IsEnabled = session.CanUndo;
        RedoButton.IsEnabled = session.CanRedo;
        SaveButton.IsEnabled = session.HasUnsavedChanges;

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
            SetIfNotFocused(ZoneCellsHBox, zone.CellsH.ToString());

            if (!ZoneGroupCombo.IsDropDownOpen)
            {
                suppressCombo = true;
                SetGroupCombo(ZoneGroupCombo, project, zone.GroupId, excludeId: null);
                suppressCombo = false;
            }

            RefreshKeyControls(TargetRef.Zone(zone.Id), TargetKind.Zone, project, ZoneKeyText, ZoneLearnKeyButton, ZoneClearKeyButton);
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

            RefreshKeyControls(TargetRef.Group(group.Id), TargetKind.Group, project, GroupKeyText, GroupLearnKeyButton, GroupClearKeyButton);
        }
    }

    /// <summary>Keeps a zone/group's "Key" row in sync with <see cref="SonolumeSession.PendingLearnTarget"/> and
    /// its current mapping(s), the same way <see cref="RefreshOpenPanel"/> keeps every other field in sync.</summary>
    private void RefreshKeyControls(TargetRef target, TargetKind kind, Project project, TextBlock keyText, Button learnButton, Button clearButton)
    {
        bool listening = session.PendingLearnTarget == target;
        string? keyLabel = BuildKeyLabels(project.Mappings, kind).GetValueOrDefault(target.Id);
        keyText.Text = listening ? "Listening..." : keyLabel ?? "(none)";
        learnButton.Content = listening ? "Cancel" : "Learn";
        clearButton.IsEnabled = !listening && keyLabel is not null;
    }

    private void LearnKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var target = CurrentTarget();
        if (target is null) return;

        if (session.PendingLearnTarget == target) session.CancelLearn();
        else session.BeginLearn(target, ParamId.EffectIntensity, MappingMode.Gate, pendingEffectId);

        RefreshOpenPanel(projectSnapshot ?? session.GetProjectCopy());
    }

    private void ClearKeyButton_Click(object sender, RoutedEventArgs e)
    {
        var target = CurrentTarget();
        if (target is null) return;

        session.RemoveMappingsForTarget(target);
        RefreshEditor();
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
        ZoneFieldsPanel.Visibility = Visibility.Collapsed;
        GroupFieldsPanel.Visibility = Visibility.Collapsed;
        EmptyFieldsPanel.Visibility = Visibility.Visible;
        ParamsPanel.Children.Clear();
        ParamsTitle.Visibility = Visibility.Collapsed;
        ParamsEmptyText.Visibility = Visibility.Visible;
        ParamsActiveCheckBox.Visibility = Visibility.Collapsed;
        ParamsActiveCheckBox.Checked -= ParamsActiveCheckBox_Changed;
        ParamsActiveCheckBox.Unchecked -= ParamsActiveCheckBox_Changed;
        paramsActiveOnChange = null;
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
            MessageBox.Show(OwnerWindow, ex.Message, "Reorder zones", MessageBoxButton.OK, MessageBoxImage.Error);
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
    }

    // --- Zone panel ---

    private void LoadZonePanel(Zone zone, Project project)
    {
        ZoneFieldsPanel.Visibility = Visibility.Visible;
        GroupFieldsPanel.Visibility = Visibility.Collapsed;
        EmptyFieldsPanel.Visibility = Visibility.Collapsed;
        session.SelectedZoneId = zone.Id;
        pendingEffectId = FlashEffect.TypeName;

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

        suppressCombo = true;
        SetGroupCombo(ZoneGroupCombo, project, zone.GroupId, excludeId: null);
        suppressCombo = false;

        string zoneId = zone.Id;
        ParamsTitle.Text = zone.Name;
        ParamsTitle.Visibility = Visibility.Visible;
        ParamsEmptyText.Visibility = Visibility.Collapsed;
        RebuildParamsPanel(ParamsPanel, zone.Params, TargetRef.Zone(zoneId), (id, raw) => session.SetParam(TargetRef.Zone(zoneId), id, raw));
        ParamsPanel.Children.Add(BuildParamGroup(ParamsPanel, "Layer",
            BuildBlendRow(zone, mode => CommitZoneUpdate(zoneId, z => z.Blend = mode))));
    }

    private void CommitZoneUpdate(string zoneId, Action<Zone> apply)
    {
        try
        {
            session.UpdateZone(zoneId, apply);
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow, ex.Message, "Update zone", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(OwnerWindow, ex.Message, "Add zone", MessageBoxButton.OK, MessageBoxImage.Error);
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
        if (MessageBox.Show(OwnerWindow, $"Delete zone '{zone.Name}'?{impact}", "Delete zone", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

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
        string name = ZoneNameBox.Text;

        bool changed = name != zone.Name || cellsW != zone.CellsW || cellsH != zone.CellsH
            || !Approximately(x, zone.Rect.X) || !Approximately(y, zone.Rect.Y)
            || !Approximately(w, zone.Rect.W) || !Approximately(h, zone.Rect.H);
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
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(OwnerWindow, ex.Message, "Update zone", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(OwnerWindow, ex.Message, "Update zone", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(OwnerWindow, ex.Message, "Update zone", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        RefreshEditor();
    }

    // --- Group panel ---

    private void LoadGroupPanel(Group group, Project project)
    {
        GroupFieldsPanel.Visibility = Visibility.Visible;
        ZoneFieldsPanel.Visibility = Visibility.Collapsed;
        EmptyFieldsPanel.Visibility = Visibility.Collapsed;
        session.SelectedZoneId = null;
        pendingEffectId = FlashEffect.TypeName;

        GroupNameBox.Text = group.Name;

        suppressCombo = true;
        SetGroupCombo(GroupParentCombo, project, group.ParentId, excludeId: group.Id);
        suppressCombo = false;

        string groupId = group.Id;
        ParamsTitle.Text = group.Name;
        ParamsTitle.Visibility = Visibility.Visible;
        ParamsEmptyText.Visibility = Visibility.Collapsed;
        RebuildParamsPanel(ParamsPanel, group.Params, TargetRef.Group(groupId), (id, raw) => session.SetParam(TargetRef.Group(groupId), id, raw));
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
            MessageBox.Show(OwnerWindow, ex.Message, "Add group", MessageBoxButton.OK, MessageBoxImage.Error);
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

        if (MessageBox.Show(OwnerWindow, $"Delete group '{group.Name}'?{impact}", "Delete group", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

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
            MessageBox.Show(OwnerWindow, ex.Message, "Update group", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(OwnerWindow, ex.Message, "Update group", MessageBoxButton.OK, MessageBoxImage.Error);
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
        if (MessageBox.Show(OwnerWindow, "Start a new empty project? Any unsaved changes will be lost.", "New project", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
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
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.Z) { session.Undo(); RefreshEditor(); e.Handled = true; }
        else if (e.Key == Key.Y) { session.Redo(); RefreshEditor(); e.Handled = true; }
        else if (e.Key == Key.S) { ProjectFileDialogs.Save(OwnerWindow, session); e.Handled = true; }
    }

    // --- Shared helpers ---

    private void RebuildParamsPanel(StackPanel panel, ParamSet values, TargetRef target, Action<ParamId, float> onChange)
    {
        panel.Children.Clear();

        ParamsActiveCheckBox.Checked -= ParamsActiveCheckBox_Changed;
        ParamsActiveCheckBox.Unchecked -= ParamsActiveCheckBox_Changed;
        ParamsActiveCheckBox.IsChecked = values[ParamId.Active] >= 0.5f;
        paramsActiveOnChange = onChange;
        ParamsActiveCheckBox.Checked += ParamsActiveCheckBox_Changed;
        ParamsActiveCheckBox.Unchecked += ParamsActiveCheckBox_Changed;
        ParamsActiveCheckBox.Visibility = Visibility.Visible;

        panel.Children.Add(BuildParamGroup(panel, "Color", BuildColorRow(values, onChange)));

        panel.Children.Add(BuildParamGroup(panel, "Effect",
            BuildEffectRow(target),
            BuildSliderRow(ParamId.EffectIntensity, values, onChange),
            BuildSliderRow(ParamId.EffectSpeed, values, onChange),
            BuildSliderRow(ParamId.EffectDecay, values, onChange)));
    }

    /// <summary>Picks which effect the target's Trigger/Gate mapping uses. Built fresh each time the panel is
    /// rebuilt (on selection change), like the color and blend rows - not kept in sync on the editor's periodic
    /// timer, since nothing else changes a mapping's EffectId out from under it.</summary>
    private UIElement BuildEffectRow(TargetRef target)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var label = new TextBlock { Text = "Type", Width = 60, VerticalAlignment = VerticalAlignment.Center, Foreground = MutedBrush };
        var combo = new ComboBox { ItemsSource = EffectOptions, DisplayMemberPath = "Label" };

        string effectId = (projectSnapshot ?? session.GetProjectCopy()).Mappings
            .FirstOrDefault(m => m.Target == target && m.Mode != MappingMode.Set)?.EffectId ?? pendingEffectId;
        combo.SelectedItem = Array.Find(EffectOptions, o => o.Id == effectId) ?? EffectOptions[0];

        // Idempotent (no-op once already Gate with this EffectId) - catches mappings that predate Gate becoming
        // the default (the built-in kit's kick/snare/hihat, or anything learned before that change) so hold-to-
        // sustain works the moment this panel is opened, not only after re-picking the effect.
        session.SetEffect(target, effectId);

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not EffectOption option) return;
            pendingEffectId = option.Id;
            session.SetEffect(target, option.Id);
        };

        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(combo);
        return row;
    }

    private static Border BuildParamGroup(FrameworkElement owner, string title, params UIElement[] rows)
    {
        var stack = new StackPanel();
        var header = new TextBlock { Text = title, Margin = new Thickness(0, 0, 0, 10) };
        if (owner.TryFindResource("SectionHeaderTextStyle") is Style headerStyle) header.Style = headerStyle;
        stack.Children.Add(header);
        foreach (var row in rows) stack.Children.Add(row);

        var border = new Border { Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(12), Child = stack };
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
        _ => id.ToString(),
    };

    private static UIElement BuildSliderRow(ParamId id, ParamSet values, Action<ParamId, float> onChange)
    {
        var info = ParamInfos.Of(id);
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var label = new TextBlock { Text = DisplayName(id), Width = 60, VerticalAlignment = VerticalAlignment.Center, Foreground = MutedBrush };
        var valueText = new TextBlock { Width = 50, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = MutedBrush, Text = FmtSlider(id, info, values[id]) };
        var slider = new Slider
        {
            Minimum = info.Min,
            Maximum = info.Max,
            Value = values[id],
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = id is not ParamId.PaletteIndex, // no effect reads this yet
        };
        if (id == ParamId.EffectDecay)
        {
            slider.IsSnapToTickEnabled = true;
            slider.TickFrequency = 0.01f;
        }
        slider.ValueChanged += (_, e) =>
        {
            float value = id == ParamId.EffectDecay ? SnapToStep((float)e.NewValue, 0.01f) : (float)e.NewValue;
            valueText.Text = FmtSlider(id, info, value);
            onChange(id, value);
        };

        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(valueText, Dock.Right);
        row.Children.Add(label);
        row.Children.Add(valueText);
        row.Children.Add(slider);
        return row;
    }

    private static UIElement BuildBlendRow(Zone zone, Action<BlendMode> onChange)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var label = new TextBlock { Text = "Blend", Width = 60, VerticalAlignment = VerticalAlignment.Center, Foreground = MutedBrush };
        var combo = new ComboBox { ItemsSource = Enum.GetValues<BlendMode>(), SelectedItem = zone.Blend };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is BlendMode mode) onChange(mode);
        };
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(combo);
        return row;
    }

    private void ParamsActiveCheckBox_Changed(object sender, RoutedEventArgs e) =>
        paramsActiveOnChange?.Invoke(ParamId.Active, ParamsActiveCheckBox.IsChecked == true ? 1f : 0f);

    private static UIElement BuildColorRow(ParamSet values, Action<ParamId, float> onChange)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

        var picker = new ColorPickerControl();
        picker.SetColor(values[ParamId.Hue], values[ParamId.Saturation], values[ParamId.Brightness]);
        stack.Children.Add(picker);

        var hsbText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = MutedBrush,
            FontFamily = new FontFamily("Consolas"),
            Text = FormatHsb(values[ParamId.Hue], values[ParamId.Saturation], values[ParamId.Brightness]),
        };
        picker.ColorChanged += (h, s, b) =>
        {
            hsbText.Text = FormatHsb(h, s, b);
            onChange(ParamId.Hue, h);
            onChange(ParamId.Saturation, s);
            onChange(ParamId.Brightness, b);
        };
        stack.Children.Add(hsbText);
        return stack;
    }

    private static string FormatHsb(float h, float s, float b) =>
        string.Create(CultureInfo.InvariantCulture, $"H {h,4:0.00} | S {s,4:0.00} | B {b,4:0.00}");

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
            if (!m.Enabled || m.Target.Kind != kind) continue;
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

    // Trims the "ch*" (any-channel wildcard) suffix SourceAddress.ToString() adds by default, since it's
    // noise for a label where the channel is almost never pinned to something specific.
    private static string FormatSource(string source) =>
        source.EndsWith(" ch*", StringComparison.Ordinal) ? source[..^4] : source;
}
