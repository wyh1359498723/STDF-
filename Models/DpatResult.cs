namespace StdfAnalyzer.Models;

public enum StatMethod
{
    Normal,
    RobustIqr,
    RobustMad,
    Trimmed
}

public class DpatTestLimit
{
    public uint TestNum { get; set; }
    public string TestName { get; set; } = "";
    public double Mean { get; set; }
    public double Sigma { get; set; }
    public double DpatLo { get; set; }
    public double DpatHi { get; set; }
    public float? SpecLo { get; set; }
    public float? SpecHi { get; set; }
    public double FinalLo { get; set; }
    public double FinalHi { get; set; }
    public int TotalCount { get; set; }
    public int FailCount { get; set; }
    public double FailRate => TotalCount > 0 ? (double)FailCount / TotalCount * 100 : 0;
}

public class DpatPartResult
{
    public int PartIndex { get; set; }
    public bool DpatFail { get; set; }
    public int FailCount { get; set; }
    public List<string> FailTests { get; set; } = new();
}

public class DpatAnalysisResult
{
    public StatMethod Method { get; set; }
    public double KSigma { get; set; }
    public bool UseSpecLimits { get; set; }
    public List<DpatTestLimit> TestLimits { get; set; } = new();
    public List<DpatPartResult> PartResults { get; set; } = new();
    public int TotalParts { get; set; }
    public int DpatFailParts { get; set; }
    public double DpatFailRate => TotalParts > 0 ? (double)DpatFailParts / TotalParts * 100 : 0;
}
