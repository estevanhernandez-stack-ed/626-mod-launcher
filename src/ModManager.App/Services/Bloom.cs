using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace ModManager.App.Services;

/// <summary>Which theme token an attached bloom emits.</summary>
public enum BloomToken { Accent, Danger }

/// <summary>
/// The glow rule's delivery mechanism (design language: "light means live"). A sanctioned surface
/// gets a composition DropShadow sized to it, hosted on an empty sibling Border BEHIND it, colored
/// from the theme's accent/danger token and shaped by the theme's accent_bloom (blur / alpha) —
/// exactly as the theme defines it. A theme with alpha 0 reads flat, and that is legitimate.
///
/// No toolkit dependency: hand-rolled on ElementCompositionPreview + Compositor.CreateDropShadow.
/// ThemeService.Apply calls OnThemeChanged so live re-theming restyles every bloom in lockstep.
/// </summary>
public static class Bloom
{
    private sealed record Attachment(SpriteVisual Sprite, DropShadow Shadow, BloomToken Token);

    private static readonly List<Attachment> Attachments = new();
    private static Color _accent = Color.FromArgb(255, 0x4d, 0xa3, 0xff);
    private static Color _danger = Color.FromArgb(255, 0xf2, 0x5c, 0x73);
    private static double _blur = 4;
    private static double _alpha; // default bloom is (4, 0) — flat until a theme says otherwise

    /// <summary>
    /// Attach a bloom to <paramref name="caster"/>, rendered on <paramref name="host"/> — an empty
    /// Border sharing the caster's layout slot, declared BEFORE it so the glow sits behind.
    /// </summary>
    public static void Attach(Border host, FrameworkElement caster, BloomToken token)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        var shadow = compositor.CreateDropShadow();
        shadow.Offset = Vector3.Zero;
        var sprite = compositor.CreateSpriteVisual();
        sprite.Shadow = shadow;
        ElementCompositionPreview.SetElementChildVisual(host, sprite);

        var attachment = new Attachment(sprite, shadow, token);
        Attachments.Add(attachment);
        Restyle(attachment);

        // The host fills the shared wrapper, so its layout size IS the caster's footprint.
        host.SizeChanged += (_, e) =>
            sprite.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
        if (host.ActualWidth > 0)
            sprite.Size = new Vector2((float)host.ActualWidth, (float)host.ActualHeight);

        // Glow only while the surface is live: track the caster's visibility, and fade the bloom
        // in over 160ms ease-out (design language: glow transitions) whenever it comes back.
        caster.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
        {
            var visible = caster.Visibility == Visibility.Visible;
            sprite.IsVisible = visible;
            if (visible) FadeIn(compositor, sprite);
        });
        sprite.IsVisible = caster.Visibility == Visibility.Visible;
    }

    /// <summary>Re-color every attached bloom from the freshly applied theme.</summary>
    public static void OnThemeChanged(Color accent, Color danger, double blur, double alpha)
    {
        _accent = accent;
        _danger = danger;
        _blur = blur;
        _alpha = alpha;
        foreach (var attachment in Attachments) Restyle(attachment);
    }

    private static void Restyle(Attachment attachment)
    {
        attachment.Shadow.Color = attachment.Token == BloomToken.Danger ? _danger : _accent;
        attachment.Shadow.BlurRadius = (float)Math.Max(0, _blur);
        attachment.Shadow.Opacity = (float)Math.Clamp(_alpha, 0, 1);
    }

    private static void FadeIn(Compositor compositor, SpriteVisual sprite)
    {
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(
            new Vector2(0f, 0f), new Vector2(0.2f, 1f)));
        fade.Duration = TimeSpan.FromMilliseconds(160);
        sprite.StartAnimation(nameof(sprite.Opacity), fade);
    }
}
