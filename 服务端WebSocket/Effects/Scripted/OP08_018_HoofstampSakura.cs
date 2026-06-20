using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-018 刻蹄『樱花』（事件）
/// 【主要】本回合中，我方最多 3 张角色力量 +1000。之后，本回合中，对方最多 1 张角色力量 -2000。
/// 【触发】本回合中，对方最多 1 张领袖或角色力量 -3000。
///
/// 实现说明：
///   - 【主要】"最多 3 张角色各 +1000"：一次性选择最多 3 张我方角色（min0 max3），逐张 +1000(本回合)。
///   - 之后选对方最多 1 张角色 -2000(本回合)。
///   - 【触发】选对方最多 1 张领袖或角色 -3000(本回合)。
/// </summary>
public class OP08_018_HoofstampSakura : IScriptedEffect
{
    public string CardNumber => "OP08-018";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.EventMain || t == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me  = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            // 本回合中，我方最多 3 张角色力量 +1000
            var myChars = me.Characters.ToList();
            if (myChars.Count > 0)
            {
                var picks = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                    "本回合中，选择我方最多 3 张角色 +1000",
                    myChars.Select(c => c.Id.ToString()).ToList(), 0, 3);
                foreach (var id in picks)
                {
                    var tgt = myChars.FirstOrDefault(c => c.Id.ToString() == id);
                    if (tgt != null) AtomicOps.AddPowerThisTurn(tgt, 1000);
                }
            }

            // 之后，本回合中，对方最多 1 张角色力量 -2000
            var oppChars = opp.Characters.ToList();
            if (oppChars.Count > 0)
            {
                var dec = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                    "本回合中，选择对方最多 1 张角色 -2000",
                    oppChars.Select(c => c.Id.ToString()).ToList(), 0, 1);
                if (dec.Count > 0)
                {
                    var tgt = oppChars.First(c => c.Id.ToString() == dec[0]);
                    AtomicOps.AddPowerThisTurn(tgt, -2000);
                }
            }
            return;
        }

        // 【触发】本回合中，对方最多 1 张领袖或角色力量 -3000
        var targets = new List<CardInstance> { opp.Leader };
        targets.AddRange(opp.Characters);
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "本回合中，选择对方最多 1 张领袖或角色 -3000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.AddPowerThisTurn(tgt, -3000);
        }
    }
}
