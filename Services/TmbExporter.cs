using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using StdfAnalyzer.Models;

namespace StdfAnalyzer.Services;

/// <summary>
/// 导出 TMB 版式晶圆图：图内 bin 宽度由该片所选 Bin（HBin/SBin）的最大值决定。
/// </summary>
public static class TmbExporter
{
    private const int PaddingDataRows = 5;
    private const string Separator71 = "=======================================================================";

    public static void WriteWaferTmb(StreamWriter writer, string device, string lotId, string waferId, List<PartData> waferParts, bool useHardBin)
    {
        if (waferParts.Count == 0) return;

        var coordMap = new Dictionary<(short x, short y), PartData>();
        foreach (var p in waferParts)
            coordMap[(p.XCoord, p.YCoord)] = p;

        int minX = waferParts.Min(p => p.XCoord);
        int maxX = waferParts.Max(p => p.XCoord);
        int minY = waferParts.Min(p => p.YCoord);
        int maxY = waferParts.Max(p => p.YCoord);
        int gridW = maxX - minX + 1;
        int gridH = maxY - minY + 1;

        int maxBinMap = 0;
        foreach (var p in waferParts)
        {
            int b = SelectBin(p, useHardBin);
            if (b > maxBinMap)
                maxBinMap = b;
        }

        var (mapBinDigits, mapCellWidth) = GetMapBinCellSpec(maxBinMap);
        int rowBodyWidth = gridW * mapCellWidth;
        int maxRowLabel = gridH + PaddingDataRows;
        int rowLabelChars = Math.Max(3, maxRowLabel.ToString(CultureInfo.InvariantCulture).Length);

        writer.WriteLine(BuildRulerLine(gridW));
        writer.WriteLine(BuildPlusMinusLine(gridW));

        var rowSb = new StringBuilder(rowLabelChars + 1 + rowBodyWidth);
        for (int fileRow = 1; fileRow <= gridH; fileRow++)
        {
            int dr = fileRow - 1;
            int yWorld = maxY - dr;
            rowSb.Clear();
            AppendRowLabel(rowSb, fileRow, rowLabelChars);
            rowSb.Append('|');
            for (int dc = 0; dc < gridW; dc++)
            {
                int xWorld = minX + dc;
                if (coordMap.TryGetValue(((short)xWorld, (short)yWorld), out var part))
                    rowSb.Append(FormatMapBinCell(SelectBin(part, useHardBin), mapBinDigits, mapCellWidth));
                else
                    rowSb.Append(new string(' ', mapCellWidth));
            }

            if (rowSb.Length != rowLabelChars + 1 + rowBodyWidth)
                throw new InvalidOperationException($"TMB 行长度异常: 期望 {rowLabelChars + 1 + rowBodyWidth}, 实际 {rowSb.Length}");
            writer.WriteLine(rowSb.ToString());
        }

        for (int fileRow = gridH + 1; fileRow <= gridH + PaddingDataRows; fileRow++)
        {
            rowSb.Clear();
            AppendRowLabel(rowSb, fileRow, rowLabelChars);
            rowSb.Append('|');
            rowSb.Append(new string(' ', rowBodyWidth));
            writer.WriteLine(rowSb.ToString());
        }

        writer.WriteLine();
        writer.WriteLine();

        int pass = waferParts.Count(p => p.Pass);
        int fail = waferParts.Count - pass;
        int total = waferParts.Count;
        double yield = total > 0 ? (double)pass / total * 100 : 0;
        string slot = TryExtractSlotNo(waferId);
        string waferDisplay = string.IsNullOrEmpty(waferId) ? "N/A" : waferId;
        string waferId_tmb = $"{lotId}-{slot}";
        writer.WriteLine("============ Wafer Information () ===========");
        writer.WriteLine($"  Device: {device}");
        writer.WriteLine($"  Lot NO: {lotId}");
        writer.WriteLine($"  Slot No: {slot}");
        writer.WriteLine($"  Wafer ID: {waferId_tmb}");
        writer.WriteLine($"  Total test die: {total}");
        writer.WriteLine($"  Pass Die: {pass}");
        writer.WriteLine($"  Fail Die: {fail}");
        writer.WriteLine($"  Yield: {yield:F2}%");
        writer.WriteLine();
        writer.WriteLine();

        int maxBin = maxBinMap;

        var binCounts = new int[maxBin + 1];
        foreach (var p in waferParts)
        {
            int b = SelectBin(p, useHardBin);
            if (b >= 0 && b <= maxBin)
                binCounts[b]++;
        }

        //string binKindLabel = useHardBin ? "HBin" : "SBin";
        writer.WriteLine($"             BIN (0~{maxBin}) Data Deatil Summary");
        writer.WriteLine(Separator71);

        for (int start = 0; start <= maxBin; start += 8)
        {
            int cells = Math.Min(8, maxBin - start + 1);
            writer.WriteLine(BuildBinHeaderLine(start, cells));
            writer.WriteLine(BuildBinCountLine(binCounts, start, cells, total));
            writer.WriteLine(BuildBinPercentLine(binCounts, start, cells, total));
            writer.WriteLine(Separator71);
        }
    }

    private static void AppendRowLabel(StringBuilder sb, int row, int width)
    {
        string s = row.ToString(CultureInfo.InvariantCulture);
        if (s.Length < width)
            sb.Append('0', width - s.Length);
        sb.Append(s);
    }

    private static string BuildRulerLine(int gridW)
    {
        var nums = Enumerable.Range(1, gridW)
            .Select(n => n <= 99 ? n.ToString("D2", CultureInfo.InvariantCulture) : n.ToString(CultureInfo.InvariantCulture));
        return "    " + string.Join(" ", nums);
    }

    private static string BuildPlusMinusLine(int gridW) =>
        "  " + string.Concat(Enumerable.Repeat("++--", gridW)) + "+";

    /// <summary>最大 bin ≤99：2 位数字 + 前导空格共 3 格；≥100 且 ≤999：3 位数字 + 前导空格共 4 格；更大则位数与 max 一致，格宽 = 位数+1。</summary>
    private static (int digits, int cellWidth) GetMapBinCellSpec(int maxBin)
    {
        if (maxBin <= 99)
            return (2, 3);
        if (maxBin <= 999)
            return (3, 4);
        int d = (int)Math.Floor(Math.Log10(maxBin)) + 1;
        return (d, d + 1);
    }

    private static ushort SelectBin(PartData p, bool useHardBin) => useHardBin ? p.HardBin : p.SoftBin;

    private static string FormatMapBinCell(ushort sbin, int digits, int cellWidth)
    {
        int cap = (int)Math.Pow(10, digits) - 1;
        int v = Math.Min((int)sbin, cap);
        return v.ToString($"D{digits}", CultureInfo.InvariantCulture).PadLeft(cellWidth);
    }

    private static string BuildBinHeaderLine(int startBin, int cellCount)
    {
        var sb = new StringBuilder(cellCount * 10);
        for (int i = 0; i < cellCount; i++)
        {
            int b = startBin + i;
            if (b < 10)
                sb.Append("Bin ").Append(b).Append("  | ");
            else
                sb.Append("Bin ").Append(b).Append(" | ");
        }

        return sb.ToString();
    }

    private static string BuildBinCountLine(int[] binCounts, int startBin, int cellCount, int total)
    {
        int countDigits = Math.Max(5, total.ToString(CultureInfo.InvariantCulture).Length);
        var sb = new StringBuilder(cellCount * (countDigits + 4));
        for (int i = 0; i < cellCount; i++)
        {
            int b = startBin + i;
            int c = binCounts[b];
            sb.Append(c.ToString("D" + countDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
            sb.Append("  | ");
        }

        return sb.ToString();
    }

    private static string BuildBinPercentLine(int[] binCounts, int startBin, int cellCount, int total)
    {
        var sb = new StringBuilder(cellCount * 10);
        for (int i = 0; i < cellCount; i++)
        {
            int b = startBin + i;
            double pct = total > 0 ? (double)binCounts[b] / total * 100 : 0;
            sb.Append(FormatTmbPercentCell(pct));
            sb.Append(" | ");
        }

        return sb.ToString();
    }

    private static string FormatTmbPercentCell(double pct)
    {
        if (pct >= 99.995)
            return "100.0%";

        double r = Math.Round(pct, 2, MidpointRounding.AwayFromZero);
        int whole = (int)Math.Floor(r + 1e-9);
        int frac = (int)Math.Round((r - whole) * 100);
        if (frac >= 100)
        {
            whole++;
            frac = 0;
        }

        whole = Math.Clamp(whole, 0, 99);
        frac = Math.Clamp(frac, 0, 99);
        return $"{whole:00}.{frac:00}%";
    }

    private static string TryExtractSlotNo(string waferId)
    {
        if (string.IsNullOrEmpty(waferId)) return "--";
        var m = System.Text.RegularExpressions.Regex.Match(waferId, @"-(\d{2})(?=[^\d]|$)");
        if (m.Success) return m.Groups[1].Value;
        m = System.Text.RegularExpressions.Regex.Match(waferId, @"^(\d{2})");
        if (m.Success) return m.Groups[1].Value;
        return "--";
    }
}
