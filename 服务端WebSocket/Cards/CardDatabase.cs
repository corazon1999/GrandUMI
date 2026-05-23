using System.Text.Json;

namespace GrandUMI.Cards;

/// <summary>
/// 全局卡牌数据库（启动时一次性加载，运行时只读）
/// 数据源：项目根目录下的 卡牌数据/*.json
/// </summary>
public static class CardDatabase
{
    private static readonly Dictionary<string, CardInfo> _byNumber = new();
    private static readonly Dictionary<string, List<CardInfo>> _bySet = new();

    public static int Count => _byNumber.Count;

    /// <summary>启动时调用，从指定目录加载所有卡集</summary>
    public static void LoadFrom(string cardDataRoot)
    {
        if (!Directory.Exists(cardDataRoot))
            throw new DirectoryNotFoundException($"卡牌数据目录不存在: {cardDataRoot}");

        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var file in Directory.GetFiles(cardDataRoot, "*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName.StartsWith("_")) continue; // 跳过 _index.json

            try
            {
                using var fs = File.OpenRead(file);
                var raw = JsonSerializer.Deserialize<List<RawCard>>(fs, jsonOpts);
                if (raw is null) continue;

                foreach (var r in raw)
                {
                    var info = MapToCardInfo(r);
                    if (info is null) continue;
                    _byNumber[info.Number] = info;

                    if (!_bySet.TryGetValue(info.SetCode, out var list))
                    {
                        list = new List<CardInfo>();
                        _bySet[info.SetCode] = list;
                    }
                    list.Add(info);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CardDB] 加载 {Path.GetFileName(file)} 失败: {ex.Message}");
            }
        }

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
        public string? effectText  { get; set; }
        public string? trigger     { get; set; }
        public string? rarity      { get; set; }
    }

    private static CardInfo? MapToCardInfo(RawCard r)
    {
        if (string.IsNullOrEmpty(r.number) || string.IsNullOrEmpty(r.name)) return null;

        int.TryParse(r.power,   out int power);
        int.TryParse(r.cost,    out int cost);
        int.TryParse(r.counter, out int counter);

        var keywords = string.IsNullOrEmpty(r.keyWords)
            ? Array.Empty<string>()
            : r.keyWords.Split(new[] { '/', ',', '，' }, StringSplitOptions.RemoveEmptyEntries);

        var kind = (r.type ?? "") switch
        {
            "领航" or "领袖" => CardKind.Leader,
            "角色"           => CardKind.Character,
            "事件"           => CardKind.Event,
            "舞台"           => CardKind.Stage,
            _                 => CardKind.Character,
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
            EffectText = r.effectText ?? "",
            Trigger    = r.trigger ?? "",
            Rarity     = r.rarity ?? "",
        };
    }
}
