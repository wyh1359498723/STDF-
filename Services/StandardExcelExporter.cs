using System.IO;
using System.Reflection;
using ClosedXML.Excel;
using StdfAnalyzer.Models;

namespace StdfAnalyzer.Services;

public static class StandardExcelExporter
{
    private const int DataStartRow = 8;
    private const int ColSort = 1;
    private const int ColTest = 2;
    private const int ColUnits = 3;
    private const int ColLimitMin = 4;
    private const int ColLimitMax = 5;
    /// <summary>框线/清除范围右边界（含 F、G 列，与模板预留列一致）。</summary>
    private const int ColBorderEnd = 7;

    public static void WriteToFile(string outputPath, IReadOnlyList<TestInfo> tests, string? device = null)
    {
        using var tpl = OpenTemplateStream();
        using var wb = new XLWorkbook(tpl);
        var ws = wb.Worksheets.First();

        ws.Cell(2, 4).Value = device ?? "";

        int clearRows = Math.Max(500, tests.Count + DataStartRow + 10);
        ws.Range(DataStartRow, 1, clearRows, ColBorderEnd).Clear();

        int row = DataStartRow;
        foreach (var t in tests.OrderBy(x => x.TestNum))
        {
            ws.Cell(row, ColSort).Value = t.TestNum;
            ws.Cell(row, ColTest).Value = t.TestName ?? "";
            ws.Cell(row, ColUnits).Value = t.Units ?? "";

            if (t.LoLimit.HasValue)
                ws.Cell(row, ColLimitMin).Value = t.LoLimit.Value;
            else
                ws.Cell(row, ColLimitMin).Clear();

            if (t.HiLimit.HasValue)
                ws.Cell(row, ColLimitMax).Value = t.HiLimit.Value;
            else
                ws.Cell(row, ColLimitMax).Clear();

            row++;
        }

        int lastDataRow = row - 1;
        if (lastDataRow >= DataStartRow)
        {
            var dataRange = ws.Range(DataStartRow, ColSort, lastDataRow, ColBorderEnd);
            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        wb.SaveAs(outputPath);
    }

    private static Stream OpenTemplateStream()
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("STDFComparisonStandard_Template.xlsx", StringComparison.OrdinalIgnoreCase));
        if (name == null)
            throw new InvalidOperationException("未找到内嵌模板 STDFComparisonStandard_Template.xlsx。");
        var stream = asm.GetManifestResourceStream(name);
        if (stream == null)
            throw new InvalidOperationException("无法打开内嵌模板。");
        return stream;
    }
}
