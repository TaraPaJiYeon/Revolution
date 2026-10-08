using System.Net;
using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using COSXML;
using COSXML.Auth;
using COSXML.Model.Object;

namespace Revolution.Could.Publishing;

public sealed record CosSettings(string Bucket, string Region, string SecretId, string SecretKey);

/// <summary>顺序发布：远端预检 → 内容（可重试）→ 清单（最终提交）。任何失败都不会提前公开新清单。</summary>
public sealed class CosPublisher
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public async Task PublishAsync(ReleasePlan plan, CosSettings settings, IProgress<string> log, IProgress<double> progress, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(settings.Bucket, @"^[a-z0-9-]+-\d+$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(settings.Region, @"^[a-z]+-[a-z0-9-]+$") ||
            string.IsNullOrWhiteSpace(settings.SecretId) || string.IsNullOrWhiteSpace(settings.SecretKey))
            throw new InvalidOperationException("请填写完整桶名（含 APPID 后缀）、地域以及 SecretId/SecretKey。");

        string root = $"https://{settings.Bucket}.cos.{settings.Region}.myqcloud.com/";
        // 直接访问 COS 源站检查，绕过 CDN 的旧缓存。工具目前面向可匿名读取的公有读桶。
        byte[]? oldManifest = await ReadRemoteAsync(root + plan.ManifestKey, ct);
        if (oldManifest != null)
        {
            string old = System.Text.Encoding.UTF8.GetString(oldManifest).TrimStart('\uFEFF');
            string? oldVersion = old.Split('\n').FirstOrDefault(s => s.StartsWith("@resVersion|", StringComparison.Ordinal))?.Split('|').ElementAtOrDefault(1)?.Trim();
            if (oldVersion == null) throw new InvalidOperationException("远端已有清单但无法解析版本，停止上传，避免覆盖线上版本。");
            if (Version.TryParse(oldVersion, out var previous) && Version.TryParse(plan.ResVersion, out var current))
            {
                if (current <= previous) throw new InvalidOperationException($"远端资源版本 {oldVersion} 不低于本次 {plan.ResVersion}。请生成更高的资源版本，禁止回退或覆盖。");
            }
            else if (oldVersion == plan.ResVersion || string.CompareOrdinal(plan.ResVersion, oldVersion) <= 0)
                throw new InvalidOperationException($"远端资源版本 {oldVersion} 不低于本次版本。请核对版本号。");
        }
        ct.ThrowIfCancellationRequested();
        log.Report("远端清单预检完成，开始检查不可变资源是否已存在…");
        // 下载已存在的同路径对象并核对散列。不能只看大小：同名不同内容被覆盖后，CDN 长缓存会产生版本撕裂。
        bool[] skip = new bool[plan.Contents.Count];
        for (int i = 0; i < plan.Contents.Count; i++)
        {
            var file = plan.Contents[i];
            string? hash = await ReadRemoteHashAsync(root + file.Key, ct);
            if (hash == null) continue;
            if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"远端同版本路径已有不同内容：{file.Key}。请提高资源版本并重新生成清单，禁止覆盖不可变资源。");
            skip[i] = true;
        }
        // 预检之后、上传之前再次检查本地快照。上传时请不要再改动 Unity 的产物文件。
        foreach (var file in plan.Contents)
            if (new FileInfo(file.Path).Length != file.Size || !string.Equals(await ReleasePlanBuilder.HashAsync(file.Path, ct), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"本地文件在预检后发生变化：{file.Path}");
        if (!string.Equals(await ReleasePlanBuilder.HashAsync(plan.ManifestPath, ct), plan.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("清单在预检后发生变化，请重新选择目录。");

        var config = new CosXmlConfig.Builder().IsHttps(true).SetRegion(settings.Region).Build();
        // 本工具不保存密钥；程序关闭后凭证即消失。仅适合可信的发布人员电脑。
        QCloudCredentialProvider credentials = new DefaultQCloudCredentialProvider(settings.SecretId, settings.SecretKey, 600);
        CosXml client = new CosXmlServer(config, credentials);
        for (int i = 0; i < plan.Contents.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = plan.Contents[i];
            if (skip[i]) { log.Report($"已存在且 SHA-256 一致，跳过：{file.Key}"); }
            else
            {
                log.Report($"上传 {i + 1}/{plan.Contents.Count}：{file.Key}");
                await Task.Run(() =>
                {
                    var request = new PutObjectRequest(settings.Bucket, file.Key, file.Path);
                    request.SetRequestHeader("Cache-Control", "public,max-age=31536000,immutable");
                    client.PutObject(request);
                }, ct);
            }
            progress.Report((i + 1.0) / (plan.Contents.Count + 1));
        }
        ct.ThrowIfCancellationRequested();
        // 发布期间可能有其他人抢先发布；提交前再次确认入口没有改变。
        byte[]? beforeCommit = await ReadRemoteAsync(root + plan.ManifestKey, ct);
        if (!((oldManifest == null && beforeCommit == null) || (oldManifest != null && beforeCommit != null && oldManifest.SequenceEqual(beforeCommit))))
            throw new InvalidOperationException("上传期间远端清单被其他发布者修改，本次不提交清单。请检查版本并重新发布。");
        if (!string.Equals(await ReleasePlanBuilder.HashAsync(plan.ManifestPath, ct), plan.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("清单在上传期间被修改，本次不提交。");
        log.Report("全部资源已就位，最后上传入口清单…");
        // 已存在的旧版入口会被最后一次覆盖；任何资源失败都不会执行到这里。
        await Task.Run(() =>
        {
            var request = new PutObjectRequest(settings.Bucket, plan.ManifestKey, plan.ManifestPath);
            request.SetRequestHeader("Cache-Control", "no-cache,no-store,must-revalidate");
            client.PutObject(request);
        }, ct);
        progress.Report(1);
        log.Report("发布完成。请检查 CDN 缓存规则 / 刷新入口清单 URL，再到客户端验证更新。");
    }

    private async Task<HttpResponseMessage> RequestRemoteAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == HttpStatusCode.NotFound || response.IsSuccessStatusCode) return response;
        int code = (int)response.StatusCode;
        response.Dispose();
        throw new InvalidOperationException($"COS 源站预检失败（HTTP {code}）：{url}。请确认桶为公有读、地域正确且网络可用；不会冒险发布。");
    }

    private async Task<byte[]?> ReadRemoteAsync(string url, CancellationToken ct)
    {
        using var response = await RequestRemoteAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.Content.Headers.ContentLength is > 1048576)
            throw new InvalidOperationException("远端清单超过 1 MiB，请检查目标路径。");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        if (buffer.Length > 1048576) throw new InvalidOperationException("远端清单超过 1 MiB，请检查目标路径。");
        return buffer.ToArray();
    }

    private async Task<string?> ReadRemoteHashAsync(string url, CancellationToken ct)
    {
        using var response = await RequestRemoteAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }
}
