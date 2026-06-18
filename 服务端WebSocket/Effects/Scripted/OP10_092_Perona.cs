using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-092 佩罗娜（角色 / 紫(地) / 4 费 / 5000 / 恐怖之船海盗团）
/// 【启动主要】【每回合1次】可以将我方废弃区中 2 张拥有《恐怖之船海盗团》特征的卡牌自选顺序
///   放回卡组最下方：本回合中，我方最多 1 张"佩罗娜"以外的角色力量 +2000。
///
/// 说明 / 简化点：
///   - 成本：废弃区选 2 张《恐怖之船海盗团》卡牌放回卡组最下方（DSL cost 节无该键，故脚本实现）。
///     "自选顺序放底"实现为按选择顺序放底（对实战影响极小）。
///   - 收益：我方最多 1 张"佩罗娜"以外的角色本回合 +2000。
///   - 【每回合1次】用 me.TurnOnceUsed 去重。
/// </summary>
public class OP10_092_Perona : IScriptedEffect
{
    public string CardNumber => "OP10-092";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        var key = self.Info.Number + "-act" + ":" + self.Id;
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本候选：废弃区《恐怖之船海盗团》卡牌
        var trashCands = me.Trash.Where(c => c.Info.HasKeyword("恐怖之船海盗团")).ToList();
        if (trashCands.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "佩罗娜【启动主要】：废弃区 2 张《恐怖之船海盗团》放回卡组最下方，使我方最多 1 张非佩罗娜角色本回合 +2000？");
        if (!use) return;

        // 选 2 张废弃区《恐怖之船海盗团》卡牌
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = trashCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var picked = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "TrashCards",
            "选择废弃区 2 张《恐怖之船海盗团》卡牌放回卡组最下方",
            trashCands.Select(c => c.Id.ToString()).ToList(), 2, 2, extra);
        if (picked.Count < 2) return;

        // 支付成本：按选择顺序放回卡组最下方
        foreach (var id in picked)
        {
            var card = trashCands.First(c => c.Id.ToString() == id);
            AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        // 收益：我方最多 1 张"佩罗娜"以外角色本回合 +2000
        var benefitCands = me.Characters.Where(c => !c.MatchesName("佩罗娜")).ToList();
        if (benefitCands.Count == 0) return;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择我方最多 1 张非佩罗娜角色，本回合 +2000",
            benefitCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
        me.TurnOnceUsed.Add(key);
        if (chosen.Count == 0) return;
        var tgt = benefitCands.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AddPowerThisTurn(tgt, 2000);
    }
}
