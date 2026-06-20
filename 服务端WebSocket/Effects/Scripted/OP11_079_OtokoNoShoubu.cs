using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-079 男人之间的胜负……!!!别擅自提供这种肤浅的援助啊!!（事件）
/// 【反击】宣言任意的费用，并公开对方卡组最上方的 1 张卡牌。
///   公开的卡牌费用与宣言的费用相同的场合，本次战斗中，我方最多 1 张领袖或角色力量 +5000。
///
/// 说明 / 简化点：
/// - "宣言任意的费用"实现为让玩家在 0~10 中选择一个数字（ChooseOption 返回下标即数值）。
/// - "公开对方卡组最上方 1 张"读取对方 Deck[0]，比较其卡面原本费用 Info.Cost。
/// - 命中则让玩家选择我方 1 张领袖或角色，本次战斗中力量 +5000。
/// </summary>
public class OP11_079_OtokoNoShoubu : IScriptedEffect
{
    public string CardNumber => "OP11-079";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 宣言任意费用（0~10）
        var options = new List<string>();
        for (int i = 0; i <= 10; i++) options.Add(i.ToString());
        int declared = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "宣言任意的费用", options);

        // 公开对方卡组最上方 1 张
        if (opp.Deck.Count == 0) return;
        var revealed = opp.Deck[0];

        // 命中：公开卡的原本费用 == 宣言费用
        if (revealed.Info.Cost != declared) return;

        // 本次战斗中，我方最多 1 张领袖或角色力量 +5000
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择我方最多 1 张领袖或角色，本次战斗中力量 +5000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisBattle(tgt, 5000);
        }
    }
}
