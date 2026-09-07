using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Sonolume.Engine.Core;
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
    private bool sliderInitialized;

    private Project? projectSnapshot;
    private SelectionKind currentKind = SelectionKind.None;
    private string? currentId;
    private bool isRefreshingLists;
    private bool suppressCombo;

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
            if (!sliderInitialized && snapshot.Zones.Count > 0)
            {
                sliderInitialized = true;
                DecaySlider.Value = snapshot.Zones[0].DecaySeconds;
            }
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

    private void DecaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DecayText is null) return;
        DecayText.Text = $"{e.NewValue * 1000:0} ms";
        if (sliderInitialized) session.SetDecaySeconds((float)e.NewValue);
    }

    // --- Zone/group editor: bottom + right panels ---

    private void RefreshEditor()
    {
        var project = session.GetProjectCopy();
        projectSnapshot = project;

        UndoButton.IsEnabled = session.CanUndo;
        RedoButton.IsEnabled = session.CanRedo;
        SaveButton.IsEnabled = session.HasUnsavedChanges;

        isRefreshingLists = true;
        ReconcileList(ZoneList, project.Zones, currentKind == SelectionKind.Zone ? currentId : null, z => z.Id);
        ReconcileList(GroupList, project.Groups, currentKind == SelectionKind.Group ? currentId : null, g => g.Id);
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

            SetIfNotFocused(ZoneNameBox, zone.Name);
            SetIfNotFocused(ZoneXBox, Fmt(zone.Rect.X));
            SetIfNotFocused(ZoneYBox, Fmt(zone.Rect.Y));
            SetIfNotFocused(ZoneWBox, Fmt(zone.Rect.W));
            SetIfNotFocused(ZoneHBox, Fmt(zone.Rect.H));
            SetIfNotFocused(ZoneCellsWBox, zone.CellsW.ToString());
            SetIfNotFocused(ZoneCellsHBox, zone.CellsH.ToString());

            if (!ZoneGroupCombo.IsDropDownOpen)
            {
                suppressCombo = true;
                SetGroupCombo(ZoneGroupCombo, project, zone.GroupId, excludeId: null);
                suppressCombo = false;
            }
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
        }
    }

    private void ClearSelection()
    {
        currentKind = SelectionKind.None;
        currentId = null;
        session.SelectedZoneId = null;
        ZoneFieldsPanel.Visibility = Visibility.Collapsed;
        GroupFieldsPanel.Visibility = Visibility.Collapsed;
        EmptyFieldsPanel.Visibility = Visibility.Visible;
        ParamsPanel.Children.Clear();
        ParamsTitle.Text = "Params";
        ParamsEmptyText.Visibility = Visibility.Visible;
    }

    // --- Selection ---

    private void ZoneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshingLists) return;
        if (ZoneList.SelectedItem is not Zone zone) return;
        isRefreshingLists = true;
        GroupList.SelectedItem = null;
        isRefreshingLists = false;
        currentKind = SelectionKind.Zone;
        currentId = zone.Id;
        LoadZonePanel(zone, projectSnapshot!);
    }

    private void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isRefreshingLists) return;
        if (GroupList.SelectedItem is not Group group) return;
        isRefreshingLists = true;
        ZoneList.SelectedItem = null;
        isRefreshingLists = false;
        currentKind = SelectionKind.Group;
        currentId = group.Id;
        LoadGroupPanel(group, projectSnapshot!);
    }

    private void SelectZone(string id)
    {
        var zone = (ZoneList.ItemsSource as IEnumerable<Zone>)?.FirstOrDefault(z => z.Id == id);
        isRefreshingLists = true;
        ZoneList.SelectedItem = zone;
        GroupList.SelectedItem = null;
        isRefreshingLists = false;
        if (zone is null) return;
        currentKind = SelectionKind.Zone;
        currentId = id;
        LoadZonePanel(zone, projectSnapshot!);
    }

    private void SelectGroup(string id)
    {
        var group = (GroupList.ItemsSource as IEnumerable<Group>)?.FirstOrDefault(g => g.Id == id);
        isRefreshingLists = true;
        GroupList.SelectedItem = group;
        ZoneList.SelectedItem = null;
        isRefreshingLists = false;
        if (group is null) return;
        currentKind = SelectionKind.Group;
        currentId = id;
        LoadGroupPanel(group, projectSnapshot!);
    }

    // --- Zone panel ---

    private void LoadZonePanel(Zone zone, Project project)
    {
        ZoneFieldsPanel.Visibility = Visibility.Visible;
        GroupFieldsPanel.Visibility = Visibility.Collapsed;
        EmptyFieldsPanel.Visibility = Visibility.Collapsed;
        session.SelectedZoneId = zone.Id;

        ZoneNameBox.Text = zone.Name;
        ZoneXBox.Text = Fmt(zone.Rect.X);
        ZoneYBox.Text = Fmt(zone.Rect.Y);
        ZoneWBox.Text = Fmt(zone.Rect.W);
        ZoneHBox.Text = Fmt(zone.Rect.H);
        ZoneCellsWBox.Text = zone.CellsW.ToString();
        ZoneCellsHBox.Text = zone.CellsH.ToString();

        suppressCombo = true;
        SetGroupCombo(ZoneGroupCombo, project, zone.GroupId, excludeId: null);
        suppressCombo = false;

        string zoneId = zone.Id;
        ParamsTitle.Text = $"Params: {zone.Name}";
        ParamsEmptyText.Visibility = Visibility.Collapsed;
        RebuildParamsPanel(ParamsPanel, zone.Params, (id, raw) => session.SetParam(TargetRef.Zone(zoneId), id, raw));
    }

    private void AddZone_Click(object sender, RoutedEventArgs e)
    {
        var project = projectSnapshot ?? session.GetProjectCopy();
        string id = Guid.NewGuid().ToString("N")[..8];
        var zone = new Zone { Id = id, Name = $"Zone {project.Zones.Count + 1}", Rect = new(0.1f, 0.1f, 0.3f, 0.3f) };
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

        float x = ParseOr(ZoneXBox.Text, zone.Rect.X);
        float y = ParseOr(ZoneYBox.Text, zone.Rect.Y);
        float w = ParseOr(ZoneWBox.Text, zone.Rect.W);
        float h = ParseOr(ZoneHBox.Text, zone.Rect.H);
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

        GroupNameBox.Text = group.Name;

        suppressCombo = true;
        SetGroupCombo(GroupParentCombo, project, group.ParentId, excludeId: group.Id);
        suppressCombo = false;

        string groupId = group.Id;
        ParamsTitle.Text = $"Params: {group.Name}";
        ParamsEmptyText.Visibility = Visibility.Collapsed;
        RebuildParamsPanel(ParamsPanel, group.Params, (id, raw) => session.SetParam(TargetRef.Group(groupId), id, raw));
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

    private void RebuildParamsPanel(StackPanel panel, ParamSet values, Action<ParamId, float> onChange)
    {
        panel.Children.Clear();
        foreach (var info in ParamInfos.All)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var label = new TextBlock { Text = info.Id.ToString(), Width = 100, VerticalAlignment = VerticalAlignment.Center };
            var valueText = new TextBlock { Width = 50, VerticalAlignment = VerticalAlignment.Center, Foreground = MutedBrush, Text = Fmt(values[info.Id]) };
            var slider = new Slider
            {
                Minimum = info.Min,
                Maximum = info.Max,
                Value = values[info.Id],
                VerticalAlignment = VerticalAlignment.Center,
            };
            slider.ValueChanged += (_, e) =>
            {
                valueText.Text = Fmt((float)e.NewValue);
                onChange(info.Id, (float)e.NewValue);
            };

            DockPanel.SetDock(label, Dock.Left);
            DockPanel.SetDock(valueText, Dock.Right);
            row.Children.Add(label);
            row.Children.Add(valueText);
            row.Children.Add(slider);
            panel.Children.Add(row);
        }
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

    private static int ParseIntOr(string text, int fallback) => int.TryParse(text, out var v) ? v : fallback;

    private static bool Approximately(float a, float b) => Math.Abs(a - b) < 0.0001f;

    private static string Fmt(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

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
}
