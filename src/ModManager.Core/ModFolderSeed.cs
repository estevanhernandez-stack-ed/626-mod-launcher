using ModManager.Core.Manifest;

namespace ModManager.Core;

/// <summary>
/// Whether to create a game's declared mod folder when it does not exist yet.
///
/// <para>Este, 2026-08-18: "When taking over a game, we're free to make the correct folder if we know
/// what the correct folder is named." A registration whose mod folder is absent otherwise lands on
/// "No mods found here, and the folder this game is set to look in doesn't exist" — the right caution
/// when the path is a guess, and unnecessary when the manifest states it. Creating an empty directory
/// is not destructive; it is what installing the first mod would do anyway, and the alternative tells
/// the user their setup is broken when it merely has not started.</para>
///
/// <para>Scoped to a MANIFEST-sourced path on purpose. A preset default or a hand-typed value that
/// points nowhere may be pointing nowhere because it is WRONG — that is the whole of A15 — and
/// creating it would replace a visible symptom with a silent one.</para>
///
/// <para>Pure decision, no IO: the caller creates. A read path must never write, and this is consulted
/// from one.</para>
/// </summary>
public static class ModFolderSeed
{
    /// <summary>The absolute mod folder to create, or null when nothing should be created.</summary>
    public static string? PathToCreate(GameEntry? game, Func<string, bool>? exists = null)
    {
        if (game is null) return null;
        exists ??= Directory.Exists;

        var curated = EffectiveManifest.Current.Games.FirstOrDefault(g => g.Id == game.Id)?.ModPath;
        if (string.IsNullOrWhiteSpace(curated)) return null;   // not the manifest's claim -> not ours to create

        // The user saying "this is my folder" outranks the manifest, exactly as it does in
        // RegistrationRefresh. Someone who pinned a location and left it empty meant to.
        if (game.UserSet?.Contains(GameEntry.UserSetModLocations, StringComparer.OrdinalIgnoreCase) == true)
            return null;

        if (string.IsNullOrEmpty(game.GameRoot) || !exists(game.GameRoot)) return null;

        var abs = Scanner.LocationAbs(Path.GetFullPath(game.GameRoot), curated!.Replace('/', Path.DirectorySeparatorChar));

        // Never claim to create something that is already there - the caller logs what it did.
        return exists(abs) ? null : abs;
    }
}
