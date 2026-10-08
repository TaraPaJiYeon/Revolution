using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Revolution.Could.Publishing;

/// <summary>一个已经通过本地完整性检查的发布快照。清单必须由 Unity 的 RevHotManifestBuilder 生成。</summary>
public sealed record ReleaseFile(string Path, string Key, long Size, string Sha256);

public sealed class ReleasePlan
{
    public required string ManifestPath { get; init; }
    public required string ManifestKey { get; init; }
    public required string Platform { get; init; }
    public required string AppVersion { get; init; }
    public required string ResVersion { get; init; }
    public required string Environment { get; init; }
    public required string Channel { get; init; }
    public required IReadOnlyList<ReleaseFile> Contents { get; init; }
    public required string ManifestSha256 { get; init; }
    public long TotalBytes => Contents.Sum(f => f.Size);
}

/// <summary>只信任清单内列出的文件，不扫描整个目录；避免把 .manifest、首包基线或其他文件上传到 COS。</summary>
public static class ReleasePlanBuilder
{
    public static async Task<ReleasePlan> BuildAsync(string directory, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory)) throw new InvalidOperationException("AB 产物目录不存在，请先在 Unity 打包并生成热更清单。");
        string manifestPath = Path.Combine(directory, "RevHotManifest.txt");
        if (!File.Exists(manifestPath)) throw new InvalidOperationException("找不到 RevHotManifest.txt，请先在 Unity 的热更清单窗口生成并自检。");

        string text = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var bundles = new List<(string Name, string Hash, long Size, string[] Dependencies)>();
        (string Name, string Hash, long Size)? map = null;
        int? count = null;
        foreach (string raw in text.TrimStart('\uFEFF').Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith('@')) continue;
            string[] parts = line[1..].Split('|');
            switch (parts[0])
            {
                case "bundleCount":
                    if (count != null || parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1)
                        throw new InvalidOperationException("清单的 bundleCount 无效或重复。");
                    count = n;
                    break;
                case "bundle":
                    if (parts.Length < 6) throw new InvalidOperationException("清单 bundle 行格式错误。");
                    bundles.Add((SafeName(parts[1]), CheckHash(parts[2]), ParseSize(parts[3]), parts[4].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                    break;
                case "resmap":
                    if (map != null || parts.Length != 4) throw new InvalidOperationException("清单 resmap 行格式错误或重复。");
                    map = (SafeName(parts[1]), CheckHash(parts[2]), ParseSize(parts[3]));
                    break;
                case "appVersion": case "resVersion": case "platform": case "env": case "channel": case "hashAlgo":
                    if (parts.Length != 2 || !fields.TryAdd(parts[0], parts[1].Trim())) throw new InvalidOperationException($"清单字段 {parts[0]} 无效或重复。");
                    break;
            }
        }
        string Field(string key) => fields.TryGetValue(key, out string? value) ? value : "";
        if (count != bundles.Count || bundles.Count == 0) throw new InvalidOperationException("清单包数量与 bundleCount 不一致，可能已被截断。");
        if (map == null || map.Value.Name != "ResMap.txt") throw new InvalidOperationException("清单缺少 ResMap.txt，新资源将无法被热更加载。");
        if (Field("hashAlgo") != "sha256") throw new InvalidOperationException("只支持 SHA-256 清单。");
        string platform = SafeSegment(Field("platform")), app = SafeSegment(Field("appVersion")), res = SafeSegment(Field("resVersion"));
        string env = OptionalSegment(Field("env")), channel = OptionalSegment(Field("channel"));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bundle in bundles)
            if (!names.Add(bundle.Name)) throw new InvalidOperationException($"清单包含重复包名：{bundle.Name}");
        foreach (var bundle in bundles)
            foreach (string dep in bundle.Dependencies)
                if (!names.Contains(dep)) throw new InvalidOperationException($"包 {bundle.Name} 依赖 {dep}，但清单中没有这个包。");

        // 客户端约定：环境/平台/大版本/渠道/资源版本/bundles/包名；清单不带资源版本。
        string prefix = string.Join('/', new[] { env, platform, app, channel }.Where(s => s.Length > 0));
        var files = new List<ReleaseFile>();
        foreach (var bundle in bundles)
            files.Add(await CheckFileAsync(directory, bundle.Name, $"{prefix}/{res}/bundles/{bundle.Name}", bundle.Size, bundle.Hash, cancellationToken));
        files.Add(await CheckFileAsync(directory, map.Value.Name, $"{prefix}/{res}/{map.Value.Name}", map.Value.Size, map.Value.Hash, cancellationToken));
        string manifestHash = await HashAsync(manifestPath, cancellationToken);
        return new ReleasePlan
        {
            ManifestPath = manifestPath, ManifestKey = $"{prefix}/RevHotManifest.txt", Platform = platform,
            AppVersion = app, ResVersion = res, Environment = env, Channel = channel,
            Contents = files, ManifestSha256 = manifestHash
        };
    }

    private static async Task<ReleaseFile> CheckFileAsync(string dir, string name, string key, long size, string hash, CancellationToken ct)
    {
        string path = Path.Combine(dir, name);
        if (!File.Exists(path)) throw new InvalidOperationException($"清单引用的文件不存在：{name}");
        if (new FileInfo(path).Length != size) throw new InvalidOperationException($"文件大小不匹配：{name}，请重新打包并生成清单。");
        if (!string.Equals(await HashAsync(path, ct), hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"SHA-256 不匹配：{name}，禁止发布已被修改的产物。");
        return new ReleaseFile(path, key, size, hash);
    }

    public static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }

    private static string CheckHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit)
        ? value.ToLowerInvariant() : throw new InvalidOperationException("清单 SHA-256 格式不正确。");
    private static long ParseSize(string text) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long size) && size >= 0
        ? size : throw new InvalidOperationException("清单文件大小无效。");
    private static string SafeName(string value) => value.Length > 0 && value is not ("." or "..")
        && value.IndexOfAny(['/', '\\', '|', ':', '?', '#', '%']) < 0 && !Path.IsPathRooted(value)
        && !value.Any(char.IsControl)
        ? value : throw new InvalidOperationException($"清单文件名不安全：{value}");
    private static string SafeSegment(string value) => value.Length > 0 ? OptionalSegment(value) : throw new InvalidOperationException("清单缺少平台/版本号。");
    private static string OptionalSegment(string value)
    {
        if (value.IndexOfAny(['/', '\\', '?', '#', '%', '|', ':']) >= 0 || value is "." or ".." || value.Contains(' ') || value.Any(c => char.IsControl(c)))
            throw new InvalidOperationException($"清单路径段不安全：{value}");
        return value;
    }
}
