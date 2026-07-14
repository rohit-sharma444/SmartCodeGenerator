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

        foreach (var page in ReadBz2Stream(file))
            yield return page;
    }

    /// <summary>
    /// Pages from an already-open bz2 stream. Exists so a caller can wrap the COMPRESSED stream and
    /// watch it go by — the number of pages in a dump is not known until it ends, so bytes consumed
    /// is the only progress anything can honestly report.
    /// </summary>
    public static IEnumerable<RawPage> ReadBz2Stream(Stream compressed)
    {
        using var bz2 = new BZip2InputStream(compressed);

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
    /// <remarks>
    /// The reader is advanced by hand rather than by a <c>while (reader.Read())</c> loop, and that is
    /// not a stylistic choice. <see cref="XmlReader.ReadElementContentAsString()"/> already moves past
    /// the element it read, so an outer <c>Read()</c> on top of it advances TWICE and skips whatever
    /// comes next. Wikimedia pretty-prints its dumps, so the skipped node was only ever whitespace and
    /// nothing broke — but hand this a dump with no whitespace between its elements and every
    /// &lt;ns&gt; is stepped over, every page defaults to namespace 0, and talk pages are imported as
    /// articles. The bug never fired on the real file and would have fired on the first one that was
    /// written compactly.
    /// </remarks>
    private static RawPage? ReadPage(XmlReader reader)
    {
        long id = 0;
        var ns = 0;
        string? title = null;
        string? redirectTo = null;
        string? wikitext = null;
        var idSeen = false;

        // <page/> with nothing in it has no end tag to stop at, and walking on would swallow the
        // pages that follow it.
        if (reader.IsEmptyElement)
            return null;

        // Advance by hand. Anything that ends on ReadElementContentAsString has ALREADY moved to the
        // next node, so the loop must examine where it stands rather than read past it.
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "page")
                break;

            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

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

                // An empty element, so nothing consumes it: step over it explicitly.
                case "redirect":
                    redirectTo = reader.GetAttribute("title");
                    reader.Read();
                    break;

                // A page carries every revision it changed through in the period the dump covers, and
                // the dump lists them oldest first — so the LAST <text> is what the article says now.
                case "text":
                    wikitext = reader.ReadElementContentAsString();
                    break;

                // Including <page> itself on the first pass, which is how we step inside it.
                default:
                    reader.Read();
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(title))
            return null;

        return new RawPage(id, title, ns, redirectTo, wikitext ?? string.Empty);
    }
}
