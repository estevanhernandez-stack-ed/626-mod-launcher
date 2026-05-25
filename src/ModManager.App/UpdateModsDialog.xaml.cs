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
    }

    /// <summary>The rel-paths the user chose to replace.</summary>
    public ISet<string> ChosenReplacements() => _rows.Where(r => r.Replace).Select(r => r.RelPath).ToHashSet();

    // "Replace all" master: flip every row, then re-bind to refresh the per-row checkboxes (Row
    // isn't observable, so the null/re-assign cycle is what pushes the new state to the UI).
    private void OnReplaceAll(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var on = ReplaceAllBox.IsChecked == true;
        foreach (var r in _rows) r.Replace = on;
        CollisionList.ItemsSource = null;
        CollisionList.ItemsSource = _rows;
    }
}
