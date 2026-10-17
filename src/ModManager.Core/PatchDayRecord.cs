using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModManager.Core;

/// <summary>What the user chose to do about a patch: nothing yet, play vanilla, or stay modded.</summary>
[JsonConverter(typeof(PatchDayChoiceConverter))]
public enum PatchDayChoice { None, Vanilla, Modded }

// Written as "none" | "vanilla" | "modded". The registry's serializer carries no naming policy of its
// own, so the enum owns it: camelCase on disk is the rule. Reads are tolerant: RegistryStore.Load turns
// any parse failure into an empty registry, so a newer build's unknown choice (or a non-string token)
// must read as None rather than throw and drop every game.
public sealed class PatchDayChoiceConverter : JsonConverter<PatchDayChoice>
{
    public override PatchDayChoice Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse<PatchDayChoice>(reader.GetString(), ignoreCase: true, out var choice)
            && Enum.IsDefined(choice))
            return choice;
        reader.TrySkip();
        return PatchDayChoice.None;
    }

    public override void Write(Utf8JsonWriter writer, PatchDayChoice value, JsonSerializerOptions options) =>
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
}

/// <summary>
/// A game's patch in play, present on <see cref="GameEntry.PatchDay"/> only while one is. Absent means
/// Quiet. The baseline ("the build the mods were last checked against") stays
/// <see cref="GameEntry.LastKnownSteamBuildId"/>; this record carries the NEW build and what was done
/// about it.
///
/// <para>Immutable, so the shallow <see cref="GameEntry.CloneShallow"/> can share it safely; change it
/// with <c>with</c>.</para>
/// </summary>
public sealed record PatchDayRecord
{
    /// <summary>The new build detected.</summary>
    public string BuildId { get; init; } = "";
    public DateTime DetectedUtc { get; init; }
    /// <summary>The automatic snapshot taken on detection; null if it could not be taken.</summary>
    public string? RestorePointId { get; init; }
    /// <summary>Why the snapshot could not be taken, when it failed.</summary>
    public string? RestorePointError { get; init; }
    public PatchDayChoice Choice { get; init; } = PatchDayChoice.None;
    /// <summary>The user asked not to be nagged until the build changes again.</summary>
    public bool SnoozedUntilChange { get; init; }
}
