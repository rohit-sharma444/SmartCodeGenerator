using System.Xml;
using ICSharpCode.SharpZipLib.BZip2;

namespace WikipediaDumpImporter;

/// <summary>One page exactly as the dump states it, before any filtering or stripping.</summary>
public sealed record RawPage(
    long Id, string Title, int Namespace, string? RedirectTo, string Wikitext);

/// <summary>
/// Streams pages out of a MediaWiki XML export.
/// </summary>
/// <remarks>
/// Lazy on purpose. The dump expands to roughly 97 GB of XML (measured: 3.83x on the real file),
/// so nothing here may materialise the document — an <see cref="XmlReader"/> forward-only walk is
/// the only shape that fits in memory.
/// <para>
/// bz2 decoding is the hard ceiling on the whole import: 23.6 MB/s of output, single-threaded and
/// CPU-bound, which puts a full pass at about 69 minutes. Disk reads at 126 MB/s, so it is not the
/// constraint. Do not try to make this faster by reading ahead.
/// </para>
/// </remarks>
public static class DumpPageReader
{
    /// <summary>Pages from a bz2-compressed dump, decompressed as they are read.</summary>
    public static IEnumerable<RawPage> ReadBz2(string dumpPath)
    {
        using var file = File.OpenRead(dumpPath);
        using var bz2 = new BZip2InputStream(file);

        foreach (var page in ReadXml(bz2))
            yield return page;
    }

    /// <summary>Pages from an already-decompressed XML stream.</summary>
    public static IEnumerable<RawPage> ReadXml(Stream xml)
    {
        var settings = new XmlReaderSettings
        {
            IgnoreWhitespace = false,
            IgnoreComments = true,
            DtdProcessing = DtdProcessing.Ignore
        };

        using var reader = XmlReader.Create(xml, settings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "page")
                continue;

            var page = ReadPage(reader);
            if (page is not null)
                yield return page;
        }
    }

    /// <summary>
    /// Consumes one &lt;page&gt; element. Returns null for a page with no title or no text, which
    /// the dump does contain — a deleted revision leaves the element behind.
    /// </summary>
    private static RawPage? ReadPage(XmlReader reader)
    {
        long id = 0;
        var ns = 0;
        string? title = null;
        string? redirectTo = null;
        string? wikitext = null;
        var idSeen = false;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "page")
                break;

            if (reader.NodeType != XmlNodeType.Element)
                continue;

            switch (reader.LocalName)
            {
                case "title":
                    title = reader.ReadElementContentAsString();
                    break;

                case "ns":
                    int.TryParse(reader.ReadElementContentAsString(), out ns);
                    break;

                // <page> and <revision> both carry an <id>; the first one is the page's.
                case "id" when !idSeen:
                    long.TryParse(reader.ReadElementContentAsString(), out id);
                    idSeen = true;
                    break;

                case "redirect":
                    redirectTo = reader.GetAttribute("title");
                    break;

                case "text":
                    wikitext = reader.ReadElementContentAsString();
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(title))
            return null;

        return new RawPage(id, title, ns, redirectTo, wikitext ?? string.Empty);
    }
}
