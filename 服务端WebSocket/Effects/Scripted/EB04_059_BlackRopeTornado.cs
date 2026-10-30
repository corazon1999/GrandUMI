using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-059 黑绳大龙卷风（事件 / 光 / 鱼人岛·超新星·草帽一伙，cost6）
/// 【主要】可以将我方生命区最上方的 1 张卡牌翻至正面朝上：
///   我方角色少于对方角色的场合，将对方最多 1 张费用不高于 6 的角色
///   和最多 1 张费用不高于 5 的角色 KO。
///
/// 实现说明：
///   - 成本为将生命区最上方1张翻至正面朝上；生命为空或生命顶已经正面朝上时无法发动。
///   - "可以…"为可选效果：先 ConfirmOptional。
///   - 条件"我方角色少于对方角色"：比较双方场上角色数量 me.Characters.Count < opp.Characters.Count。
///   - KO 段：分别选择费用≤6和费用≤5的不同角色，再作为同一批次同时KO。
///     费用按当前费用 CurrentCostOf 评估，每档各最多1张。
/// </summary>
public class EB04_059_BlackRopeTornado : IScriptedEffect
{
    public string CardNumber => "EB04-059";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        if (me.LifeArea.Count == 0 || me.LifeArea[0].IsLifeFaceUp) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "黑绳大龙卷风【主要】：将生命区最上方 1 张翻至正面朝上，KO 对方角色？");
        if (!use) return;
        AtomicOps.FlipTopLifeFaceUp(me);

        // 条件：我方角色少于对方角色
        if (me.Characters.Count >= opp.Characters.Count) return;

        // 先完成两档目标选择，再统一处理替代、离场与KO时效果。
        var victims = new List<CardInstance>();
        var cand6 = opp.Characters.Where(c => ctx.State.CurrentCostOf(oppIdx, c) <= 6).ToList();
        Guid? firstChosenId = null;
        if (cand6.Count > 0)
        {
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张费用≤6 的对方角色 KO",
                cand6.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0)
            {
                var tgt = cand6.First(c => c.Id.ToString() == ch[0]);
                firstChosenId = tgt.Id;
                victims.Add(tgt);
            }
        }

        // 第二档不得重复选择第一档的角色。
        var cand5 = opp.Characters.Where(c =>
            c.Id != firstChosenId && ctx.State.CurrentCostOf(oppIdx, c) <= 5).ToList();
        if (cand5.Count > 0)
        {
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张费用≤5 的对方角色 KO",
                cand5.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch.Count > 0)
            {
                var tgt = cand5.First(c => c.Id.ToString() == ch[0]);
                victims.Add(tgt);
            }
        }
        await AtomicOps.KOCardsByEffectAsync(ctx.State, oppIdx, victims, ctx.Prompts, ctx.OwnerIndex);
    }
}
