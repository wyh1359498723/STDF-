using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CsvHelper;
using StdfAnalyzer.Models;
using StdfAnalyzer.Parser;

namespace StdfAnalyzer.ViewModels;

public partial class FileQueueItem : ObservableObject
{
    [ObservableProperty] private int _order;
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string FileSize { get; init; } = "";
}

public partial class MainViewModel : ObservableObject
{
    private readonly StdfParser _parser = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "将 STDF 文件拖拽到此处，添加到解析队列";
    [ObservableProperty] private ParseResult? _currentResult;
    [ObservableProperty] private DataTable? _testDataTable;
    [ObservableProperty] private DataTable? _hbinTable;
    [ObservableProperty] private DataTable? _sbinTable;
    [ObservableProperty] private List<TestInfo>? _testInfoList;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _hasFileQueue;
    [ObservableProperty] private int _selectedTabIndex;

    public ObservableCollection<FileQueueItem> FileQueue { get; } = new();
    public ObservableCollection<ParseResult> ParseHistory { get; } = new();

    #region File Queue Management

    public void AddFilesToQueue(string[] filePaths)
    {
        var stdfPaths = filePaths
            .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".std" or ".stdf" or ".stdf_tmp")
            .Where(p => FileQueue.All(q => q.FilePath != p))
            .ToArray();

        if (stdfPaths.Length == 0)
        {
            StatusText = FileQueue.Count > 0
                ? $"无新文件添加（已有 {FileQueue.Count} 个文件在队列中）"
                : "未找到有效的 STDF 文件 (.std / .stdf)";
            return;
        }

        foreach (var path in stdfPaths)
        {
            var fi = new FileInfo(path);
            FileQueue.Add(new FileQueueItem
            {
                Order = FileQueue.Count + 1,
                FilePath = path,
                FileName = fi.Name,
                FileSize = FormatFileSize(fi.Length)
            });
        }

        HasFileQueue = true;
        HasData = false;
        StatusText = $"队列中有 {FileQueue.Count} 个文件，拖动调整顺序后点击「开始解析」";
    }

    [RelayCommand]
    private void MoveFileUp(FileQueueItem item)
    {
        int idx = FileQueue.IndexOf(item);
        if (idx <= 0) return;
        FileQueue.Move(idx, idx - 1);
        RefreshOrders();
    }

    [RelayCommand]
    private void MoveFileDown(FileQueueItem item)
    {
        int idx = FileQueue.IndexOf(item);
        if (idx < 0 || idx >= FileQueue.Count - 1) return;
        FileQueue.Move(idx, idx + 1);
        RefreshOrders();
    }

    [RelayCommand]
    private void RemoveFile(FileQueueItem item)
    {
        FileQueue.Remove(item);
        RefreshOrders();
        HasFileQueue = FileQueue.Count > 0;
        StatusText = FileQueue.Count > 0
            ? $"队列中有 {FileQueue.Count} 个文件"
            : "将 STDF 文件拖拽到此处，添加到解析队列";
    }

    [RelayCommand]
    public void ClearQueue()
    {
        FileQueue.Clear();
        HasFileQueue = false;
        StatusText = "将 STDF 文件拖拽到此处，添加到解析队列";
    }

    public void MoveFile(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= FileQueue.Count) return;
        if (toIndex < 0 || toIndex >= FileQueue.Count) return;
        if (fromIndex == toIndex) return;
        FileQueue.Move(fromIndex, toIndex);
        RefreshOrders();
    }

    private void RefreshOrders()
    {
        for (int i = 0; i < FileQueue.Count; i++)
            FileQueue[i].Order = i + 1;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    #endregion

    #region Parse

    [RelayCommand]
    private async Task ParseAllAsync()
    {
        if (IsLoading || FileQueue.Count == 0) return;

        var filePaths = FileQueue.Select(q => q.FilePath).ToArray();

        try
        {
            IsLoading = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var allResults = new List<ParseResult>();
            for (int i = 0; i < filePaths.Length; i++)
            {
                var path = filePaths[i];
                StatusText = $"正在解析 ({i + 1}/{filePaths.Length}): {Path.GetFileName(path)} ...";

                var result = await Task.Run(() =>
                {
                    var r = _parser.Parse(path);
                    foreach (var part in r.Parts)
                    {
                        part.FileIndex = i;
                        part.SourceFile = Path.GetFileName(path);
                    }
                    return r;
                });

                if (result.IsSuccess)
                {
                    allResults.Add(result);
                    ParseHistory.Insert(0, result);
                }
                else
                {
                    MessageBox.Show($"文件解析失败: {Path.GetFileName(path)}\n{result.ErrorMessage}",
                        "解析警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }

            if (allResults.Count == 0)
            {
                StatusText = "所有文件解析均失败";
                return;
            }

            sw.Stop();
            var merged = MergeResults(allResults, sw.Elapsed);

            BuildTestDataTable(merged);
            BuildBinTables(merged);
            TestInfoList = merged.TestInfos.Values.OrderBy(t => t.TestNum).ToList();

            CurrentResult = merged;
            HasData = true;

            if (merged.FileInfo.IsMerged)
            {
                StatusText = $"合并完成: {merged.FileInfo.MergedFileCount} 个文件 | " +
                             $"合并前: {merged.FileInfo.PreMergeParts} → 合并后: {merged.Parts.Count} 芯片 " +
                             $"(覆盖 {merged.FileInfo.OverwrittenParts} 个同坐标) | " +
                             $"良率: {merged.FileInfo.Yield:F1}% | 耗时: {sw.Elapsed.TotalMilliseconds:F0}ms";
            }
            else
            {
                StatusText = $"解析完成: {merged.FileInfo.FileName} | " +
                             $"批次: {merged.FileInfo.LotId} | 晶圆: {merged.FileInfo.WaferId} | " +
                             $"芯片: {merged.Parts.Count} | 测试项: {merged.TestInfos.Count} | " +
                             $"良率: {merged.FileInfo.Yield:F1}% | 耗时: {sw.Elapsed.TotalMilliseconds:F0}ms";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"错误: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ClearResults()
    {
        HasData = false;
        CurrentResult = null;
        TestDataTable = null;
        HbinTable = null;
        SbinTable = null;
        TestInfoList = null;

        StatusText = FileQueue.Count > 0
            ? $"队列中有 {FileQueue.Count} 个文件，拖动调整顺序后点击「开始解析」"
            : "将 STDF 文件拖拽到此处，添加到解析队列";
    }

    private static ParseResult MergeResults(List<ParseResult> results, TimeSpan totalDuration)
    {
        if (results.Count == 1)
            return results[0];

        var mergedTestInfos = new Dictionary<uint, TestInfo>();
        var allParts = new List<PartData>();
        var fileNames = new List<string>();

        foreach (var r in results)
        {
            fileNames.Add(r.FileInfo.FileName);

            foreach (var (testNum, info) in r.TestInfos)
            {
                if (!mergedTestInfos.ContainsKey(testNum))
                    mergedTestInfos[testNum] = info;
                else if (string.IsNullOrEmpty(mergedTestInfos[testNum].TestName) && !string.IsNullOrEmpty(info.TestName))
                    mergedTestInfos[testNum].TestName = info.TestName;
            }

            allParts.AddRange(r.Parts);
        }

        int preMergeCount = allParts.Count;

        var mergedParts = allParts
            .GroupBy(p => p.CoordKey)
            .Select(g => g.OrderByDescending(p => p.FileIndex).First())
            .OrderBy(p => p.WaferId)
            .ThenBy(p => p.YCoord)
            .ThenBy(p => p.XCoord)
            .ToList();

        int overwritten = preMergeCount - mergedParts.Count;

        var firstInfo = results[0].FileInfo;
        return new ParseResult
        {
            Parts = mergedParts,
            TestInfos = mergedTestInfos,
            ParseDuration = totalDuration,
            FileInfo = new StdfFileInfo
            {
                FilePath = firstInfo.FilePath,
                FileName = string.Join(" + ", fileNames),
                LotId = firstInfo.LotId,
                WaferId = string.Join(", ", results.Select(r => r.FileInfo.WaferId).Distinct()),
                IsLittleEndian = firstInfo.IsLittleEndian,
                TotalParts = mergedParts.Count,
                PassCount = mergedParts.Count(p => p.Pass),
                FailCount = mergedParts.Count(p => !p.Pass),
                TestCount = mergedTestInfos.Count,
                IsMerged = true,
                MergedFileCount = results.Count,
                MergedFileNames = fileNames,
                PreMergeParts = preMergeCount,
                OverwrittenParts = overwritten
            }
        };
    }

    #endregion

    #region Export

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (TestDataTable == null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV 文件|*.csv",
            FileName = CurrentResult?.FileInfo.FileName?.Replace(".stdf", "").Replace(".std", "") + "_export.csv"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            IsLoading = true;
            StatusText = "正在导出 CSV...";

            await Task.Run(() =>
            {
                using var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8);
                using var csv = new CsvWriter(writer, System.Globalization.CultureInfo.InvariantCulture);

                foreach (DataColumn col in TestDataTable.Columns)
                    csv.WriteField(col.ColumnName);
                csv.NextRecord();

                foreach (DataRow row in TestDataTable.Rows)
                {
                    foreach (var item in row.ItemArray)
                        csv.WriteField(item);
                    csv.NextRecord();
                }
            });

            StatusText = $"CSV 已导出: {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText = $"导出错误: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Table Builders

    private void BuildTestDataTable(ParseResult result)
    {
        var dt = new DataTable();
        dt.Columns.Add("Part_ID", typeof(string));
        dt.Columns.Add("Site", typeof(byte));
        dt.Columns.Add("X", typeof(short));
        dt.Columns.Add("Y", typeof(short));
        dt.Columns.Add("HBin", typeof(ushort));
        dt.Columns.Add("SBin", typeof(ushort));
        dt.Columns.Add("Pass", typeof(bool));

        if (result.FileInfo.IsMerged)
            dt.Columns.Add("Source", typeof(string));

        var sortedTests = result.TestInfos.OrderBy(kv => kv.Key).ToList();
        foreach (var (testNum, info) in sortedTests)
        {
            string colName = string.IsNullOrEmpty(info.TestName) ? $"T{testNum}" : $"{info.TestName}(T{testNum})";
            dt.Columns.Add(colName, typeof(float));
        }

        foreach (var part in result.Parts)
        {
            var row = dt.NewRow();
            row["Part_ID"] = part.PartId;
            row["Site"] = part.SiteNum;
            row["X"] = part.XCoord;
            row["Y"] = part.YCoord;
            row["HBin"] = part.HardBin;
            row["SBin"] = part.SoftBin;
            row["Pass"] = part.Pass;

            if (result.FileInfo.IsMerged)
                row["Source"] = part.SourceFile;

            foreach (var (testNum, info) in sortedTests)
            {
                string colName = string.IsNullOrEmpty(info.TestName) ? $"T{testNum}" : $"{info.TestName}(T{testNum})";
                if (part.TestResults.TryGetValue(testNum, out var val))
                    row[colName] = val;
                else
                    row[colName] = DBNull.Value;
            }

            dt.Rows.Add(row);
        }

        TestDataTable = dt;
    }

    private void BuildBinTables(ParseResult result)
    {
        HbinTable = BuildSingleBinTable(result.Parts, p => p.HardBin);
        SbinTable = BuildSingleBinTable(result.Parts, p => p.SoftBin);
    }

    private static DataTable BuildSingleBinTable(List<PartData> parts, Func<PartData, ushort> binSelector)
    {
        var dt = new DataTable();
        dt.Columns.Add("Bin", typeof(ushort));
        dt.Columns.Add("数量", typeof(int));
        dt.Columns.Add("占比", typeof(string));

        int total = parts.Count;
        var groups = parts
            .GroupBy(binSelector)
            .OrderBy(g => g.Key);

        foreach (var g in groups)
        {
            double pct = total > 0 ? (double)g.Count() / total * 100 : 0;
            dt.Rows.Add(g.Key, g.Count(), $"{pct:F2}%");
        }

        return dt;
    }

    #endregion
}
