using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-119 特拉法尔加·罗（角色）
/// 【登场时】公开我方手牌中最多 1 张拥有《超新星》特征的角色卡牌，并将其背面朝上加入生命区最上方。
///   之后，赋予我方 1 张拥有《超新星》特征的领袖最多 1 张休息状态的咚!!。
///
/// 实现说明 / 简化点：
///   - "背面朝上加入生命区最上方"在内部与普通生命牌一致存放于 LifeArea 顶部。
///   - 赋予咚的领袖需拥有《超新星》特征；若领袖不满足则该段不发动。
/// </summary>
public class OP10_119_Law : IScriptedEffect
{
    public string CardNumber => "OP10-119";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // ── 第一段：公开手牌中最多 1 张《超新星》角色，背面朝上加入生命区最上方 ──
        var cands = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character &&
            c.Info.HasKeyword("超新星")
        ).ToList();
        if (cands.Count > 0)
        {
            var extra = new Dictionary<string, object?>
            {
                ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
            };
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
                "公开最多1张《超新星》角色，背面朝上加入生命区最上方",
                cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
            if (chosen.Count > 0)
            {
                var picked = cands.First(c => c.Id.ToString() == chosen[0]);
                me.Hand.Remove(picked);
                me.LifeArea.Insert(0, picked);
            }
        }

        // ── 第二段：赋予我方拥有《超新星》特征的领袖最多 1 张休息状态咚!! ──
        if (me.Leader.Info.HasKeyword("超新星"))
        {
            AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Rest);
        }
    }
}
