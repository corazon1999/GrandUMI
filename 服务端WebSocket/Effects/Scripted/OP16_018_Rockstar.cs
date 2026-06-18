using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-018 罗库斯塔（角色 / 炎・斩 / 力量2000 / 红发海盗团）
/// 【每回合1次】我方拥有《红发海盗团》特征的角色将要被KO的场合，
///   可以改为丢弃我方手牌中1张力量为6000或更高的角色卡牌，使该角色不会被KO。
///
/// 实现说明：
///   - 同时处理 PreKO（自身将被KO）与 OnAllyWillBeKOd（其他我方角色将被KO）两个守护时机，
///     与卡牌数据 effectTags 一致；二者均由 BattleEngine.KOCardAsync 派发（含 GM「KO 全部」）。
///   - 被守护角色须拥有《红发海盗团》特征（自身亦满足，故 PreKO 时不必特例）。
///   - 成本：丢弃 1 张手牌中【角色】且印刷力量≥6000 的卡；无合法弃牌则无法发动（不弹提示）。
///   - 发动后 ctx.State.MarkPreventKO(victim.Id) 取消本次 KO（KOCardAsync 检测后留卡场上）。
///   - 【每回合1次】按本卡实例计（同名多张各自每回合 1 次），仅在实际发动时记入 TurnOnceUsed。
///   - 局限：守护仅在 KOCardAsync 路径（战斗KO / 走 KO 流程的效果KO / GM KO）派发，
///     同步 KOCard（满员废弃等）不经此处，符合规则（那类不是「被KO」置换时机）。
/// </summary>
public class OP16_018_Rockstar : IScriptedEffect
{
    public string CardNumber => "OP16-018";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.PreKO || t == EffectTrigger.OnAllyWillBeKOd;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source; // 守护者本体（OP16-018）

        // 取被守护目标：PreKO=自身；OnAllyWillBeKOd=ctx.Vars["victimId"] 反查我方角色
        CardInstance? victim;
        if (ctx.Trigger == EffectTrigger.PreKO)
        {
            victim = self;
        }
        else
        {
            var victimId = ctx.Vars.TryGetValue("victimId", out var v) ? v as string : null;
            if (victimId is null) return;
            victim = me.Characters.FirstOrDefault(c => c.Id.ToString() == victimId);
        }
        if (victim is null) return;

        // 被守护角色须含《红发海盗团》特征
        if (!victim.Info.HasKeyword("红发海盗团")) return;

        // 【每回合1次】按本卡实例计
        var key = $"OP16-018-guard:{self.Id}";
        if (me.TurnOnceUsed.Contains(key)) return;

        // 成本候选：手牌中【角色】且印刷力量≥6000
        var candidates = me.Hand
            .Where(c => c.Info.Kind == CardKind.Character && c.Info.Power >= 6000)
            .ToList();
        if (candidates.Count == 0) return; // 无合法弃牌 → 无法发动，不弹提示

        // 可选发动：选 1 张要丢弃的手牌（选 0 张 = 放弃，不计入每回合1次）
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "DiscardToPreventKO",
            "罗库斯塔：丢弃手牌中1张力量≥6000的角色，使该角色不被KO？",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return; // 放弃

        var toDiscard = me.Hand.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (toDiscard is null) return;

        AtomicOps.DiscardHand(me, toDiscard);
        ctx.State.MarkPreventKO(victim.Id);
        me.TurnOnceUsed.Add(key);
    }
}
