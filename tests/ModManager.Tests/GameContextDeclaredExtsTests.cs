using ModManager.Core;
using ModManager.Core.Discovery;
using ModManager.Core.Manifest;

namespace ModManager.Tests;

// A5. GameContext carries THREE similar extension values and they are easy to confuse:
//
//   ctx.Game.FileExtensions — the RAW stored registration value. May be a stale preset snapshot.
//   ctx.DeclaredExts        — the RESOLVED value (RegistrationRefresh applied). What ctx.FileRe,
//                             and therefore Scanner.ModKeyFor, is built from.
//   ctx.Exts                — DeclaredExts with the regex build's empty->["pak"] substitution
//                             applied. The extensions themselves, never a regex-escaped copy —
//                             the escaping happens at the FileRe interpolation (A7).
//
// Consumers that must agree with the scanner's key formula have to read the middle one. Reading the
// raw value lets a manifest correction reach the scanner but not them; reading ctx.Exts invents a
// "pak" for folder-based engines that genuinely have none. These tests pin the middle value.
//
// This collection mutates the process-global EffectiveManifest static, so it joins the serialized
// ManifestState collection and resets the remote on the way out.
[Collection("ManifestState")]
public class GameContextDeclaredExtsTests : IDisposable
{
    public void Dispose() => EffectiveManifest.SetRemote(null);

    // The live bug, in the exact shape that was measured: Cyberpunk 2077 registered under the
    // "custom" preset, whose default is ["pak"], while the shipped manifest says ["archive"]. The
    // stored value is an untouched preset default, so RegistrationRefresh hands the scanner the
    // manifest's list — and ctx.FileRe agrees with THAT, not with what is stored. Anything keyed off
    // the raw value (the discovery sweep's EngineExtensions) goes blind on exactly the 194 mods A1
    // exists to make visible.
    [Fact]
    public void The_resolved_list_is_what_the_scan_regex_agrees_with()
    {
        EffectiveManifest.SetRemote(null); // the shipped snapshot is the point of this test
        var game = new GameEntry
        {
            Id = "cyberpunk-2077",
            GameName = "Cyberpunk 2077",
            Engine = "custom",
            GameRoot = TestSupport.TempDir("declared-exts-"),
            FileExtensions = new[] { "pak" }, // the custom preset's untouched default
        };

        var ctx = Scanner.GameContext(game);

        Assert.Contains("archive", ctx.DeclaredExts);
        Assert.DoesNotContain("pak", ctx.DeclaredExts);
        Assert.Matches(ctx.FileRe, "Whatever.archive");       // the scanner looks for the RESOLVED extension
        Assert.DoesNotMatch(ctx.FileRe, "Whatever.pak");      // ...and not for the stale stored one

        // And the two genuinely differ — the raw registration is untouched (nothing rewrites games.json).
        Assert.Equal(new[] { "pak" }, game.FileExtensions);
        Assert.Equal(new[] { "pak" }, ctx.Game.FileExtensions);
    }

    // The invariant the two Scanner branch sites rely on. They pick "extension-based engine" vs
    // "catalog-based (fromsoft direct-inject)" by asking whether the list is EMPTY. Switching them
    // from the raw value to the resolved one must not change that answer for a folder-based engine
    // the manifest says nothing about — and ctx.Exts could never be used there, because its
    // empty->["pak"] normalisation would claim a pak engine that does not exist.
    [Fact]
    public void An_engine_with_no_extensions_resolves_to_none_while_Exts_invents_a_pak()
    {
        EffectiveManifest.SetRemote(null);
        var game = new GameEntry
        {
            Id = "elden-ring",
            GameName = "ELDEN RING",
            Engine = "fromsoft",
            GameRoot = TestSupport.TempDir("declared-exts-"),
            FileExtensions = Array.Empty<string>(), // the fromsoft preset's default: folder-based, no extensions
        };

        var ctx = Scanner.GameContext(game);

        Assert.Empty(ctx.DeclaredExts);
        Assert.Empty(ctx.Game.FileExtensions);   // raw and resolved agree today — nothing changes
        Assert.Equal(new[] { "pak" }, ctx.Exts); // ...and this is why ctx.Exts is the wrong value to read
    }

    // The latent half, made real. No shipped manifest entry supplies extensions to an engine whose
    // preset default is empty, so raw and resolved agree on emptiness for every registration today.
    // The feed can change that without an app release — that is the whole point of the feed — and
    // the moment it does, the two Scanner sites branch on the stale answer.
    [Fact]
    public void A_manifest_correction_can_make_the_raw_and_resolved_lists_disagree_about_emptiness()
    {
        EffectiveManifest.SetRemote(new GameManifest
        {
            Games = new[]
            {
                new GameManifestEntry
                {
                    Id = "elden-ring",
                    Name = "ELDEN RING",
                    Engine = "fromsoft",
                    FileExtensions = new[] { "dll" },
                    Provenance = new ManifestProvenance { Sources = new[] { "known-engines" } },
                },
            },
        });
        var game = new GameEntry
        {
            Id = "elden-ring",
            GameName = "ELDEN RING",
            Engine = "fromsoft",
            GameRoot = TestSupport.TempDir("declared-exts-"),
            FileExtensions = Array.Empty<string>(),
        };

        var ctx = Scanner.GameContext(game);

        Assert.Equal(new[] { "dll" }, ctx.DeclaredExts); // the feed reached the scanner
        Assert.Empty(ctx.Game.FileExtensions);           // the registration still says nothing
        Assert.Matches(ctx.FileRe, "mod.dll");           // and the regex followed the feed
    }

    // Escaping is the regex's alone, applied at the FileRe interpolation. So an extension holding a
    // regex metacharacter survives UNESCAPED in both lists, and a plain equality comparison against
    // either one still finds the extension the user actually stored — the substitution is the only
    // thing separating them. ctx.Exts used to carry the escaped copy, and every Intake.ClassifyDrop
    // site paid for it: a real foo.mod+pak classified as skip. (A7.)
    [Fact]
    public void Both_lists_carry_the_extension_unescaped_because_only_the_regex_escapes()
    {
        EffectiveManifest.SetRemote(null);
        var game = new GameEntry
        {
            Id = "hand-rolled",
            GameName = "Hand Rolled",
            Engine = "custom",
            GameRoot = TestSupport.TempDir("declared-exts-"),
            FileExtensions = new[] { "mod+pak" }, // a customisation, so the manifest never touches it
        };

        var ctx = Scanner.GameContext(game);

        Assert.Equal(new[] { "mod+pak" }, ctx.DeclaredExts);
        Assert.Equal(new[] { "mod+pak" }, ctx.Exts);

        // ...and the escaping still happened where it belongs: the regex treats "+" as data.
        Assert.Matches(ctx.FileRe, "Foo.mod+pak");
        Assert.DoesNotMatch(ctx.FileRe, "Foo.modddpak");
    }

    // A hand-written registration carries extensions the way a person types them — Scanner's regex
    // build anticipates exactly this and names ".smpcmod" / ".suit" in its own comment. The regex
    // trims the dot, so the scan side matches; the sweep compares against DiscoverySweep.Extension's
    // output, which is lowercase and dot-LESS. If the two sides normalise differently the sweep goes
    // blind again on a registration the scanner reads perfectly — the same disagreement A5 closes,
    // arriving through a different input. So DeclaredExts is the single normalised list BOTH readers
    // derive from, and this test crosses the real boundary to prove it.
    [Fact]
    public void A_leading_dot_does_not_split_the_sweep_from_the_scan_regex()
    {
        EffectiveManifest.SetRemote(null);
        var game = new GameEntry
        {
            Id = "cyberpunk-2077",
            GameName = "Cyberpunk 2077",
            Engine = "custom",
            GameRoot = TestSupport.TempDir("declared-exts-"),
            FileExtensions = new[] { ".Archive" }, // dotted AND mixed-case, as typed by hand
        };

        var ctx = Scanner.GameContext(game);

        Assert.Matches(ctx.FileRe, "Whatever.archive"); // the scan side matches...

        // ...and so does the sweep side. Only the extension list is wired the way MainViewModel
        // wires it — ModPaths and SkipFolders are fixtures, since the extension list is what is
        // under test here.
        var options = new DiscoverySweepOptions(
            ModPaths: new[] { new DiscoverySweepModPath("archive/pc/mod", PaksRoot: false) },
            EngineExtensions: ctx.DeclaredExts,
            SkipFolders: Array.Empty<string>());

        var found = DiscoverySweep.Classify(
            new[] { new SweptFile("archive/pc/mod/Whatever.archive", 1024) }, options);

        var one = Assert.Single(found);
        Assert.Equal(DiscoveryKind.EngineShaped, one.Kind);

        // ...because both read the one normalised list.
        Assert.Equal(new[] { "archive" }, ctx.DeclaredExts);
    }

    // The stated intent at the regex build ("a list of nothing but dots is the same as none at all")
    // now holds for DeclaredExts too, so the emptiness question the two Scanner branches ask gets the
    // same answer as the regex build's. A list that normalises to nothing declares no extensions.
    [Fact]
    public void A_list_of_nothing_but_dots_declares_no_extensions()
    {
        EffectiveManifest.SetRemote(null);
        var ctx = Scanner.GameContext(new GameEntry
        {
            Id = "dots-only",
            GameName = "Dots Only",
            Engine = "custom",
            GameRoot = TestSupport.TempDir("declared-exts-"),
            FileExtensions = new[] { "." },
        });

        Assert.Empty(ctx.DeclaredExts);
        Assert.Equal(new[] { "pak" }, ctx.Exts); // the regex build's fallback, unchanged
    }
}
