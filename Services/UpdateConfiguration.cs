namespace StdfAnalyzer.Services;

/// <summary>根配置 appsettings.json 中的 Update 节。</summary>
public sealed class UpdateSection
{
    /// <summary>HTTPS（或内网 http，见 AllowInsecureHttp）上的 update.json 完整 URL。</summary>
    public string ManifestUrl { get; set; } = "";

    public bool CheckOnStartup { get; set; } = true;

    /// <summary>距上次检查不足该小时数则跳过，减轻服务器压力。</summary>
    public int CheckIntervalHours { get; set; } = 24;

    /// <summary>为 false 时：发现新版本即下载并安装（仍建议用户无未保存操作）。</summary>
    public bool RequireUserConfirm { get; set; } = true;

    /// <summary>仅当 manifest 提供非空 sha256（64 位十六进制）时校验安装包。</summary>
    public bool VerifySha256WhenProvided { get; set; } = true;

    /// <summary>内网 HTTP 清单/下载（默认仅允许 https）。</summary>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>内网自签名证书等（慎用）。</summary>
    public bool IgnoreTlsErrors { get; set; }

    /// <summary>传给 Inno Setup 安装包的附加参数（默认静默）。</summary>
    public string InstallerArgs { get; set; } = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";
}

public sealed class AppSettingsRoot
{
    public UpdateSection? Update { get; set; }
}

/// <summary>服务器上的 update.json 结构。</summary>
public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string? Sha256 { get; set; }
    public string? ReleaseNotes { get; set; }
}

public sealed class UpdateCheckState
{
    public DateTime LastCheckUtc { get; set; }
}
