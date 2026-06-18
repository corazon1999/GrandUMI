using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-042 一本松（角色 / 水 / 东海）
/// 【登场时】本回合中，我方最多 1 张拥有属性（斩）的角色力量+3000。
///   之后，将我方卡组最上方的 1 张卡牌放置到废弃区。
///
/// 实现说明：
///   - 属性（斩）= CardInfo.Property == "斩"，按属性过滤候选角色。
///   - 选 1 张 +3000（AddPowerThisTurn）。
///   - 之后 MillTop(1) 将卡组顶 1 张放入废弃区。
/// </summary>
public class OP04_042_Ipponmatsu : IScriptedEffect
{
    public string CardNumber => "OP04-042";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 第一段：我方最多 1 张属性（斩）角色 +3000
        var cands = me.Characters.Where(c => c.Info.Property == "斩").ToList();
        if (cands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
                "本回合中，我方最多 1 张属性（斩）角色力量+3000",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.AddPowerThisTurn(tgt, 3000);
            }
        }

        // 第二段：将卡组最上方 1 张放置到废弃区
        AtomicOps.MillTop(me, 1);
    }
}
