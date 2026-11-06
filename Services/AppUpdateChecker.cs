using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace StdfAnalyzer.Services;

/// <summary>从服务器拉取清单、比对版本、下载安装包并静默启动 Inno 安装。</summary>
public static class AppUpdateChecker
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>主窗口 Loaded 且欢迎页关闭后调用（应在 UI 线程）。</summary>
    public static async Task CheckAfterStartupAsync(Window owner)
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var settingsPath = Path.Combine(baseDir, "appsettings.json");
            if (!File.Exists(settingsPath))
                return;

            UpdateSection? upd;
            await using (var fs = File.OpenRead(settingsPath))
            {
                var root = await JsonSerializer.DeserializeAsync<AppSettingsRoot>(fs, JsonOptions).ConfigureAwait(false);
                upd = root?.Update;
            }

            if (upd is null || !upd.CheckOnStartup || string.IsNullOrWhiteSpace(upd.ManifestUrl))
                return;

            var manifestUri = new Uri(upd.ManifestUrl.Trim(), UriKind.Absolute);
            if (!upd.AllowInsecureHttp && manifestUri.Scheme != Uri.UriSchemeHttps)
                return;

            if (!ShouldCheckNow(upd.CheckIntervalHours))
                return;

            UpdateManifest? manifest;
            using (var client = CreateHttpClient(upd))
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    $"StdfAnalyzer/{GetCurrentVersionString()} (Windows)");

                using var resp = await client.GetAsync(manifestUri, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, JsonOptions)
                    .ConfigureAwait(false);
            }

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) ||
                string.IsNullOrWhiteSpace(manifest.DownloadUrl))
            {
                TouchLastCheck();
                return;
            }

            var downloadUri = new Uri(manifest.DownloadUrl.Trim(), UriKind.Absolute);
            if (!upd.AllowInsecureHttp && downloadUri.Scheme != Uri.UriSchemeHttps)
            {
                TouchLastCheck();
                return;
            }

            var current = GetCurrentVersionString();
            if (!TryParseVersionLoose(manifest.Version, out var remoteVer) ||
                !TryParseVersionLoose(current, out var localVer) ||
                remoteVer <= localVer)
            {
                TouchLastCheck();
                return;
            }

            await RunUpdateFlowAsync(owner, upd, manifest, downloadUri).ConfigureAwait(true);
        }
        catch
        {
            // 网络/解析失败不阻断使用
        }
    }

    private static async Task RunUpdateFlowAsync(
        Window owner,
        UpdateSection settings,
        UpdateManifest manifest,
        Uri downloadUri)
    {
        try
        {
            var notes = string.IsNullOrWhiteSpace(manifest.ReleaseNotes)
                ? ""
                : $"\n\n更新说明：\n{manifest.ReleaseNotes}";

            if (settings.RequireUserConfirm)
            {
                var msg =
                    $"发现新版本 {manifest.Version}（当前 {GetCurrentVersionString()}）。{notes}\n\n是否下载并安装？安装前将关闭本程序。";
                var r = await owner.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(owner, msg, "检查更新",
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No));
                if (r != MessageBoxResult.Yes)
                {
                    TouchLastCheck();
                    return;
                }
            }

            await owner.Dispatcher.InvokeAsync(() => Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait);

            string tempExe;
            try
            {
                tempExe = await Task.Run(() => DownloadInstallerAsync(settings, downloadUri)).ConfigureAwait(false);
            }
            finally
            {
                await owner.Dispatcher.InvokeAsync(() => Mouse.OverrideCursor = null);
            }

            if (settings.VerifySha256WhenProvided &&
                !string.IsNullOrWhiteSpace(manifest.Sha256) &&
                !await VerifySha256HexAsync(tempExe, manifest.Sha256.Trim()).ConfigureAwait(false))
            {
                await owner.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(owner, "安装包校验失败（SHA256 不匹配），已取消更新。", "检查更新",
                        MessageBoxButton.OK, MessageBoxImage.Warning));
                TryDeleteFile(tempExe);
                return;
            }

            var args = string.IsNullOrWhiteSpace(settings.InstallerArgs)
                ? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-"
                : settings.InstallerArgs.Trim();

            await owner.Dispatcher.InvokeAsync(() =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempExe,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetTempPath()
                });
            });

            TouchLastCheck();
            await Task.Delay(400).ConfigureAwait(false);
            await owner.Dispatcher.InvokeAsync(() => Application.Current.Shutdown(0), DispatcherPriority.Normal);
        }
        catch (Exception ex)
        {
            await owner.Dispatcher.InvokeAsync(() =>
                MessageBox.Show(owner, $"更新失败：{ex.Message}", "检查更新",
                    MessageBoxButton.OK, MessageBoxImage.Error));
        }
    }

    private static HttpClient CreateHttpClient(UpdateSection settings)
    {
        var handler = new HttpClientHandler();
        if (settings.IgnoreTlsErrors)
            handler.ServerCertificateCustomValidationCallback = static (_, _, _, _) => true;

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
    }

    private static async Task<string> DownloadInstallerAsync(UpdateSection settings, Uri downloadUri)
    {
        using var client = CreateHttpClient(settings);

        var ext = Path.GetExtension(downloadUri.AbsolutePath);
        if (string.IsNullOrEmpty(ext) || ext.Length > 5)
            ext = ".exe";

        var path = Path.Combine(Path.GetTempPath(), $"StdfAnalyzer_Setup_{Guid.NewGuid():N}{ext}");
        await using (var outFs = File.Create(path))
        {
            using var resp = await client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var inStream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await inStream.CopyToAsync(outFs).ConfigureAwait(false);
        }

        return path;
    }

    private static async Task<bool> VerifySha256HexAsync(string filePath, string sha256Hex)
    {
        if (sha256Hex.Length != 64)
            return false;

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(sha256Hex);
        }
        catch
        {
            return false;
        }

        await using var fs = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(fs).ConfigureAwait(false);
        return hash.AsSpan().SequenceEqual(expected);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private static string StatePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StdfAnalyzer", "update-check.json");

    private static bool ShouldCheckNow(int intervalHours)
    {
        if (intervalHours <= 0)
            return true;

        try
        {
            if (!File.Exists(StatePath))
                return true;

            var json = File.ReadAllText(StatePath);
            var state = JsonSerializer.Deserialize<UpdateCheckState>(json, JsonOptions);
            if (state is null)
                return true;

            return DateTime.UtcNow - state.LastCheckUtc >= TimeSpan.FromHours(intervalHours);
        }
        catch
        {
            return true;
        }
    }

    private static void TouchLastCheck()
    {
        try
        {
            var dir = Path.GetDirectoryName(StatePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var state = new UpdateCheckState { LastCheckUtc = DateTime.UtcNow };
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch
        {
            // ignore
        }
    }

    private static string GetCurrentVersionString()
    {
        var asm = Assembly.GetExecutingAssembly();
        var attr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(attr))
        {
            var plus = attr.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? attr[..plus] : attr;
        }

        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static bool TryParseVersionLoose(string s, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(s))
            return false;

        s = s.Trim();
        var parts = s.Split('.');
        try
        {
            version = parts.Length switch
            {
                1 => new Version(int.Parse(parts[0]), 0),
                2 => new Version(int.Parse(parts[0]), int.Parse(parts[1])),
                3 => new Version(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2])),
                _ => Version.Parse(s)
            };
            return true;
        }
        catch
        {
            return false;
        }
    }
}
