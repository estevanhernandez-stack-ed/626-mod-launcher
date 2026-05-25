using ModManager.Core;

namespace ModManager.Tests;

// Picking the best readme from a mod's file/zip-entry names. Pure selection — the caller does the
// safe extraction. README.md wins, then README.txt, then the first .md, then the first .txt.
public class ReadmeCaptureTests
{
    [Fact]
    public void Prefers_README_md_over_other_docs()
    {
        var pick = ReadmeCapture.PickReadme(new[] { "changelog.md", "README.md", "notes.txt" });
        Assert.Equal("README.md", pick);
    }

    [Fact]
    public void Prefers_README_txt_over_a_non_readme_md()
    {
        var pick = ReadmeCapture.PickReadme(new[] { "changelog.md", "README.txt" });
        Assert.Equal("README.txt", pick);
    }

    [Fact]
    public void Falls_back_to_first_md_then_first_txt()
    {
        Assert.Equal("guide.md", ReadmeCapture.PickReadme(new[] { "data.pak", "guide.md", "extra.txt" }));
        Assert.Equal("install.txt", ReadmeCapture.PickReadme(new[] { "data.pak", "install.txt" }));
    }

    [Fact]
    public void Is_case_insensitive()
    {
        Assert.Equal("readme.MD", ReadmeCapture.PickReadme(new[] { "other.md", "readme.MD" }));
    }

    [Fact]
    public void Returns_null_when_no_readable_doc_present()
    {
        Assert.Null(ReadmeCapture.PickReadme(new[] { "mod.pak", "textures.ucas", "mod.utoc" }));
        Assert.Null(ReadmeCapture.PickReadme(Array.Empty<string>()));
    }

    [Fact]
    public void Selects_by_basename_inside_a_subfolder()
    {
        var pick = ReadmeCapture.PickReadme(new[] { "MyMod/data.pak", "MyMod/docs/README.md" });
        Assert.Equal("MyMod/docs/README.md", pick);
    }
}
