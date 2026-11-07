using ModManager.Core.SaveEditor.FromSoft;

namespace ModManager.Core;

/// <summary>
/// Reads the save folder for <see cref="SaveOffer"/>: whether each declared save exists, how many characters it
/// holds, and who the first one is. Target first, so the common launch (the save already has characters) reads one
/// file. Elden Ring's save is about 28 MB, so callers run this off the UI thread.
///
/// <para>v1 reads FromSoft saves only (<c>ER0000.*</c>, through <see cref="EldenRingSave.ReadCharacters"/>, which is
/// verified on .sl2, .co2 and .err). Any other engine gets no offer.</para>
/// </summary>
public static class SaveOfferProbe
{
    private const string FromSoftBase = "ER0000";

    public static SaveFileFacts Read(string saveDir, string fileName, Func<string, IReadOnlyList<CharacterSlot>> reader)
    {
        var path = Path.Combine(saveDir, fileName);
        if (!File.Exists(path)) return new SaveFileFacts(fileName, false, 0, default);
        DateTime written;
        try { written = File.GetLastWriteTimeUtc(path); }
        catch { return new SaveFileFacts(fileName, true, null, default); }
        try
        {
            var slots = reader(path);
            var first = slots.OrderBy(s => s.SlotIndex).FirstOrDefault();
            return new SaveFileFacts(fileName, true, slots.Count, written, first?.Name, first?.Level);
        }
        catch
        {
            return new SaveFileFacts(fileName, true, null, written);   // unknown: never a source, never a target
        }
    }

    /// <summary>The offer for this launch, or null. Never throws.</summary>
    public static SaveOfferPlan? For(GameEntry game, string? saveDir, LaunchMode mode, LaunchTarget? target,
        Func<string, IReadOnlyList<CharacterSlot>>? reader = null)
    {
        try
        {
            if (!string.Equals(game.Engine, "fromsoft", StringComparison.OrdinalIgnoreCase)) return null;
            if (string.IsNullOrEmpty(saveDir) || !Directory.Exists(saveDir)) return null;
            var profile = GameSaveTypesCatalog.Resolve(game);
            var ext = SaveOffer.TargetExtension(mode, target, ReforgedLaunch.Inspect(target), profile.SaveTypes);
            var read = reader ?? EldenRingSave.ReadCharacters;
            return SaveOffer.Evaluate(profile.SaveTypes, profile.Layout, ext, name => Read(saveDir, name, read),
                FromSoftBase, game.SaveOfferDismissed ?? Array.Empty<string>(), SaveWritePolicy.Refusal(game) is not null);
        }
        catch
        {
            return null;
        }
    }
}
