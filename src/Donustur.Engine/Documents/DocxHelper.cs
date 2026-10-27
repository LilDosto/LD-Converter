using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace Donustur.Engine.Documents;

/// <summary>
/// Harici ofis kurulumuna ihtiyaç duymadan standart OpenXML (.docx) dosyaları
/// oluşturmak ve mevcut .docx dosyalarından metin okumak için yerel yardımcı.
/// </summary>
public static class DocxHelper
{
    public static void CreateDocxFromPages(string outputPath, IReadOnlyList<string> pages)
    {
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        // 1. [Content_Types].xml
        WriteZipText(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);

        // 2. _rels/.rels
        WriteZipText(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);

        // 3. word/document.xml
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.AppendLine("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">""");
        sb.AppendLine("  <w:body>");

        for (int p = 0; p < pages.Count; p++)
        {
            string pageText = SanitizeXmlChars(pages[p] ?? string.Empty);
            string[] lines = pageText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            foreach (var line in lines)
            {
                string escaped = SecurityElement.Escape(line) ?? string.Empty;
                sb.AppendLine("    <w:p>");
                if (!string.IsNullOrEmpty(escaped))
                {
                    sb.AppendLine($"      <w:r><w:t xml:space=\"preserve\">{escaped}</w:t></w:r>");
                }
                sb.AppendLine("    </w:p>");
            }

            if (p < pages.Count - 1)
            {
                sb.AppendLine("    <w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>");
            }
        }

        sb.AppendLine("  </w:body>");
        sb.AppendLine("</w:document>");

        WriteZipText(archive, "word/document.xml", sb.ToString());
    }

    public static string ExtractTextFromDocx(string docxPath)
    {
        using var fs = File.OpenRead(docxPath);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Read);
        var docEntry = archive.GetEntry("word/document.xml");
        if (docEntry == null)
        {
            throw new InvalidOperationException("Geçerli bir DOCX belgesi değil (word/document.xml bulunamadı).");
        }

        using var entryStream = docEntry.Open();
        var xdoc = XDocument.Load(entryStream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var sb = new StringBuilder();
        foreach (var para in xdoc.Descendants(w + "p"))
        {
            var paraText = new StringBuilder();
            foreach (var node in para.Descendants())
            {
                if (node.Name == w + "t")
                {
                    paraText.Append(node.Value);
                }
                else if (node.Name == w + "tab")
                {
                    paraText.Append('\t');
                }
                else if (node.Name == w + "br")
                {
                    paraText.AppendLine();
                }
            }
            sb.AppendLine(paraText.ToString());
        }

        return sb.ToString();
    }

    private static void WriteZipText(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content.Trim());
    }

    private static string SanitizeXmlChars(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
