using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP02-069 DEATH WINK（事件 / 水 / 因佩尔地狱・革命军，cost3）
/// 【反击】本次战斗中，我方最多 1 张领袖或角色力量 +6000。
///   之后，抽取卡牌直到我方手牌变为 2 张。
/// 【触发】将最多 1 张费用不高于 7 的角色放回其持有者的手牌。（触发由引擎/DSL处理，此处仅实现反击）
///
/// 实现说明：
///   - 反击=EventCounter；力量 +6000 限定本次战斗 → AddPowerThisBattle。
///   - "抽取卡牌直到手牌变为 2 张"为动态张数：运行时按 2 - 当前手牌数 计算 n（≤0 则不抽）。
///   - 选择 +6000 目标用 ChooseCards（领袖+我方角色，最多 1 张，场上卡可不传 choiceCards）。
/// </summary>
public class OP02_069_DeathWink : IScriptedEffect
{
    public string CardNumber => "OP02-069";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 本次战斗中，我方最多 1 张领袖或角色 +6000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择最多 1 张领袖或角色，本次战斗中力量 +6000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisBattle(tgt, 6000);
        }

        // 之后：抽取卡牌直到我方手牌变为 2 张（动态张数）
        int need = 2 - me.Hand.Count;
        if (need > 0) AtomicOps.Draw(ctx.State, ctx.OwnerIndex, need);
    }
}
