using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP10-103 卡彭·班吉（角色）
/// 【登场时】可以将我方生命区最上方或最下方的 1 张卡牌加入手牌：
/// 将我方手牌中最多 1 张拥有《超新星》特征的角色卡牌正面朝上加入生命区最上方。
///
/// 实现说明 / 简化点：
///   - 成本为"将生命区最上方或最下方 1 张加入手牌"。生命牌不公开，用 ChooseOption 让玩家选
///     最上/最下，再把对应位置的卡加入手牌。
///   - 收益"正面朝上加入生命区最上方"：写入 IsLifeFaceUp 后 Insert 到最上方。
///   - 生命区为空则无法支付成本，效果不发动。
/// </summary>
public class OP10_103_CaponeBege : IScriptedEffect
{
    public string CardNumber => "OP10-103";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (me.LifeArea.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "卡彭·班吉：将生命区最上/最下方 1 张加入手牌，并将最多 1 张《超新星》角色加入生命区最上方？");
        if (!use) return;

        // 成本：选择生命区最上方或最下方
        int pos = 0;
        if (me.LifeArea.Count >= 2)
        {
            pos = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "将生命区哪一张加入手牌？", new[] { "最上方", "最下方" });
        }
        var lifeCard = pos == 0 ? me.LifeArea[0] : me.LifeArea[me.LifeArea.Count - 1];
        me.LifeArea.Remove(lifeCard);
        me.Hand.Add(lifeCard);

        // 成本提交后重新读取权威手牌；刚从生命区加入手牌的《超新星》也必须成为收益目标。
        var supernovas = me.Hand.Where(c =>
            c.Info.Kind == CardKind.Character && c.Info.HasKeyword("超新星")).ToList();
        if (supernovas.Count == 0) return;

        // 收益：将手牌中最多 1 张《超新星》角色加入生命区最上方。
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = supernovas.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHandCharacter",
            "将手牌中最多 1 张《超新星》角色加入生命区最上方",
            supernovas.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (chosen.Count == 0) return;

        // Prompt 恢复/重放后仍以当前手牌为最终提交依据，失效选择不得造成异常或重复移动。
        var target = supernovas.FirstOrDefault(c => c.Id.ToString() == chosen[0]);
        if (target is null || !me.Hand.Remove(target)) return;
        target.IsLifeFaceUp = true;
        me.LifeArea.Insert(0, target);
    }
}
