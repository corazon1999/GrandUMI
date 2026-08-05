using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// EB03-053 奈美（角色 / 光 / 草帽一伙，cost5 power6000）
/// 【登场时】赋予我方领袖最多 1 张休息状态的咚!!。之后，对方生命卡牌为 3 张或更多的场合，
///   将对方生命区最上方的最多 1 张卡牌加入其持有者的手牌。
/// 【KO时】可以将我方生命区最上方的 1 张卡牌翻至正面朝上：将我方手牌中最多 1 张力量不高于 6000 的角色卡牌登场。
///
/// 实现说明：
///   - 登场段（均为强制，无"可以"）：①赋予领袖 1 张咚（AttachDonFromDeck；引擎 Attached 不分横竖，
///     力量+1000，下个准备阶段解除→回费用区，与"休息赋予咚"力量等价）；②对方生命≥3 时，将对方
///     生命区最上方 1 张加入其（对方）手牌。
///   - 【KO时】以生命区最上方1张翻至正面作为成本，之后可从手牌登场最多1张力量≤6000的角色。
/// </summary>
public class EB03_053_Nami : IScriptedEffect
{
    public string CardNumber => "EB03-053";

    public bool HandlesTrigger(EffectTrigger t)
        => t is EffectTrigger.OnEnterField or EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            // ① 赋予我方领袖最多 1 张休息状态的咚!!（从费用区取休息咚，与汉库珂/阿兰/戈耳工统一走
            //    AttachDonFromCost；原 AttachDonFromDeck 从咚卡组取，解除后咚永久留费用区→玩家咚+1，故改）
            AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Rest);

            // ② 之后：对方生命卡牌≥3 时，将对方生命区最上方 1 张加入其（对方）手牌
            if (opp.LifeArea.Count >= 3)
            {
                var top = opp.LifeArea[0];
                opp.LifeArea.RemoveAt(0);
                top.IsLifeFaceUp = false;
                opp.Hand.Add(top);
            }
            return;
        }

        // 【KO时】成本：生命顶须存在且当前为背面朝下
        if (!me.Trash.Contains(ctx.Source) || me.LifeArea.Count == 0 || me.LifeArea[0].IsLifeFaceUp) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "奈美【KO时】：将生命区最上方1张翻至正面，登场手牌中最多1张力量≤6000的角色？")) return;
        AtomicOps.FlipTopLifeFaceUp(me);

        var candidates = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.Power <= 6000).ToList();
        if (candidates.Count == 0) return;
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = candidates.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "登场手牌中最多1张力量≤6000的角色",
            candidates.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = candidates.First(c => c.Id.ToString() == chosen[0]);
            await AtomicOps.PlayFromHandFree(ctx.State, ctx.OwnerIndex, picked);
        }
    }
}
