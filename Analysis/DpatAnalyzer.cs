using StdfAnalyzer.Models;

namespace StdfAnalyzer.Analysis;

public class DpatAnalyzer
{
    public DpatAnalysisResult Analyze(
        List<PartData> parts,
        Dictionary<uint, TestInfo> testInfos,
        StatMethod method = StatMethod.Normal,
        double kSigma = 6.0,
        bool useSpecLimits = true,
        double trimPercent = 0.05)
    {
        var result = new DpatAnalysisResult
        {
            Method = method,
            KSigma = kSigma,
            UseSpecLimits = useSpecLimits,
            TotalParts = parts.Count
        };

        var partResults = parts.Select((_, i) => new DpatPartResult { PartIndex = i }).ToList();

        foreach (var (testNum, testInfo) in testInfos)
        {
            var values = parts.Select(p => p.GetTestResult(testNum)).ToList();
            var validValues = values.Where(v => v.HasValue).Select(v => (double)v!.Value).ToArray();

            if (validValues.Length < 3) continue;

            var (center, sigma) = CalcStats(validValues, method, trimPercent);

            if (sigma <= 0 || double.IsNaN(sigma) || double.IsInfinity(sigma))
                continue;

            double dpatLo = center - kSigma * sigma;
            double dpatHi = center + kSigma * sigma;

            double finalLo = dpatLo;
            double finalHi = dpatHi;

            if (useSpecLimits)
            {
                if (testInfo.LoLimit.HasValue)
                    finalLo = Math.Max(dpatLo, testInfo.LoLimit.Value);
                if (testInfo.HiLimit.HasValue)
                    finalHi = Math.Min(dpatHi, testInfo.HiLimit.Value);
            }

            int failCount = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                var val = values[i];
                if (!val.HasValue) continue;
                if (val.Value < finalLo || val.Value > finalHi)
                {
                    partResults[i].DpatFail = true;
                    partResults[i].FailCount++;
                    partResults[i].FailTests.Add(testInfo.TestName);
                    failCount++;
                }
            }

            result.TestLimits.Add(new DpatTestLimit
            {
                TestNum = testNum,
                TestName = testInfo.TestName,
                Mean = center,
                Sigma = sigma,
                DpatLo = dpatLo,
                DpatHi = dpatHi,
                SpecLo = testInfo.LoLimit,
                SpecHi = testInfo.HiLimit,
                FinalLo = finalLo,
                FinalHi = finalHi,
                TotalCount = validValues.Length,
                FailCount = failCount
            });
        }

        result.PartResults = partResults;
        result.DpatFailParts = partResults.Count(p => p.DpatFail);

        return result;
    }

    private static (double center, double sigma) CalcStats(double[] values, StatMethod method, double trimPercent)
    {
        return method switch
        {
            StatMethod.Normal => CalcNormal(values),
            StatMethod.RobustIqr => CalcRobustIqr(values),
            StatMethod.RobustMad => CalcRobustMad(values),
            StatMethod.Trimmed => CalcTrimmed(values, trimPercent),
            _ => CalcNormal(values)
        };
    }

    private static (double mean, double std) CalcNormal(double[] values)
    {
        double mean = values.Average();
        double variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);
        return (mean, Math.Sqrt(variance));
    }

    private static (double median, double sigma) CalcRobustIqr(double[] values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        double median = Percentile(sorted, 50);
        double q25 = Percentile(sorted, 25);
        double q75 = Percentile(sorted, 75);
        double sigma = (q75 - q25) / 1.349;
        return (median, sigma);
    }

    private static (double median, double sigma) CalcRobustMad(double[] values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        double median = Percentile(sorted, 50);
        var absDevs = values.Select(v => Math.Abs(v - median)).OrderBy(v => v).ToArray();
        double mad = Percentile(absDevs, 50);
        double sigma = mad * 1.4826;
        return (median, sigma);
    }

    private static (double mean, double std) CalcTrimmed(double[] values, double trimPercent)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int trimCount = (int)(sorted.Length * trimPercent);
        var trimmed = sorted.Skip(trimCount).Take(sorted.Length - 2 * trimCount).ToArray();

        if (trimmed.Length < 2)
            return CalcNormal(values);

        double mean = trimmed.Average();
        double variance = trimmed.Sum(v => (v - mean) * (v - mean)) / (trimmed.Length - 1);
        return (mean, Math.Sqrt(variance));
    }

    private static double Percentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0) return 0;
        double index = (percentile / 100.0) * (sortedValues.Length - 1);
        int lower = (int)Math.Floor(index);
        int upper = (int)Math.Ceiling(index);
        if (lower == upper) return sortedValues[lower];
        double frac = index - lower;
        return sortedValues[lower] * (1 - frac) + sortedValues[upper] * frac;
    }
}
