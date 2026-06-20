using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// P-085 杰丽·邦妮（角色 / 光）
/// 【登场时】我方领袖拥有《超新星》特征，且我方生命牌张数不多于对方生命牌张数的场合，
///   将对方最多 1 张费用不高于 4 的角色正面朝上加入其持有者生命区的最上方或最下方。
///
/// 实现：MoveCharToLife(对方 owner, toTop) 支持把对方角色置入对方生命区顶/底，
///   通过 ChooseOption 让玩家选择顶/底。
/// </summary>
public class P_085_JewelryBonney : IScriptedEffect
{
    public string CardNumber => "P-085";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：领袖拥有《超新星》特征，且我方生命 ≤ 对方生命
        if (!me.Leader.Info.HasKeyword("超新星")) return;
        if (me.LifeCount > opp.LifeCount) return;

        var cands = opp.Characters.Where(c => c.Info.Cost <= 4).ToList();
        if (cands.Count == 0) return;

        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择对方最多 1 张费用≤4 的角色，加入其生命区顶或底",
            cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        var tgt = cands.First(c => c.Id.ToString() == chosen[0]);
        int pos = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将其加入对方生命区的", new[] { "最上方", "最下方" });

        AtomicOps.MoveCharToLife(ctx.State, 1 - ctx.OwnerIndex, tgt, toTop: pos == 0);
    }
}
