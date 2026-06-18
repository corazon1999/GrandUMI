namespace GrandUMI.Cards;

/// <summary>
/// 静态卡牌信息（从 JSON 加载，对战中不可变）
/// 字段对应 卡牌数据/*.json 的结构
/// </summary>
public class CardInfo
{
    public required string Number    { get; init; }   // "OP15-001"
    public required string Name      { get; init; }
    public required string Color     { get; init; }   // "红" / "红/蓝" 等
    public required CardKind Kind    { get; init; }
    public required string Property  { get; init; }   // "斩" / "打" / ...
    public int Power                 { get; init; }   // 0 = 无
    public int Cost                  { get; init; }   // 领航 cost 对应生命数
    public string[] Keywords         { get; init; } = Array.Empty<string>();
    public int Counter               { get; init; }   // 0 = 无
    /// <summary>该卡响应的触发时机枚举名（如 "OnEnterField"）。由卡牌数据预计算，替代旧的卡面原文扫描。</summary>
    public string[] EffectTags       { get; init; } = Array.Empty<string>();
    /// <summary>卡面能力关键字 + 攻击限制（阻挡者/速攻/双重攻击/可攻击活跃/不可阻挡/流放/此角色无法攻击）。</summary>
    public string[] Abilities        { get; init; } = Array.Empty<string>();
    public string Trigger            { get; init; } = "";
    public string Rarity             { get; init; } = "";

    /// <summary>单人测试模式专用：为 true 时 HasKeyword 一律返回 true（特征通配）。仅克隆副本设置，不污染共享缓存实例。</summary>
    public bool AllKeywordsWildcard  { get; init; }

    /// <summary>单人测试模式专用：为 true 时 NameContains/NameIs 一律返回 true（名称通配）。仅领袖克隆副本设置，不污染共享缓存实例。</summary>
    public bool NameWildcard         { get; init; }

    /// <summary>所属卡集，例如 "OP15" / "ST01"</summary>
    public string SetCode => Number.Split('-')[0];

    /// <summary>颜色集合（多色卡按 / 拆分）</summary>
    public string[] ColorList => Color.Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>判断与另一张卡（通常是领航）是否有颜色交集</summary>
    public bool SharesColorWith(CardInfo other)
    {
        var mine = ColorList;
        foreach (var c in other.ColorList)
            foreach (var m in mine)
                if (m == c) return true;
        return false;
    }

    /// <summary>是否拥有指定特征（特征字符串完全匹配）</summary>
    public bool HasKeyword(string kw)
    {
        if (AllKeywordsWildcard) return true;
        foreach (var k in Keywords) if (k == kw) return true;
        return false;
    }

    /// <summary>名称是否包含指定子串。单人测试模式领袖（NameWildcard）一律返回 true，便于测任意"领袖名为X"的卡。</summary>
    public bool NameContains(string s)
    {
        if (NameWildcard) return true;
        return Name.Contains(s);
    }

    /// <summary>名称是否等于指定字符串。单人测试模式领袖（NameWildcard）一律返回 true。</summary>
    public bool NameIs(string s)
    {
        if (NameWildcard) return true;
        return Name == s;
    }

    /// <summary>返回「所有特征判定 + 名称判定均为 true」的副本（单人测试模式领袖用，不污染共享缓存实例）</summary>
    public CardInfo WithWildcardKeywords() => new CardInfo
    {
        Number = Number, Name = Name, Color = Color, Kind = Kind, Property = Property,
        Power = Power, Cost = Cost, Keywords = Keywords, Counter = Counter,
        EffectTags = EffectTags, Abilities = Abilities, Trigger = Trigger, Rarity = Rarity,
        AllKeywordsWildcard = true,
        NameWildcard = true,
    };
}

public enum CardKind
{
    Leader,
    Character,
    Event,
    Stage,
    Don,
}
