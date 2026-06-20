using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP04-073 Mr.13&amp;Ms.星期五（角色 / 暗 / 动物·巴洛克工作室）
/// 【启动主要】可以将此角色和我方 1 张拥有《巴洛克工作室》特征的角色放置到废弃区：
///   从咚!!卡组中追加最多 1 张活跃状态的咚!!。
///
/// 实现说明：
///   - 成本 = 将此角色 + 另 1 张《巴洛克工作室》角色废弃。需场上存在另一张该特征角色才可发动。
///   - 收益 = RefreshDonFromDeck(1, Active) 从咚卡组追加 1 张活跃咚。
/// </summary>
public class OP04_073_Mr13AndMsFriday : IScriptedEffect
{
    public string CardNumber => "OP04-073";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 另 1 张《巴洛克工作室》角色（不含自身）
        var others = me.Characters.Where(c => c.Id != self.Id && c.Info.HasKeyword("巴洛克工作室")).ToList();
        if (others.Count == 0) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "Mr.13&Ms.星期五【启动主要】：废弃此角色和 1 张《巴洛克工作室》角色，从咚卡组追加 1 张活跃咚？");
        if (!use) return;

        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnCharacter",
            "选择 1 张《巴洛克工作室》角色与此角色一同废弃",
            others.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (chosen.Count < 1) return;

        var partner = others.First(c => c.Id.ToString() == chosen[0]);

        // 支付成本：将两张角色放置到废弃区
        if (me.Characters.Remove(partner)) me.Trash.Add(partner);
        if (me.Characters.Remove(self)) me.Trash.Add(self);

        // 收益：从咚卡组追加最多 1 张活跃咚
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
    }
}
