namespace ModManager.Core;

/// <summary>
/// What a confirm says it is about to replace.
///
/// <para>This is in Core, and pure, for one reason: <b>a confirmation that misreports what it will
/// destroy is worse than one that says nothing.</b> "Everything in the save folder is replaced —
/// 2 worlds, 25.4 MB" is the sentence a person weighs before pressing a button that cannot be
/// un-pressed without knowing about <c>before-restore</c>. Built inline in a click handler it would
/// be untestable and would drift the first time the panel changed.</para>
///
/// <para>Takes counts rather than a path so it stays free of I/O — the caller has already walked the
/// folder to render the list it is standing in front of.</para>
/// </summary>
public static class SaveFolderSummary
{
    /// <summary>
    /// Describe a save folder's contents in the terms that game arranges them in.
    ///
    /// <para>Worlds win when there are any: a Palworld player thinks in worlds, and "74 save files"
    /// is technically true and useless. A worlds-shaped folder also holds loose files —
    /// <c>GlobalPalStorage.sav</c> and the like — which are real but not what anyone is deciding
    /// about.</para>
    /// </summary>
    public static string Describe(int worlds, int files, long bytes)
    {
        if (worlds > 0) return $"{worlds} world{S(worlds)}, {Human(bytes)}";
        if (files > 0) return $"{files} save file{S(files)}, {Human(bytes)}";

        // Never "0 files, 0 B". A confirm for an empty folder should read like a sentence, and it is
        // also a hint that the folder may be the wrong one.
        return "an empty save folder";
    }

    private static string S(int n) => n == 1 ? "" : "s";

    /// <summary>Byte size for human eyes. Lives here so the App and any confirm copy share ONE
    /// formatter — the saves panel briefly had two, and they disagreed above a gigabyte.</summary>
    public static string Human(long bytes) => bytes switch
    {
        < 0 => "0 B",
        < 1024 => $"{bytes} B",
        < 1024L * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.#} GB",
    };
}
