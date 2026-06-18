using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-091 蒙奇·D·路飞（角色 / 草帽一伙）
/// 【攻击时】将对方最多 1 张费用不高于 2 的角色放置到废弃区（KO）。
///   之后，将我方废弃区中任意张数的费用为 4 或更高的角色卡牌自选顺序放回卡组最下方。
///   每放回 3 张，本回合中，此角色的力量 +1000。
///
/// 说明 / 简化点：
///   - "放置到废弃区"即 KO 对方所选角色（最多 1 张，费用≤2）。
///   - "任意张数"实现为玩家从废弃区费用≥4 的角色中自选 0~N 张放回卡组最下方；
///     "自选顺序"按所选顺序放底（与玩家选择顺序一致）。
///   - 每放回满 3 张给此角色本回合 +1000（floor(count/3) * 1000）。
/// </summary>
public class OP07_091_Luffy : IScriptedEffect
{
    public string CardNumber => "OP07-091";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        var self = ctx.Source;

        // ── 效果 1：KO 对方最多 1 张费用≤2 的角色 ──
        var koCands = opp.Characters.Where(c => c.Info.Cost <= 2).ToList();
        if (koCands.Count > 0)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "将对方最多 1 张费用≤2 的角色放置到废弃区",
                koCands.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (chosen.Count > 0)
            {
                var tgt = koCands.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, tgt);
            }
        }

        // ── 效果 2：将废弃区任意张费用≥4 的角色放回卡组最下方 ──
        var trashCands = me.Trash.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.Cost >= 4).ToList();
        if (trashCands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = trashCands
                .Select(c => new { id = c.Id.ToString(), number = c.Info.Number })
                .ToList(),
        };
        var picked = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnTrashCharacter",
            "将废弃区中任意张数费用≥4 的角色自选顺序放回卡组最下方",
            trashCands.Select(c => c.Id.ToString()).ToList(), 0, trashCands.Count, extra);

        int returned = 0;
        foreach (var cid in picked)
        {
            var card = trashCands.FirstOrDefault(c => c.Id.ToString() == cid);
            if (card is not null)
            {
                AtomicOps.ReturnTrashToDeckBottom(me, card);
                returned++;
            }
        }

        // 每放回 3 张，本回合此角色 +1000
        int bonus = (returned / 3) * 1000;
        if (bonus > 0) AtomicOps.AddPowerThisTurn(self, bonus);
    }
}
