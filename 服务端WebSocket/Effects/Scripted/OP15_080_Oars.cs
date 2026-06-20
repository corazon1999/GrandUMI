using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-080 奥兹（4 费 0 力量，巨人族/恐怖之船海盗团）
///
/// 卡面效果：
///   1. 我方场上存在力量为 10000 或更高的“月光·莫利亚”，且没有其他“奥兹”的场合，此角色的力量+7000。
///      （持续力量修正 → ContinuousEffect 注册，Predicate 按当前局面实时判定。）
///   2. 【KO时】可以将我方废弃区中的 3 张卡牌自选顺序放回卡组最下方：从废弃区中登场此角色卡牌。
///
/// 实现说明 / 简化点：
/// - 持续 +7000：仅作用于自身（card.Id == selfId）；条件为我方场上存在当前力量≥10000 的“月光·莫利亚”，
///   且我方场上不存在除自身外的其他“奥兹”。用 ContinuousEffect.Predicate 实时评估。
/// - 【KO时】此卡已位于我方废弃区。可选成本=由玩家选择废弃区 3 张放回卡组最下方（自选顺序，
///   实现为按所选顺序放底）。成本支付完成后，从废弃区登场自身（PlayFromTrashFree）。
///   废弃区不足 3 张则无法支付成本，效果不发动。
/// </summary>
public class OP15_080_Oars : IScriptedEffect
{
    public string CardNumber => "OP15-080";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var s = ctx.State;
        var me = s.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int owner = ctx.OwnerIndex;
        var selfId = self.Id;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 持续 +7000：场上存在力量≥10000 的“月光·莫利亚”且无其他“奥兹”
            s.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
            s.ContinuousEffects.Add(new ContinuousEffect
            {
                SourceCardId = selfId.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                PowerDelta = 7000,
                Predicate = (st, sideIdx, card) =>
                {
                    if (card.Id != selfId) return false;
                    var p = st.Players[owner];
                    bool hasBigMoria = p.Characters.Any(c =>
                        c.MatchesName("月光·莫利亚") && st.CurrentPowerOf(owner, c) >= 10000);
                    bool noOtherOars = !p.Characters.Any(c =>
                        c.Id != selfId && c.MatchesName("奥兹"));
                    return hasBigMoria && noOtherOars;
                },
            });
            return;
        }

        // 【KO时】（自身已在废弃区）
        // 需要废弃区另有 3 张可放回（不含自身）
        var trashOthers = me.Trash.Where(c => c.Id != selfId).ToList();
        if (trashOthers.Count < 3) return;

        bool use = await ctx.Prompts.ConfirmOptional(owner,
            "奥兹【KO时】：将废弃区 3 张卡牌放回卡组最下方，从废弃区登场此角色？");
        if (!use) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = trashOthers.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var picks = await ctx.Prompts.ChooseCards(owner, "OwnTrash",
            "将废弃区的 3 张卡牌放回卡组最下方（自选顺序）",
            trashOthers.Select(c => c.Id.ToString()).ToList(), 3, 3, extra);
        if (picks.Count < 3) return; // 成本未支付完成

        // 成本：按所选顺序放回卡组最下方
        foreach (var id in picks)
        {
            var card = trashOthers.FirstOrDefault(c => c.Id.ToString() == id);
            if (card is not null) AtomicOps.ReturnTrashToDeckBottom(me, card);
        }

        // 收益：从废弃区登场此角色
        if (me.Trash.Any(c => c.Id == selfId))
        {
            AtomicOps.PlayFromTrashFree(s, owner, self, restState: false);
        }
    }
}
