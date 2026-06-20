using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP12-015 蒙奇·D·路飞（角色）
/// 持续：我方合计存在 2 张或更多被赋予中的咚!! 的场合，此角色的力量 +2000。
///   （通过 ContinuousEffect 注册，引擎在每次评估力量时按条件累加。）
/// 【登场时】可以公开我方手牌中的 2 张事件：
///   将我方手牌中最多 1 张力量不高于 3000 的红色（炎）角色卡牌登场。
///   之后，赋予我方 1 张领袖或角色最多 1 张休息状态的咚!!。
///
/// 说明 / 简化点：
/// - "公开 2 张事件" 是发动【登场时】的成本：要求手牌中 ≥2 张事件，公开（选择）后仍留在手牌。
/// - "力量不高于 3000 的红色角色" 取卡面原始力量 c.Info.Power 与元素色"红"。
/// - 持续力量在该角色登场时注册，离场后由引擎清理；重复登场前先去重避免叠加。
/// </summary>
public class OP12_015_Luffy : IScriptedEffect
{
    public string CardNumber => "OP12-015";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;
        int ownerIndex = ctx.OwnerIndex;

        // ── 注册持续：我方合计 ≥2 张被赋予中的咚!! 时，此角色 +2000 ──
        var selfId = self.Id;
        ctx.State.ContinuousEffects.RemoveAll(e => e.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 2000,
            Predicate = (s, sideIdx, card) =>
                card.Id == selfId &&
                s.Players[ownerIndex].CostArea.Count(d => d.State == DonState.Attached) >= 2,
        });

        // ── 【登场时】（可选，成本：公开 2 张事件）──
        var events = me.Hand.Where(c => c.Info.Kind == CardKind.Event).ToList();
        if (events.Count < 2) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞【登场时】：公开手牌中 2 张事件，登场 1 张力量≤3000 的红色角色，并赋予 1 张领袖/角色 1 张休息咚？");
        if (!use) return;

        // 成本：公开（选择）2 张事件
        var revealExtra = new Dictionary<string, object?>
        {
            ["choiceCards"] = events.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var revealed = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "RevealEvents",
            "公开手牌中的 2 张事件",
            events.Select(c => c.Id.ToString()).ToList(), 2, 2, revealExtra);
        if (revealed.Count < 2) return; // 未完成公开 → 成本未支付

        // 向对手公开所选 2 张事件：广播牌面浮层（公开≠丢弃，仍留手牌）
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex,
            revealed.Select(id => events.First(c => c.Id.ToString() == id).Info.Number).ToList());

        // 效果 1：将手牌中最多 1 张力量≤3000 的红色角色登场
        var playable = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Power <= 3000 &&
            c.Info.ColorList.Contains("红")
        ).ToList();
        if (playable.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = playable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
                "登场 1 张力量≤3000 的红色角色（最多 1 张）",
                playable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = playable.First(c => c.Id.ToString() == chosen[0]);
                AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
            }
        }

        // 效果 2：赋予我方 1 张领袖或角色最多 1 张休息状态的咚!!
        var donTargets = new List<CardInstance> { me.Leader };
        donTargets.AddRange(me.Characters);
        var pickTgt = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择 1 张领袖或角色，赋予最多 1 张休息状态的咚!!",
            donTargets.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pickTgt.Count > 0)
        {
            var target = donTargets.First(c => c.Id.ToString() == pickTgt[0]);
            AtomicOps.AttachDonFromCost(me, target.Id, 1, DonState.Rest);
        }
    }
}
