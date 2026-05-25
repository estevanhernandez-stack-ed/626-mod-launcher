using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using ModManager.Core;

namespace ModManager.App;

/// <summary>
/// Renders a mod readme (markdown) into native WinUI controls — render-only, no HTML/script, no
/// remote images. Mod READMEs are attacker-controlled, so this builds only text Runs and (for
/// http(s) URLs only, via <see cref="SafeUrl"/>) real Hyperlinks; a non-safe link degrades to plain
/// text so a readme can't smuggle a javascript:/file: scheme. Built in code off the pure
/// <see cref="Markdown"/> parser — never raw markup.
/// </summary>
public static class ReadmeRenderer
{
    private static readonly FontFamily Mono = new("Consolas");

    public static FrameworkElement Build(string markdown)
    {
        var rtb = new RichTextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, MaxWidth = 540 };
        var number = 0;
        foreach (var block in Markdown.Parse(markdown))
        {
            if (block.Kind != MdBlockKind.NumberItem) number = 0;
            rtb.Blocks.Add(block.Kind switch
            {
                MdBlockKind.Heading => Heading(block),
                MdBlockKind.Code => Code(block.Text),
                MdBlockKind.BulletItem => ListItem("•  ", block.Spans),
                MdBlockKind.NumberItem => ListItem($"{++number}.  ", block.Spans),
                _ => Para(block.Spans),
            });
        }

        return new ScrollViewer
        {
            Content = rtb,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 460,
            MinWidth = 460,
            MaxWidth = 560,
        };
    }

    private static Paragraph Heading(MdBlock block)
    {
        var p = new Paragraph { Margin = new Thickness(0, 8, 0, 4) };
        double size = block.Level switch { 1 => 20, 2 => 17, 3 => 15, _ => 14 };
        foreach (var inline in Inlines(block.Spans))
        {
            if (inline is Run r) { r.FontWeight = FontWeights.SemiBold; r.FontSize = size; }
            p.Inlines.Add(inline);
        }
        return p;
    }

    private static Paragraph Code(string text)
    {
        var p = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
        p.Inlines.Add(new Run { Text = text, FontFamily = Mono, FontSize = 13 });
        return p;
    }

    private static Paragraph ListItem(string bullet, IReadOnlyList<MdSpan> spans)
    {
        var p = new Paragraph { Margin = new Thickness(12, 2, 0, 2), TextIndent = -12 };
        p.Inlines.Add(new Run { Text = bullet });
        foreach (var inline in Inlines(spans)) p.Inlines.Add(inline);
        return p;
    }

    private static Paragraph Para(IReadOnlyList<MdSpan> spans)
    {
        var p = new Paragraph { Margin = new Thickness(0, 4, 0, 4) };
        foreach (var inline in Inlines(spans)) p.Inlines.Add(inline);
        return p;
    }

    private static IEnumerable<Inline> Inlines(IReadOnlyList<MdSpan> spans)
    {
        foreach (var s in spans)
        {
            yield return s.Kind switch
            {
                MdSpanKind.Bold => new Run { Text = s.Text, FontWeight = FontWeights.SemiBold },
                MdSpanKind.Italic => new Run { Text = s.Text, FontStyle = Windows.UI.Text.FontStyle.Italic },
                MdSpanKind.Code => new Run { Text = s.Text, FontFamily = Mono },
                MdSpanKind.Link => Link(s),
                _ => new Run { Text = s.Text },
            };
        }
    }

    // http(s) -> a real Hyperlink the OS opens; anything else is shown as plain text so an
    // attacker-controlled readme link can't smuggle a non-web scheme past the user.
    private static Inline Link(MdSpan s)
    {
        if (SafeUrl.IsHttpUrl(s.Url))
        {
            var link = new Hyperlink { NavigateUri = new Uri(s.Url!) };
            link.Inlines.Add(new Run { Text = s.Text });
            return link;
        }
        return new Run { Text = s.Text };
    }
}
