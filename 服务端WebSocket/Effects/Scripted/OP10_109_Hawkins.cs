using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-109 巴基尔·霍金斯（角色）
/// 【KO时】将对方生命区最上方的最多 1 张卡牌放置到废弃区。
///   （【触发】部分由引擎在生命触发流程中处理，此处仅实现【KO时】主效果。）
///
/// 实现说明：
///   - "对方生命顶 → 废弃区"无对应 AtomicOps，直接将对方 LifeArea[0] 移入对方 Trash。
///   - "最多 1 张"且对方有可选，按文本以 ConfirmOptional 询问是否执行（无收益时可放弃）。
/// </summary>
public class OP10_109_Hawkins : IScriptedEffect
{
    public string CardNumber => "OP10-109";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        if (opp.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "霍金斯【KO时】：将对方生命区最上方的 1 张卡牌放置到废弃区？");
        if (!use) return;

        var top = opp.LifeArea[0];
        opp.LifeArea.RemoveAt(0);
        opp.Trash.Add(top);
    }
}
