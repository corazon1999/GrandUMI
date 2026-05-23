namespace GrandUMI.Cards;

/// <summary>
/// 静态卡牌信息（从 JSON 加载，对战中不可变）
/// 字段对应 卡牌数据/*.json 的结构
/// </summary>
public class CardInfo
{
    public required string Number    { get; init; }   // "OP15-001"
    public required string Name      { get; init; }
    public required string Color     { get; init; }   // "炎" / "炎/水" 等
    public required CardKind Kind    { get; init; }
    public required string Property  { get; init; }   // "斩" / "打" / ...
    public int Power                 { get; init; }   // 0 = 无
    public int Cost                  { get; init; }   // 领航 cost 对应生命数
    public string[] Keywords         { get; init; } = Array.Empty<string>();
    public int Counter               { get; init; }   // 0 = 无
    public string EffectText         { get; init; } = "";
    public string Trigger            { get; init; } = "";
    public string Rarity             { get; init; } = "";

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
        foreach (var k in Keywords) if (k == kw) return true;
        return false;
    }
}

public enum CardKind
{
    Leader,
    Character,
    Event,
    Stage,
    Don,
}
