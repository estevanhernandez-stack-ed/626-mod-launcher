using System.Xml;
using System.Xml.Linq;

namespace ModManager.Core.Stores;

/// <summary>What the EA app records about one install, read from its <c>__Installer\installerdata.xml</c>.</summary>
public sealed record EaInstall(IReadOnlyList<string> ContentIds, string? Title, string? GameVersion);

/// <summary>
/// Reads the EA app's per-install manifest (<c>DiPManifest</c>). Pure: it takes the file's text.
/// Any installer can write that file, so DTDs are prohibited and no external resource is resolved.
/// Anything that is not a DiPManifest with at least one content id is "not an EA install" — null,
/// never an exception.
/// </summary>
public static class EaInstallerData
{
    public static EaInstall? Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        XDocument doc;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            doc = XDocument.Load(reader);
        }
        catch { return null; }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "DiPManifest") return null;

        var ids = root.Elements("contentIDs").Elements("contentID")
            .Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList();
        if (ids.Count == 0) return null;

        var titles = root.Elements("gameTitles").Elements("gameTitle").ToList();
        var title = (titles.FirstOrDefault(t => (string?)t.Attribute("locale") == "en_US") ?? titles.FirstOrDefault())
            ?.Value.Trim();
        var version = (string?)root.Element("buildMetaData")?.Element("gameVersion")?.Attribute("version");

        return new EaInstall(ids, string.IsNullOrEmpty(title) ? null : title, string.IsNullOrWhiteSpace(version) ? null : version);
    }
}
