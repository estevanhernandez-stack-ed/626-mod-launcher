using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using ModManager.Core;

namespace ModManager.App;

/// <summary>
/// Collision prompt for an intake: the dropped files that already exist on disk are listed with a
/// per-file "replace" checkbox (plus a "replace all" master). Apply returns the chosen rel-paths;
/// the old versions are kept by Core and can be reverted. New (non-colliding) files are shown for
/// context but always install — they don't need a decision.
/// </summary>
public sealed partial class UpdateModsDialog : ContentDialog
{
    public sealed class Row
    {
        public string Name { get; set; } = "";
        public string RelPath { get; set; } = "";
        public bool Replace { get; set; } = true;
    }

    private readonly ObservableCollection<Row> _rows = new();

    public UpdateModsDialog(IntakePlan plan)
    {
        InitializeComponent();
        foreach (var c in plan.Collisions) _rows.Add(new Row { Name = c.Name, RelPath = c.RelPath, Replace = true });
        CollisionList.ItemsSource = _rows;
        var adds = plan.ToAdd.Select(a => "+ " + a.RelPath).ToList();
        AddsHeader.Text = adds.Count > 0 ? $"Will also install {adds.Count} new file(s):" : "";
        AddsList.ItemsSource = adds;
        // Set state in code, not XAML: this WinUI 3 build throws XamlParseException assigning a literal
        // bool to the nullable IsChecked. Tri-state master so it can show "some selected" (indeterminate).
        ReplaceAllBox.IsThreeState = true;
        SyncMaster();
    }

    /// <summary>The rel-paths the user chose to replace.</summary>
    public ISet<string> ChosenReplacements() => _rows.Where(r => r.Replace).Select(r => r.RelPath).ToHashSet();

    // User clicked "Replace all": force every row to one definite state (all-off if currently all-on,
    // else all-on), then refresh the row checkboxes. Click fires on user input only, so the master's
    // IsChecked set in SyncMaster never loops back here.
    private void OnReplaceAllClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var turnOn = !_rows.All(r => r.Replace);
        foreach (var r in _rows) r.Replace = turnOn;
        CollisionList.ItemsSource = null; // Row isn't observable; re-assign pushes the new state to the UI
        CollisionList.ItemsSource = _rows;
        SyncMaster();
    }

    // User toggled one row: capture it, then reflect all / none / some on the master.
    private void OnRowClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is Row row) row.Replace = cb.IsChecked == true;
        SyncMaster();
    }

    // Master mirrors the rows: all -> checked, none -> unchecked, some -> indeterminate ("—").
    private void SyncMaster()
    {
        var n = _rows.Count(r => r.Replace);
        ReplaceAllBox.IsChecked = n == 0 ? false : n == _rows.Count ? true : (bool?)null;
    }
}
