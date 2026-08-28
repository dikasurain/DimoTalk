using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DimoTalk.Maui.Services.AI;

/// <summary>
/// 自传导出 docx —— DocumentFormat.OpenXml 生成 Word 文档
/// 结构：封面标题 + 生成时间 → 每章（章标题 + 正文段落）
/// </summary>
public static class DocxExporter
{
    /// <summary>
    /// 导出章节为 docx，返回文件完整路径
    /// </summary>
    public static string Export(string docTitle, IReadOnlyList<ChapterInfo> chapters, string? protagonistSummary = null)
    {
        var dir = Path.Combine(FileSystem.AppDataDirectory, "exports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{SanitizeFileName(docTitle)}_{DateTime.Now:yyyyMMdd_HHmm}.docx");

        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new W.Document();
        var body = new W.Body();

        // ── 封面标题 ──
        body.Append(CenterParagraph(docTitle, fontSize: "44", bold: true, spacingBefore: "2400"));
        if (!string.IsNullOrWhiteSpace(protagonistSummary))
            body.Append(CenterParagraph(protagonistSummary, fontSize: "22", bold: false, color: "6E6A61", spacingBefore: "200"));
        body.Append(CenterParagraph($"滴墨讲 · {DateTime.Now:yyyy年MM月dd日}", fontSize: "18", bold: false, color: "A9A496", spacingBefore: "120"));
        body.Append(new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page })));

        // ── 章节 ──
        foreach (var ch in chapters)
        {
            body.Append(ChapterHeading(ch.Title));
            foreach (var para in SplitParagraphs(ch.Content))
                body.Append(BodyParagraph(para));
            // 章节间空一行
            body.Append(new W.Paragraph());
        }

        mainPart.Document!.Append(body);
        mainPart.Document.Save();
        return path;
    }

    private static W.Paragraph ChapterHeading(string title)
    {
        return new W.Paragraph(
            new W.ParagraphProperties(
                new W.KeepNext(),
                new W.SpacingBetweenLines { Before = "480", After = "240" }),
            new W.Run(
                new W.RunProperties(
                    new W.Bold(),
                    new W.FontSize { Val = "36" },
                    new W.Color { Val = "1C1A17" }),
                new W.Text(title) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static W.Paragraph BodyParagraph(string text)
    {
        return new W.Paragraph(
            new W.ParagraphProperties(
                new W.SpacingBetweenLines { Line = "360", LineRule = W.LineSpacingRuleValues.Auto, After = "120" },
                new W.Indentation { FirstLineChars = 200 }),
            new W.Run(
                new W.RunProperties(
                    new W.FontSize { Val = "24" },
                    new W.Color { Val = "26231E" }),
                new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static W.Paragraph CenterParagraph(string text, string fontSize, bool bold, string? color = null, string? spacingBefore = null)
    {
        var props = new W.ParagraphProperties(
            new W.Justification { Val = W.JustificationValues.Center });
        if (spacingBefore is not null)
            props.Append(new W.SpacingBetweenLines { Before = spacingBefore });

        var runProps = new W.RunProperties(new W.FontSize { Val = fontSize });
        if (bold) runProps.Append(new W.Bold());
        if (color is not null) runProps.Append(new W.Color { Val = color });

        return new W.Paragraph(props, new W.Run(runProps, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    /// <summary>按换行切分段落，过滤空行</summary>
    private static List<string> SplitParagraphs(string content)
    {
        return content
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
