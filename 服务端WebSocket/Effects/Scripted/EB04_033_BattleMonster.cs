using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-033 武斗怪物（角色 / 暗 / 巨人族・鱼人族・福克斯海盗团 / cost5 power6000）
/// 【登场时】咚!!-1：我方场上存在3张或更多拥有《福克斯海盗团》特征的角色的场合，
///   将对方最多1张原本的力量不高于6000的角色KO。
///
/// 实现说明：
///   - 成本咚!!-1（费用区需有≥1 张可放回的活跃/休息咚）。
///   - 条件：我方场上（角色）≥3 张含《福克斯海盗团》特征（含自身，自身已登场计入）。
///   - "原本的力量不高于6000"取卡面原始力量 c.Info.Power（不含加成）。
/// </summary>
public class EB04_033_BattleMonster : IScriptedEffect
{
    public string CardNumber => "EB04-033";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (me.CostArea.Count < 1) return; // 无法支付成本 咚!!-1

        // 成本：咚!!-1（强制成本，发动本效果即支付）
        if (!await AtomicOps.PromptReturnDonToDeck(ctx, 1)) return;

        // 条件：我方场上≥3 张《福克斯海盗团》角色
        int foxy = me.Characters.Count(c => c.Info.HasKeyword("福克斯海盗团"));
        if (foxy < 3) return;

        // 效果：KO 对方最多 1 张原本力量≤6000 的角色
        var cands = opp.Characters.Where(c => c.Info.Power <= 6000).ToList();
        if (cands.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "将对方最多 1 张原本力量≤6000 的角色KO",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (chosen.Count > 0)
        {
            var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
        }
    }
}
