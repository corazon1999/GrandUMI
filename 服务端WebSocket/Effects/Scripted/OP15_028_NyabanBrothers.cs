using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-028 小猫兄弟（角色）
/// 【登场时】我方领袖拥有《东海》特征的场合，赋予对方1张角色最多1张对方费用区中的咚!!。
///
/// 实现说明：
///   - 仅当我方领袖具备《东海》特征时发动。
///   - "赋予对方1张角色对方费用区中的咚!!"：用 AttachDonFromCost(对方PlayerState, 对方角色Id, 1, DonState.Active)
///     从对方费用区取1张活跃咚贴到对方角色上。
///   - 无对方角色或对方费用区无活跃咚时无收益。
/// </summary>
public class OP15_028_NyabanBrothers : IScriptedEffect
{
    public string CardNumber => "OP15-028";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var opp = ctx.State.Players[1 - ctx.OwnerIndex];

        // 条件：我方领袖拥有《东海》特征
        if (!me.Leader.Info.HasKeyword("东海")) return;

        if (opp.Characters.Count == 0 || opp.ActiveDonCount < 1) return;

        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OpponentCharacter",
            "选择1张对方角色，赋予最多1张对方费用区中的咚!!",
            opp.Characters.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (pick.Count < 1) return;

        var target = opp.Characters.First(c => c.Id.ToString() == pick[0]);
        AtomicOps.AttachDonFromCost(opp, target.Id, 1, DonState.Active);
    }
}
