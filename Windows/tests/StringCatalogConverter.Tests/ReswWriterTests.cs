using System.Xml.Linq;

namespace StringCatalogConverter.Tests;

/// <summary>The .resw XML: Visual Studio's layout, valid XML, stable order.</summary>
[TestClass]
public sealed class ReswWriterTests
{
    [TestMethod]
    public void WritesVisualStudiosLayout()
    {
        var text = ReswWriter.Write([new ResourceString("ShowLogFiles", "Show Log Files", "Mac key: Show Log Files")]);

        Assert.AreEqual("""
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <resheader name="resmimetype">
                <value>text/microsoft-resx</value>
              </resheader>
              <resheader name="version">
                <value>2.0</value>
              </resheader>
              <resheader name="reader">
                <value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
              </resheader>
              <resheader name="writer">
                <value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value>
              </resheader>
              <data name="ShowLogFiles" xml:space="preserve">
                <value>Show Log Files</value>
                <comment>Mac key: Show Log Files</comment>
              </data>
            </root>

            """, text);
    }

    [TestMethod]
    public void DataIsSortedByNameTheSameWayOnEveryMachine()
    {
        ResourceString[] resources = [new("beta", "", ""), new("Gamma", "", ""), new("alpha", "", "")];

        var text = ReswWriter.Write(resources);

        // Ordinal, not culture-sensitive, order: capitals first.
        var names = XDocument.Parse(text).Root!.Elements("data").Select(data => (string?)data.Attribute("name"));
        CollectionAssert.AreEqual(new[] { "Gamma", "alpha", "beta" }, names.ToList());
    }

    [TestMethod]
    public void ValuesSurviveARoundTripThroughAnXmlReader()
    {
        ResourceString[] resources =
        [
            new("Markup", "<b>Tom & \"Jerry\"</b>", "Line one\nLine two"),
            new("Space", " ", "Only a space"),
            new("Russian", "Удалить {0} торрентов?", "Mac key: Remove ^[%lld torrent](inflect: true)?"),
        ];

        var document = XDocument.Parse(ReswWriter.Write(resources), LoadOptions.PreserveWhitespace);

        foreach (var resource in resources)
        {
            var data = document.Root!.Elements("data").Single(data => (string?)data.Attribute("name") == resource.Name);
            Assert.AreEqual(resource.Value, data.Element("value")!.Value);
            Assert.AreEqual(resource.Comment, data.Element("comment")!.Value);
        }
    }

    [TestMethod]
    public void TheTextHasNoByteOrderMarkOrCarriageReturns()
    {
        var text = ReswWriter.Write([new ResourceString("A", "x\ny", "")]);

        Assert.IsTrue(text.StartsWith("<?xml", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains('\r', StringComparison.Ordinal));
        Assert.IsTrue(text.EndsWith("</root>\n", StringComparison.Ordinal));
    }
}
