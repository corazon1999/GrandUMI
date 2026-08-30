using System.Text.Json;
using System.Text;
using GrandUMI.Training;

namespace GrandUMI.Cards;

/// <summary>
/// 全局卡牌数据库（启动时一次性加载，运行时只读）
/// 数据源：项目根目录下的 卡牌数据/*.json
/// </summary>
public static class CardDatabase
{
    private static readonly Dictionary<string, CardInfo> _byNumber = new();
    private static readonly Dictionary<string, List<CardInfo>> _bySet = new();
    private static readonly object _loadLock = new();
    private static bool _loaded;

    public static int Count => _byNumber.Count;
    /// <summary>启动时一次性计算的“规范相对路径 + 原始文件字节”SHA-256。</summary>
    public static string ContentHash { get; private set; } = string.Empty;

    /// <summary>
    /// 加载所有卡集。幂等且线程安全：首次加载后重复调用直接返回。
    /// （生产启动只调一次；多处/并发调用如单测，靠此保证不会并发写同一字典而损坏。）
    /// </summary>
    public static void LoadFrom(string cardDataRoot)
    {
        if (_loaded) return;
        lock (_loadLock)
        {
            if (_loaded) return;
            LoadFromCore(cardDataRoot);
            _loaded = true;
        }
    }

    private static void LoadFromCore(string cardDataRoot)
    {
        if (!Directory.Exists(cardDataRoot))
            throw new DirectoryNotFoundException($"卡牌数据目录不存在: {cardDataRoot}");

        var manifest = CardContentManifest.Validate(cardDataRoot);
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var files = manifest.Files.Select(file => Path.Combine(cardDataRoot, file)).ToArray();
        // 哈希只在启动加载阶段遍历磁盘；新建房间和逐动作路径只读取缓存字符串。
        var contentHash = ReplayContentManifest.HashFiles(cardDataRoot, files);
        var byNumber = new Dictionary<string, CardInfo>(StringComparer.OrdinalIgnoreCase);
        var bySet = new Dictionary<string, List<CardInfo>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            using var fs = File.OpenRead(file);
            var raw = JsonSerializer.Deserialize<List<RawCard>>(fs, jsonOpts)
                ?? throw new InvalidDataException($"卡牌数据文件为空：{Path.GetFileName(file)}");

            for (var index = 0; index < raw.Count; index++)
            {
                var info = MapToCardInfo(raw[index])
                    ?? throw new InvalidDataException($"卡牌必填字段无效：{Path.GetFileName(file)}[{index}]");
                if (!byNumber.TryAdd(info.Number, info))
                    throw new InvalidDataException($"卡牌番号重复：{info.Number}");
                if (!bySet.TryGetValue(info.SetCode, out var list))
                    bySet[info.SetCode] = list = new List<CardInfo>();
                list.Add(info);
            }
        }

        if (byNumber.Count != manifest.TotalCards)
            throw new InvalidDataException($"卡牌唯一番号数 {byNumber.Count} 与清单总数 {manifest.TotalCards} 不一致");
        _byNumber.Clear();
        _bySet.Clear();
        foreach (var (number, card) in byNumber) _byNumber.Add(number, card);
        foreach (var (set, cards) in bySet) _bySet.Add(set, cards);
        ContentHash = contentHash;

        Console.WriteLine($"[CardDB] 已加载 {_byNumber.Count} 张卡，{_bySet.Count} 个卡集");
    }

    public static CardInfo? Get(string number)
        => _byNumber.TryGetValue(number, out var c) ? c : null;

    public static IReadOnlyList<CardInfo> GetBySet(string setCode)
        => _bySet.TryGetValue(setCode, out var list)
            ? list
            : (IReadOnlyList<CardInfo>)Array.Empty<CardInfo>();

    public static bool Exists(string number) => _byNumber.ContainsKey(number);

    // ── 内部转换 ──────────────────────────────────────────────────────────

    private class RawCard
    {
        public string? number      { get; set; }
        public string? name        { get; set; }
        public string? color       { get; set; }
        public string? type        { get; set; }
        public string? property    { get; set; }
        public string? power       { get; set; }
        public string? cost        { get; set; }
        public string? keyWords    { get; set; }
        public string? counter     { get; set; }
        public string[]? effectTags { get; set; }
        public string[]? abilities { get; set; }
        public string? trigger     { get; set; }
        public string? rarity      { get; set; }
        public JsonElement subscript { get; set; }
        public string[]? alsoNames { get; set; }
    }

    private static CardInfo? MapToCardInfo(RawCard r)
    {
        if (string.IsNullOrEmpty(r.number) || string.IsNullOrEmpty(r.name)) return null;

        // 历史卡表偶有全角数字（如 OP08-044 的“４”）；兼容性归一化后再解析，
        // 避免合法费用被静默解析为 0。
        int.TryParse(r.power?.Normalize(NormalizationForm.FormKC), out int power);
        int.TryParse(r.cost?.Normalize(NormalizationForm.FormKC),  out int cost);
        // counter 字段可能是 "反击+1000" 这类带文字前缀的格式，需抽取数字（与客户端解析一致）
        int counter = ParseCounterValue(r.counter);

        var keywords = string.IsNullOrEmpty(r.keyWords)
            ? Array.Empty<string>()
            : r.keyWords.Split(new[] { '/', ',', '，' }, StringSplitOptions.RemoveEmptyEntries);

        var kind = (r.type ?? "") switch
        {
            "领航" or "领袖" => CardKind.Leader,
            "角色"           => CardKind.Character,
            "事件"           => CardKind.Event,
            "舞台"           => CardKind.Stage,
            _                 => throw new InvalidDataException($"未知卡牌类型：{r.number}={r.type}"),
        };

        return new CardInfo
        {
            Number     = r.number,
            Name       = r.name,
            Color      = r.color ?? "",
            Kind       = kind,
            Property   = r.property ?? "",
            Power      = power,
            Cost       = cost,
            Keywords   = keywords,
            Counter    = counter,
            EffectTags = r.effectTags ?? Array.Empty<string>(),
            Abilities  = r.abilities ?? Array.Empty<string>(),
            Trigger    = r.trigger ?? "",
            Rarity     = r.rarity ?? "",
            Subscript  = ParseSubscript(r.subscript),
            AlsoNames  = r.alsoNames ?? Array.Empty<string>(),
        };
    }

    /// <summary>解析反击值：兼容 "1000"、"+1000"、"反击+1000" 等格式，抽取首个数字串</summary>
    private static int ParseCounterValue(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return 0;
        var m = System.Text.RegularExpressions.Regex.Match(raw, @"\d+");
        return m.Success ? int.Parse(m.Value) : 0;
    }

    private static int ParseSubscript(JsonElement raw)
    {
        if (raw.ValueKind == JsonValueKind.Number && raw.TryGetInt32(out var number)) return number;
        if (raw.ValueKind == JsonValueKind.String && int.TryParse(raw.GetString(), out number)) return number;
        return 0;
    }
}
