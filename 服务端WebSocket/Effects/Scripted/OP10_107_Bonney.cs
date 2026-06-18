using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-107 杰丽·邦妮（角色）
/// 【阻挡者】（关键词，引擎处理）
/// 【登场时】可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：
///   将我方手牌中最多 1 张费用为 5 且拥有《超新星》特征的角色卡牌正面朝上加入生命区最上方。
///
/// 实现说明 / 简化点：
///   - 发动成本为"将我方生命区最上方或最下方 1 张加入手牌"，用 ChooseOption 让玩家选取顶/底。
///   - "正面朝上加入生命区"在内部数据上与普通生命牌一致存放于 LifeArea 顶部（公开与否由展示层处理）。
/// </summary>
public class OP10_107_Bonney : IScriptedEffect
{
    public string CardNumber => "OP10-107";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 收益候选：手牌中费用5且《超新星》角色
        var payload = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.Cost == 5 &&
            c.Info.HasKeyword("超新星")
        ).ToList();

        if (me.LifeArea.Count == 0) return;
        if (payload.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "邦妮【登场时】：将生命区最上方或最下方的1张卡牌加入手牌，然后将手牌中1张费用5且《超新星》角色正面朝上加入生命区最上方？");
        if (!use) return;

        // 成本：选取生命区顶或底 1 张加入手牌
        int pos = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将生命区哪一端的卡牌加入手牌？", new[] { "最上方", "最下方" });
        CardInstance lifeCard = pos == 0 ? me.LifeArea[0] : me.LifeArea[me.LifeArea.Count - 1];
        me.LifeArea.Remove(lifeCard);
        me.Hand.Add(lifeCard);

        // 收益：手牌中最多 1 张费用5《超新星》角色加入生命区最上方
        // （注意：成本牌已进入手牌，但其费用/特征不一定满足，候选已在成本支付前确定）
        var stillAvailable = payload.Where(c => me.Hand.Contains(c)).ToList();
        if (stillAvailable.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = stillAvailable.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "选择最多1张费用5且《超新星》的角色加入生命区最上方",
            stillAvailable.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count > 0)
        {
            var picked = stillAvailable.First(c => c.Id.ToString() == chosen[0]);
            me.Hand.Remove(picked);
            me.LifeArea.Insert(0, picked);
        }
    }
}
