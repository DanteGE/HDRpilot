using System.Xml;
using System.Xml.Linq;

namespace HdrPilot.Core.Games.Scanners.Xbox;

/// <summary>Sicheres Laden der XML-Dateien eines Spielpakets (ohne DTD, Kodierung laut BOM bzw. XML-Deklaration).</summary>
internal static class XmlDocuments
{
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
    };

    /// <summary>Lädt die Bytes als XML. Wirft <see cref="InvalidDataException"/> mit deutscher Meldung bei kaputtem Inhalt.</summary>
    public static XElement LoadRoot(byte[] data, string expectedRoot, string label)
    {
        if (data.Length == 0)
        {
            throw new InvalidDataException($"{label} ist leer.");
        }

        XDocument document;
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var reader = XmlReader.Create(stream, Settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"{label} ist kein gültiges XML (abgeschnitten, falsche Kodierung oder kein Text): {ex.Message}", ex);
        }
        catch (System.Text.DecoderFallbackException ex)
        {
            throw new InvalidDataException($"{label} hat eine falsche Zeichenkodierung: {ex.Message}", ex);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != expectedRoot)
        {
            throw new InvalidDataException($"{label}: Wurzelelement <{expectedRoot}> fehlt.");
        }

        return root;
    }

    /// <summary>Erstes Kindelement mit diesem lokalen Namen, unabhängig vom XML-Namensraum.</summary>
    public static XElement? Child(this XElement element, string localName) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName == localName);

    public static IEnumerable<XElement> Children(this XElement element, string localName) =>
        element.Elements().Where(e => e.Name.LocalName == localName);

    /// <summary>Attributwert ohne Namensraum, getrimmt; leer wird zu null.</summary>
    public static string? Attr(this XElement? element, string name)
    {
        var value = element?.Attribute(name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>Textinhalt, getrimmt; leer wird zu null.</summary>
    public static string? Text(this XElement? element)
    {
        var value = element?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
