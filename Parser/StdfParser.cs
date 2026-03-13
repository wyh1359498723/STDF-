using System.Diagnostics;
using System.IO;
using System.Text;
using StdfAnalyzer.Models;

namespace StdfAnalyzer.Parser;

public class StdfParser
{
    private bool _littleEndian = true;
    private string _lotId = "";
    private string _waferId = "";

    private readonly Dictionary<uint, TestInfo> _testInfos = new();
    private readonly List<PartData> _parts = new();

    private readonly Dictionary<byte, string> _activeParts = new();
    private readonly Dictionary<byte, int> _sitePartCount = new();
    private readonly Dictionary<string, PartData> _partDataMap = new();
    private readonly Dictionary<string, int> _recordCounts = new();

    public ParseResult Parse(string filePath)
    {
        var sw = Stopwatch.StartNew();
        var result = new ParseResult();

        try
        {
            Reset();

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);

            while (fs.Position < fs.Length - 4)
            {
                var header = ReadHeader(reader);
                if (header == null) break;

                var recData = reader.ReadBytes(header.RecLen);
                if (recData.Length < header.RecLen) break;

                var recName = header.RecordType.ToString();
                _recordCounts[recName] = _recordCounts.GetValueOrDefault(recName) + 1;

                switch (header.RecordType)
                {
                    case RecordType.FAR:
                        ParseFar(recData);
                        break;
                    case RecordType.MIR:
                        ParseMir(recData);
                        break;
                    case RecordType.WIR:
                        ParseWir(recData);
                        break;
                    case RecordType.PIR:
                        ParsePir(recData);
                        break;
                    case RecordType.PTR:
                        ParsePtr(recData);
                        break;
                    case RecordType.FTR:
                        ParseFtr(recData);
                        break;
                    case RecordType.PRR:
                        ParsePrr(recData);
                        break;
                }
            }

            // Deduplicate by coordinate: same (WaferId, X, Y) keeps the last occurrence (retest wins)
            var dedupedParts = _parts
                .GroupBy(p => p.CoordKey)
                .Select(g => g.Last())
                .ToList();

            result.Parts = dedupedParts;
            result.TestInfos = new Dictionary<uint, TestInfo>(_testInfos);
            result.FileInfo = new StdfFileInfo
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                LotId = _lotId,
                WaferId = _waferId,
                IsLittleEndian = _littleEndian,
                TotalParts = dedupedParts.Count,
                PassCount = dedupedParts.Count(p => p.Pass),
                FailCount = dedupedParts.Count(p => !p.Pass),
                TestCount = _testInfos.Count,
                RecordCounts = new Dictionary<string, int>(_recordCounts),
                RawPartsBeforeDedup = _parts.Count
            };
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"解析失败: {ex.Message}";
        }

        sw.Stop();
        result.ParseDuration = sw.Elapsed;
        return result;
    }

    private void Reset()
    {
        _littleEndian = true;
        _lotId = "";
        _waferId = "";
        _testInfos.Clear();
        _parts.Clear();
        _activeParts.Clear();
        _sitePartCount.Clear();
        _partDataMap.Clear();
        _recordCounts.Clear();
    }

    private RecordHeader? ReadHeader(BinaryReader reader)
    {
        try
        {
            var headerBytes = reader.ReadBytes(4);
            if (headerBytes.Length < 4) return null;

            ushort recLen = _littleEndian
                ? BitConverter.ToUInt16(headerBytes, 0)
                : (ushort)((headerBytes[0] << 8) | headerBytes[1]);

            return new RecordHeader(recLen, headerBytes[2], headerBytes[3]);
        }
        catch
        {
            return null;
        }
    }

    private void ParseFar(byte[] data)
    {
        if (data.Length < 1) return;
        // cpu_type: 1 = big endian, 2 = little endian (per STDF V4 spec)
        _littleEndian = data[0] != 1;
    }

    private void ParseMir(byte[] data)
    {
        // MIR has many fields; lot_id is at variable offset
        // Skip: SETUP_T(4) + START_T(4) + STAT_NUM(1) + MODE_COD(1) + RTST_COD(1)
        //      + PROT_COD(1) + BURN_TIM(2) = 14 bytes
        if (data.Length < 15) return;

        int offset = 14;
        _lotId = ReadCString(data, ref offset);
    }

    private void ParseWir(byte[] data)
    {
        // HEAD_NUM(1) + SITE_GRP(1) + START_T(4) = 6 bytes, then WAFER_ID
        if (data.Length < 7) return;

        int offset = 6;
        _waferId = ReadCString(data, ref offset);
    }

    private void ParsePir(byte[] data)
    {
        if (data.Length < 2) return;
        byte siteNum = data[1]; // HEAD_NUM(1) + SITE_NUM(1)

        if (!_sitePartCount.ContainsKey(siteNum))
            _sitePartCount[siteNum] = 0;
        _sitePartCount[siteNum]++;

        string partKey = $"TEMP_{siteNum}_{_sitePartCount[siteNum]}";
        _activeParts[siteNum] = partKey;

        var part = new PartData
        {
            LotId = _lotId,
            WaferId = _waferId,
            SiteNum = siteNum
        };
        _partDataMap[partKey] = part;
    }

    private void ParsePtr(byte[] data)
    {
        if (data.Length < 12) return;

        uint testNum = ReadUInt32(data, 0);
        byte siteNum = data[5]; // head_num(1) + site_num at offset 5
        byte testFlg = data[6];
        byte parmFlg = data[7];
        float result = ReadFloat(data, 8);

        // Skip invalid tests
        if ((testFlg & 0x10) != 0) return;

        // Read test name
        int offset = 12;
        string testName = ReadCString(data, ref offset);

        // Read alarm_id
        string alarmId = offset < data.Length ? ReadCString(data, ref offset) : "";

        // Read optional info byte if present
        if (offset < data.Length)
        {
            byte optFlag = data[offset++];
        }

        // Skip RES_SCAL, LLM_SCAL, HLM_SCAL, LO_SPEC, HI_SPEC if present
        // We focus on lo_limit and hi_limit from parm_flg

        float? loLimit = null;
        float? hiLimit = null;

        // Parse limits based on parm_flg
        if ((parmFlg & 0x40) == 0 && offset + 4 <= data.Length)
        {
            loLimit = ReadFloat(data, offset);
            offset += 4;
        }
        if ((parmFlg & 0x80) == 0 && offset + 4 <= data.Length)
        {
            hiLimit = ReadFloat(data, offset);
            offset += 4;
        }

        // Update test info
        if (!_testInfos.ContainsKey(testNum))
        {
            _testInfos[testNum] = new TestInfo
            {
                TestNum = testNum,
                TestName = testName,
                LoLimit = loLimit,
                HiLimit = hiLimit
            };
        }
        else if (string.IsNullOrEmpty(_testInfos[testNum].TestName) && !string.IsNullOrEmpty(testName))
        {
            _testInfos[testNum].TestName = testName;
        }

        // Store result for the active part at this site
        if (_activeParts.TryGetValue(siteNum, out var partKey) &&
            _partDataMap.TryGetValue(partKey, out var part))
        {
            part.TestResults[testNum] = result;
        }
    }

    private void ParseFtr(byte[] data)
    {
        if (data.Length < 7) return;

        uint testNum = ReadUInt32(data, 0);
        byte siteNum = data[5];
        byte testFlg = data[6];

        if ((testFlg & 0x10) != 0) return;

        bool pass = (testFlg & 0x80) == 0;

        string testName = "";
        try
        {
            if (data.Length > 38)
            {
                int offset = 34;
                ushort rtnIcnt = ReadUInt16(data, offset);
                offset += 2;
                ushort pgmIcnt = ReadUInt16(data, offset);
                offset += 2;

                // RTN_INDX: rtnIcnt * 2 bytes
                offset += rtnIcnt * 2;
                // RTN_STAT: nibbles, ceil(rtnIcnt / 2) bytes
                offset += (rtnIcnt + 1) / 2;
                // PGM_INDX: pgmIcnt * 2 bytes
                offset += pgmIcnt * 2;
                // PGM_STAT: nibbles, ceil(pgmIcnt / 2) bytes
                offset += (pgmIcnt + 1) / 2;

                if (offset < data.Length)
                {
                    // FAIL_PIN (D*n): bit-encoded, first 2 bytes = bit count
                    ushort failPinBits = ReadUInt16(data, offset);
                    offset += 2;
                    offset += (failPinBits + 7) / 8;
                }

                // VECT_NAM (C*n)
                if (offset < data.Length) ReadCString(data, ref offset);
                // TIME_SET (C*n)
                if (offset < data.Length) ReadCString(data, ref offset);
                // OP_CODE (C*n)
                if (offset < data.Length) ReadCString(data, ref offset);
                // TEST_TXT (C*n)
                if (offset < data.Length) testName = ReadCString(data, ref offset);
            }
        }
        catch
        {
            // FTR structure varies; if parsing fails, we still register the test
        }

        if (!_testInfos.ContainsKey(testNum))
        {
            _testInfos[testNum] = new TestInfo
            {
                TestNum = testNum,
                TestName = testName
            };
        }
        else if (string.IsNullOrEmpty(_testInfos[testNum].TestName) && !string.IsNullOrEmpty(testName))
        {
            _testInfos[testNum].TestName = testName;
        }

        if (_activeParts.TryGetValue(siteNum, out var partKey) &&
            _partDataMap.TryGetValue(partKey, out var part))
        {
            part.TestResults[testNum] = pass ? 1.0f : 0.0f;
        }
    }

    private void ParsePrr(byte[] data)
    {
        if (data.Length < 13) return;

        byte siteNum = data[1]; // HEAD_NUM(1) + SITE_NUM(1)
        byte partFlg = data[2];
        // num_test(2) at offset 3
        ushort hardBin = ReadUInt16(data, 5);
        ushort softBin = ReadUInt16(data, 7);
        short xCoord = ReadInt16(data, 9);
        short yCoord = ReadInt16(data, 11);

        // test_t(4) at offset 13
        int offset = 17;
        string partId = offset < data.Length ? ReadCString(data, ref offset) : "";

        bool pass = (partFlg & 0x08) == 0;

        if (_activeParts.TryGetValue(siteNum, out var partKey) &&
            _partDataMap.TryGetValue(partKey, out var part))
        {
            part.HardBin = hardBin;
            part.SoftBin = softBin;
            part.XCoord = xCoord;
            part.YCoord = yCoord;
            part.Pass = pass;
            part.PartId = partId;
            _parts.Add(part);
        }
    }

    #region Binary Read Helpers

    private string ReadCString(byte[] data, ref int offset)
    {
        if (offset >= data.Length) return "";
        byte len = data[offset++];
        if (len == 0 || offset + len > data.Length) return "";
        var s = Encoding.ASCII.GetString(data, offset, len);
        offset += len;
        return s.TrimEnd('\0', ' ');
    }

    private ushort ReadUInt16(byte[] data, int offset)
    {
        if (offset + 2 > data.Length) return 0;
        return _littleEndian
            ? (ushort)(data[offset] | (data[offset + 1] << 8))
            : (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private short ReadInt16(byte[] data, int offset)
    {
        return (short)ReadUInt16(data, offset);
    }

    private uint ReadUInt32(byte[] data, int offset)
    {
        if (offset + 4 > data.Length) return 0;
        if (_littleEndian)
            return (uint)(data[offset] | (data[offset + 1] << 8) |
                          (data[offset + 2] << 16) | (data[offset + 3] << 24));
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
                      (data[offset + 2] << 8) | data[offset + 3]);
    }

    private float ReadFloat(byte[] data, int offset)
    {
        if (offset + 4 > data.Length) return 0f;
        var bytes = new byte[4];
        Array.Copy(data, offset, bytes, 0, 4);
        if (!_littleEndian) Array.Reverse(bytes);
        return BitConverter.ToSingle(bytes, 0);
    }

    #endregion
}
