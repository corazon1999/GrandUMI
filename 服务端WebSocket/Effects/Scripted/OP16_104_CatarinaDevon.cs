using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP16-104 卡特琳·蝶美（角色，黄，力量3000）
/// 【攻击时】选择对方最多1张角色。本回合中，此角色原本的力量变为与被选中的角色相同。
///
/// 实现说明：
///   - 只接管 OnAttackDeclare；生命区【触发】继续由 OP16.json 的 DSL 处理。
///   - 复制所选角色结算时的当前力量，并写入 OriginalPowerOverride。
///   - 该覆盖只改变原本力量，自身的咚加成和其他力量修正仍会继续叠加，且在回合结束时自动清除。
/// </summary>
public class OP16_104_CatarinaDevon : IScriptedEffect
{
    public string CardNumber => "OP16-104";

    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        int opponentIndex = 1 - ctx.OwnerIndex;
        var opponentCharacters = ctx.State.Players[opponentIndex].Characters.ToList();
        if (opponentCharacters.Count == 0) return;

        var chosen = await ctx.Prompts.ChooseCards(
            ctx.OwnerIndex,
            "OpponentCharacter",
            "选择对方最多1张角色，本回合此角色原本力量变为与其相同",
            opponentCharacters.Select(card => card.Id.ToString()).ToList(),
            0,
            1);

        if (chosen.Count == 0) return;

        var target = opponentCharacters.FirstOrDefault(card => card.Id.ToString() == chosen[0]);
        if (target is null) return;

        ctx.Source.OriginalPowerOverride = ctx.State.CurrentPowerOf(opponentIndex, target);
    }
}
