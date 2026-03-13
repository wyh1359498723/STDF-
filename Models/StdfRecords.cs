namespace StdfAnalyzer.Models;

public enum RecordType
{
    FAR, MIR, MRR, WIR, WRR, PIR, PRR, PTR, MPR, FTR, Unknown
}

public record RecordHeader(ushort RecLen, byte RecTyp, byte RecSub)
{
    public RecordType RecordType => (RecTyp, RecSub) switch
    {
        (0, 10) => RecordType.FAR,
        (1, 10) => RecordType.MIR,
        (1, 20) => RecordType.MRR,
        (2, 10) => RecordType.WIR,
        (2, 20) => RecordType.WRR,
        (5, 10) => RecordType.PIR,
        (5, 20) => RecordType.PRR,
        (15, 10) => RecordType.PTR,
        (15, 15) => RecordType.MPR,
        (15, 20) => RecordType.FTR,
        _ => RecordType.Unknown
    };
}

public class TestInfo
{
    public uint TestNum { get; set; }
    public string TestName { get; set; } = "";
    public float? LoLimit { get; set; }
    public float? HiLimit { get; set; }
    public string ColumnName => $"T{TestNum}";
}

public class PartData
{
    public string LotId { get; set; } = "";
    public string WaferId { get; set; } = "";
    public short XCoord { get; set; }
    public short YCoord { get; set; }
    public byte SiteNum { get; set; }
    public ushort HardBin { get; set; }
    public ushort SoftBin { get; set; }
    public bool Pass { get; set; }
    public string PartId { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public int FileIndex { get; set; }
    public Dictionary<uint, float> TestResults { get; } = new();

    public string CoordKey => $"{WaferId}_{XCoord}_{YCoord}";

    public float? GetTestResult(uint testNum) =>
        TestResults.TryGetValue(testNum, out var val) ? val : null;
}

public class StdfFileInfo
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string LotId { get; set; } = "";
    public string WaferId { get; set; } = "";
    public bool IsLittleEndian { get; set; } = true;
    public int TotalParts { get; set; }
    public int PassCount { get; set; }
    public int FailCount { get; set; }
    public int TestCount { get; set; }
    public double Yield => TotalParts > 0 ? (double)PassCount / TotalParts * 100 : 0;

    public Dictionary<string, int> RecordCounts { get; set; } = new();
    public int RawPartsBeforeDedup { get; set; }

    public bool IsMerged { get; set; }
    public int MergedFileCount { get; set; }
    public List<string> MergedFileNames { get; set; } = new();
    public int PreMergeParts { get; set; }
    public int OverwrittenParts { get; set; }
}

public class ParseResult
{
    public StdfFileInfo FileInfo { get; set; } = new();
    public List<PartData> Parts { get; set; } = new();
    public Dictionary<uint, TestInfo> TestInfos { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public bool IsSuccess => ErrorMessage == null;
    public TimeSpan ParseDuration { get; set; }
}
