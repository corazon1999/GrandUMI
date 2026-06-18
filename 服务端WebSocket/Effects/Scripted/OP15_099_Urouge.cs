using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-099 乌鲁吉（角色 / 光 6 费 7000，空岛/超新星/破戒僧海盗团）
/// 【登场时】可以丢弃我方手牌中 1 张拥有《超新星》特征的卡牌：本回合中，此角色获得【速攻】效果。
/// 【启动主要】可以将我方生命区最上方的 1 张卡牌翻至正面朝下：
///   赋予我方 1 张领袖或角色最多 1 张休息状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 两个时机：OnEnterField + ActivatedMain，在 Resolve 内按 ctx.Trigger 分支。
///   - 【登场时】成本为可选丢弃 1 张《超新星》手牌，收益为本回合获得【速攻】(GiveKeyword)。
///   - 【启动主要】成本"将生命区最上方 1 张翻至正面朝下"：引擎未在 PlayerState 区分生命牌朝向
///     (生命牌默认即面朝下)，故该成本无实际状态变化，作为可选发动开关处理；收益为赋予 1 张休息咚。
/// </summary>
public class OP15_099_Urouge : IScriptedEffect
{
    public string CardNumber => "OP15-099";

    public bool HandlesTrigger(EffectTrigger t)
        => t == EffectTrigger.OnEnterField || t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // 成本：丢弃我方手牌中 1 张拥有《超新星》特征的卡牌（可选）
            var cands = me.Hand.Where(c => c.Info.HasKeyword("超新星")).ToList();
            if (cands.Count == 0) return;

            bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "乌鲁吉【登场时】：丢弃 1 张《超新星》手牌，使此角色本回合获得【速攻】？");
            if (!use) return;

            var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "丢弃 1 张《超新星》手牌",
                cands.Select(c => c.Id.ToString()).ToList(), 1, 1);
            if (pick.Count < 1) return;
            var discard = cands.First(c => c.Id.ToString() == pick[0]);
            AtomicOps.DiscardHand(me, discard);

            // 收益：本回合此角色获得【速攻】
            AtomicOps.GiveKeyword(self, "速攻", KeywordDuration.ThisTurn);
            return;
        }

        // ── 【启动主要】 ──
        // 成本：将生命区最上方 1 张翻至正面朝下（引擎无朝向状态，作为可选开关）
        if (me.LifeArea.Count == 0) return;

        bool act = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "乌鲁吉【启动主要】：将生命区最上方 1 张翻至正面朝下，赋予我方 1 张领袖/角色 1 张休息咚?");
        if (!act) return;

        // 需要有休息状态的咚可赋予
        int restDon = me.CostArea.Count(d => d.State == DonState.Rest);
        if (restDon <= 0) return;

        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = targets.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "选择 1 张领袖或角色，赋予最多 1 张休息状态的咚!!（可不选）",
            targets.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var tgt = targets.First(c => c.Id.ToString() == chosen[0]);
        AtomicOps.AttachDonFromCost(me, tgt.Id, 1, DonState.Rest);
    }
}
