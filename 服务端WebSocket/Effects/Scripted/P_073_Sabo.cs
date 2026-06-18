using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-073 萨波（角色 / 光 5 费 5000 / 德莱斯罗兹・革命军）
/// 【启动主要】【每回合1次】可以将我方生命区最上方或最下方的1张卡牌加入手牌：
///   本回合中，此角色的力量+1000。
///
/// 实现说明：
///   - 每回合1次用 TurnOnceUsed 去重。
///   - 成本：将我方生命区最上方或最下方 1 张卡牌加入手牌（"可以"型可选，需生命区 ≥1 张）。
///     由玩家选择取生命顶/底；仅 1 张时直接取该张。
///   - 收益：本回合此角色力量 +1000（AddPowerThisTurn）。
/// </summary>
public class P_073_Sabo : IScriptedEffect
{
    public string CardNumber => "P-073";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本需要生命区至少 1 张
        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "萨波【启动主要】：将生命区顶/底 1 张加入手牌，本回合此角色力量+1000？");
        if (!use) return;

        // 成本：生命顶或底 1 张入手
        int which = me.LifeArea.Count == 1
            ? 0
            : await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "将哪一张生命牌加入手牌？",
                new[] { "生命区最上方", "生命区最下方" });
        var lifeCard = which == 0 ? me.LifeArea[0] : me.LifeArea[^1];
        me.LifeArea.Remove(lifeCard);
        me.Hand.Add(lifeCard);

        me.TurnOnceUsed.Add(key);

        // 收益：本回合此角色力量 +1000
        AtomicOps.AddPowerThisTurn(self, 1000);
    }
}
