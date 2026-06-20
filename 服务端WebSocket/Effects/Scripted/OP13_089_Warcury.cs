using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-089 托普曼·沃丘利圣（4 费 5000，天龙人/五老星）
/// 原文：
///   我方废弃区中有 7 张或更多卡牌的场合，此角色不会因对方的效果而离场，并获得【阻挡者】效果。
///   【KO时】抽取 1 张卡牌。
///
/// 本脚本仅实现【KO时】抽取 1 张（OnKO 触发）。
///
/// 简化/未实现（引擎缺口，见 summary）：
///   前半段"废弃区≥7 时不会因对方效果离场 + 获得【阻挡者】"是带条件的持续静态能力：
///   - 引擎暂无"按废弃区张数动态开关静态免疫/关键字"的通道；
///   - 且免疫的是所有"因对方效果离场"（KO/回手/回卡组等），PreKO 钩子只能拦 KO 且无法区分
///     战斗 KO 与效果 KO；
///   - 另外引擎对【阻挡者】是按卡面文本 EffectText 包含"【阻挡者】"恒定授予的，无法按条件门控。
///   因此这部分静态能力未实现，仅实现【KO时】抽 1。
/// </summary>
public class OP13_089_Warcury : IScriptedEffect
{
    public string CardNumber => "OP13-089";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        // 【KO时】抽取 1 张卡牌
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        await Task.CompletedTask;
    }
}
