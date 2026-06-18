using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB04-060 橡皮橡皮猎鹰机关枪（事件 / 光 / 艾格赫德·四皇·草帽一伙，cost2）
/// 【主要】可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：
///   将我方手牌中最多 1 张拥有《艾格赫德》特征的角色卡牌正面朝上加入生命区最上方。
///   之后，本回合中，对方最多 1 张角色力量-1000。
///
/// 实现说明 / 简化点：
///   - 成本"将生命区最上方或最下方 1 张加入手牌"：让玩家二选一（顶/底），手动移动到手牌。
///     生命区为空则无法支付，直接返回。
///   - 效果"将手牌中最多 1 张《艾格赫德》角色加入生命区最上方"：手动 Hand→LifeArea 顶部
///     （生命牌无正/反面字段，"正面朝上"按惯例省略，仅做位置转移）。
///   - "之后，本回合中对方最多 1 张角色力量-1000"：AddPowerThisTurn(tgt, -1000)。
/// </summary>
public class EB04_060_FalconGatling : IScriptedEffect
{
    public string CardNumber => "EB04-060";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.EventMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];
        int oppIdx = 1 - ctx.OwnerIndex;

        if (me.LifeArea.Count == 0) return; // 无生命牌 → 无法支付成本

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "橡皮橡皮猎鹰机关枪【主要】：将生命区顶或底 1 张加入手牌，置 1 张《艾格赫德》角色入生命区，对方 1 张角色-1000？");
        if (!use) return;

        // 成本：将生命区最上方或最下方 1 张加入手牌（顶/底二选一）
        CardInstance lifeCard;
        if (me.LifeArea.Count == 1)
        {
            lifeCard = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
        }
        else
        {
            int pick = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "将生命区哪一张加入手牌？", new[] { "最上方", "最下方" });
            if (pick == 1)
            {
                lifeCard = me.LifeArea[^1];
                me.LifeArea.RemoveAt(me.LifeArea.Count - 1);
            }
            else
            {
                lifeCard = me.LifeArea[0];
                me.LifeArea.RemoveAt(0);
            }
        }
        me.Hand.Add(lifeCard);

        // 效果 1：将手牌中最多 1 张《艾格赫德》角色加入生命区最上方
        var cands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.HasKeyword("艾格赫德")).ToList();
        if (cands.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
                "将手牌中最多 1 张《艾格赫德》角色加入生命区最上方",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (ch.Count > 0)
            {
                var picked = cands.First(c => c.Id.ToString() == ch[0]);
                me.Hand.Remove(picked);
                me.LifeArea.Insert(0, picked);
            }
        }

        // 效果 2：本回合中，对方最多 1 张角色力量-1000
        if (opp.Characters.Count > 0)
        {
            var ch2 = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
                "选择最多 1 张对方角色，本回合力量-1000",
                opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
            if (ch2.Count > 0)
            {
                var tgt = opp.Characters.First(c => c.Id.ToString() == ch2[0]);
                AtomicOps.AddPowerThisTurn(tgt, -1000);
            }
        }
    }
}
