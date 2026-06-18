using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP07-085 斯图西（角色）
/// 【登场时】可以将我方的 1 张角色放置到废弃区：将对方最多 1 张角色 KO。
///
/// 实现说明 / 简化点：
///   - 可选效果，先 ConfirmOptional 询问是否发动。
///   - 成本："将我方的 1 张角色放置到废弃区"——用 KO(ctx.State, ctx.OwnerIndex, 牺牲角色) 实现（我方角色入废弃区）。
///     注意：自身斯图西也可作为牺牲对象，故候选含全部我方角色。
///   - 若无可牺牲的我方角色或玩家不发动，则不产生收益。
///   - 收益："将对方最多 1 张角色 KO"——由玩家选择 0~1 张对方角色。
/// </summary>
public class OP07_085_Stussy : IScriptedEffect
{
    public string CardNumber => "OP07-085";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 成本所需：我方至少 1 张角色可牺牲
        var sacCands = me.Characters.ToList();
        if (sacCands.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "斯图西【登场时】：将我方 1 张角色放置到废弃区，将对方最多 1 张角色 KO？");
        if (!use) return;

        // 支付成本：选择并废弃我方 1 张角色
        var sacChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张我方角色放置到废弃区（成本）",
            sacCands.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (sacChosen.Count < 1) return; // 未支付成本

        var sacrifice = sacCands.First(c => c.Id.ToString() == sacChosen[0]);
        AtomicOps.KO(ctx.State, ctx.OwnerIndex, sacrifice);

        // 收益：将对方最多 1 张角色 KO
        var koCands = opp.Characters.ToList();
        if (koCands.Count == 0) return;
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = koCands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var koChosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择最多 1 张对方角色 KO",
            koCands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (koChosen.Count > 0)
        {
            var target = koCands.First(c => c.Id.ToString() == koChosen[0]);
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, target);
        }
    }
}
