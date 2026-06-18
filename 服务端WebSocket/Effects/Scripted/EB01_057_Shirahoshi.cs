using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB01-057 白星（角色 / 光 / 人鱼族，cost2 power0）
/// 当此角色因对方的效果而被KO时，将我方卡组最上方的最多 1 张卡牌加入生命区最上方。
/// 【阻挡者】（关键词，引擎处理）
///
/// 实现说明（M6 效果KO来源判定）：
///   - 走 OnKO；引擎的效果KO（KOByEffectAsync）已设 s.KOReason="effect"、s.KOActingSide=发起方，
///     据此判定"因对方的效果"。战斗KO时 KOReason 非 "effect"，正确地不发动。
///   - 收益：AddLifeFromDeckTop(1)。
///   - 局限：脚本直接同步KO（非DSL）不派发 OnKO，本卡此情形不发动（已文档化）。
/// </summary>
public class EB01_057_Shirahoshi : IScriptedEffect
{
    public string CardNumber => "EB01-057";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public Task Resolve(EffectContext ctx)
    {
        // 仅"因对方的效果而被KO"
        if (ctx.State.KOReason == "effect" && ctx.State.KOActingSide == 1 - ctx.OwnerIndex)
        {
            var me = ctx.State.Players[ctx.OwnerIndex];
            AtomicOps.AddLifeFromDeckTop(me, 1);
        }
        return Task.CompletedTask;
    }
}
