using ModManager.Core;
using ModManager.Core.SaveEditor.FromSoft;

namespace ModManager.App.Services;

/// <summary>
/// App-layer wrapper enforcing the "snapshot before every edit" safety law from the spec
/// (docs/superpowers/specs/2026-05-26-saves-editor-fromsoft-mvp-design.md). Calls
/// <see cref="SaveManager.Backup"/> with auto:false so the snapshot survives KeepBox pruning,
/// then applies the edit via <see cref="EldenRingSave.WriteEdit"/>. Either step's failure
/// surfaces to the caller — the VM is responsible for putting the message into StatusText.
/// </summary>
public sealed class SaveEditorService
{
    /// <summary>List the characters in a save file. Read-only; no snapshot needed.</summary>
    public IReadOnlyList<CharacterSlot> ReadCharacters(string savePath)
        => EldenRingSave.ReadCharacters(savePath);

    /// <summary>Apply an edit. Snapshots FIRST; if that fails, throws before any write.
    /// Returns the snapshot taken (so the UI can surface it / point the user at it).</summary>
    /// <exception cref="InvalidOperationException">Snapshot failed — edit aborted.</exception>
    public SaveSnapshot EditCharacter(
        string saveDir, string snapshotsDir, string savePath,
        int slotIndex, CharacterSlot beforeEdit, CharacterEdit edit)
    {
        // Auto-label so the snapshot is self-explanatory in the Snapshots list.
        var label = $"before-edit: {beforeEdit.Name} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}";

        SaveSnapshot snap;
        try
        {
            // auto:false keeps the snapshot out of KeepBox pruning — these are safety nets.
            snap = SaveManager.Backup(saveDir, snapshotsDir, label, auto: false);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"Couldn't snapshot the save before editing ({e.Message}). Edit was NOT applied.", e);
        }

        try
        {
            EldenRingSave.WriteEdit(savePath, slotIndex, edit);
        }
        catch (Exception e)
        {
            // The snapshot is still on disk — point the user at it.
            throw new InvalidOperationException(
                $"Edit failed ({e.Message}). Your save is still intact, and a pre-edit snapshot is in the Snapshots list.", e);
        }

        return snap;
    }
}
