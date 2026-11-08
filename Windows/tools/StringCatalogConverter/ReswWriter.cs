using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace StringCatalogConverter;

/// <summary>
/// Writes a .resw file in Visual Studio's layout: the standard ResX headers, then one data element per string,
/// sorted by name. UTF-8 without a byte order mark and LF line endings, like the rest of the repository.
/// </summary>
internal static class ReswWriter
{
    private const string WinFormsAssembly =
        "System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

    public static string Write(IEnumerable<ResourceString> resources)
    {
        var root = new XElement("root",
            Header("resmimetype", "text/microsoft-resx"),
            Header("version", "2.0"),
            Header("reader", $"System.Resources.ResXResourceReader, {WinFormsAssembly}"),
            Header("writer", $"System.Resources.ResXResourceWriter, {WinFormsAssembly}"),
            resources.OrderBy(resource => resource.Name, StringComparer.Ordinal).Select(Data));
        var document = new XDocument(new XDeclaration("1.0", "utf-8", standalone: null), root);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
        };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static XElement Header(string name, string value) =>
        new("resheader", new XAttribute("name", name), new XElement("value", value));

    /// <summary>
    /// xml:space="preserve" keeps leading and trailing spaces in values. It also stops the writer's indenting, so
    /// the line breaks Visual Studio puts inside each data element are added by hand.
    /// </summary>
    private static XElement Data(ResourceString resource) =>
        new("data",
            new XAttribute("name", resource.Name),
            new XAttribute(XNamespace.Xml + "space", "preserve"),
            new XText("\n    "),
            new XElement("value", resource.Value),
            new XText("\n    "),
            new XElement("comment", resource.Comment),
            new XText("\n  "));
}
