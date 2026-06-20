using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP03-015 莉姆（角色 / 炎 3 费 2000，时光旅诗）
/// 【阻挡者】（关键词，由引擎处理）
/// 【对方的回合中】当此角色被KO时，本回合中，对方最多1张领袖或角色力量-2000。
///
/// 实现说明：
///   - OnKO（此卡被KO时触发，ctx.Source 即本卡）。仅在对方回合中生效。
///   - 选择对方最多1张领袖或角色，本回合力量-2000（AddPowerThisTurn 负值）。
/// </summary>
public class OP03_015_Lim : IScriptedEffect
{
    public string CardNumber => "OP03-015";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 仅在对方的回合中
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;

        var targets = new List<CardInstance> { opp.Leader };
        targets.AddRange(opp.Characters);

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentLeaderOrCharacter",
            "选择对方最多1张领袖或角色，本回合力量-2000",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count > 0)
        {
            var tgt = targets.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.AddPowerThisTurn(tgt, -2000);
        }
    }
}
