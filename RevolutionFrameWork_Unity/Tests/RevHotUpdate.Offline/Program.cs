using Revolution.HotUpdate;

static class Program
{
    static int _passed;
    static void Main()
    {
        RevHotManifest manifest = NewManifest();
        Expect(manifest.ValidateForActivation("PC", "official") == null, "完整清单通过");
        Expect(manifest.ValidateForActivation("Android", "official") != null, "跨平台拒绝");
        Expect(manifest.ValidateForActivation("PC", "other") != null, "跨渠道拒绝");
        Expect(manifest.ValidateForActivation("PC", "official", "prod") != null, "跨环境拒绝");
        Expect(manifest.ValidateForActivation("PC", "official", "") == null, "同环境通过");

        manifest.Bundles[0].Dependencies.Add("missing");
        Expect(manifest.ValidateForActivation("PC", "official") != null, "缺依赖拒绝");
        manifest.Bundles[0].Dependencies.Clear();
        manifest.Bundles[0].Dependencies.Add("hero");
        Expect(manifest.ValidateForActivation("PC", "official") != null, "自依赖拒绝");
        manifest.Bundles[0].Dependencies.Clear();
        manifest.Bundles.Add(NewBundle("armor", 'c'));
        manifest.Bundles[0].Dependencies.Add("armor");
        manifest.Bundles[1].Dependencies.Add("hero");
        Expect(manifest.ValidateForActivation("PC", "official") != null, "依赖环拒绝");
        manifest.Bundles[1].Dependencies.Clear();
        Expect(manifest.ValidateForActivation("PC", "official") == null, "依赖闭包通过");
        manifest.Bundles.RemoveAt(1);
        manifest.Bundles[0].Dependencies.Clear();

        manifest.Bundles[0].Name = "../hero";
        Expect(manifest.ValidateForActivation("PC", "official") != null, "路径穿越拒绝");
        manifest.Bundles[0].Name = "hero";
        manifest.Bundles.Add(NewBundle("HERO", 'd'));
        Expect(manifest.ValidateForActivation("PC", "official") != null, "大小写重复名拒绝");
        manifest.Bundles.RemoveAt(1);
        manifest.Bundles[0].Sha256 = "";
        Expect(manifest.ValidateForActivation("PC", "official") != null, "空哈希拒绝");
        manifest.Bundles[0].Sha256 = new string('b', 64);
        manifest.HashAlgo = "md5";
        Expect(manifest.ValidateForActivation("PC", "official") != null, "不支持的校验算法拒绝");
        manifest.HashAlgo = "sha256";
        manifest.ResVersion = "../escape";
        Expect(manifest.ValidateForActivation("PC", "official") != null, "版本号路径穿越拒绝");
        manifest.ResVersion = "1.0.0.1";

        string text = manifest.Serialize();
        RevHotManifest parsed = RevHotManifest.Parse(text, out string error);
        Expect(parsed != null && error == null && parsed.ValidateForActivation("PC", "official") == null, "序列化往返验证");
        Expect(RevHotManifest.Parse(text.Replace("@bundleCount|1", "@bundleCount|2"), out _) == null, "截断清单拒绝");
        manifest.Platform = "WebGL";
        Expect(manifest.ValidateForActivation("WebGL", "official") != null, "WebGL 缺 Hash128 拒绝");
        manifest.Bundles[0].UnityHash = new string('f', 32);
        manifest.Bundles[0].UnityCrc = "1234";
        Expect(manifest.ValidateForActivation("WebGL", "official") == null, "WebGL 有效 Hash128/CRC 通过");
        Console.WriteLine($"RevHotUpdate manifest offline: {_passed} assertions passed");
    }

    static RevHotBundleInfo NewBundle(string name, char hash)
        => new() { Name = name, Sha256 = new string(hash, 64), Size = 10 };

    static RevHotManifest NewManifest()
    {
        var manifest = new RevHotManifest { AppVersion = "1.0.0", ResVersion = "1.0.0.1", Platform = "PC", Channel = "official" };
        manifest.ResMap = new RevHotFileInfo { Name = "ResMap.txt", Sha256 = new string('a', 64), Size = 13 };
        manifest.Bundles.Add(NewBundle("hero", 'b'));
        return manifest;
    }

    static void Expect(bool result, string message)
    {
        if (!result) throw new Exception("断言失败：" + message);
        _passed++;
    }
}
