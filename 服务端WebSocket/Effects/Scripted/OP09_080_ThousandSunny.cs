using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP09-080 千里·阳光号（舞台）
/// 【对方的回合中】可以将此舞台转为休息状态：当我方拥有《草帽一伙》特征的角色因对方的效果而
///   离开场上时，从咚!!卡组中追加最多 1 张休息状态的咚!!。
///
/// 实现说明 / 简化点：
///   - 用 Wave2 反应式 watcher OnCharLeaveField 监听"角色因效果离开场上时"。
///   - 触发限制为【对方的回合中】（CurrentTurnPlayer != OwnerIndex）；对方回合中导致我方角色
///     离场的效果通常即为对方的效果，故以此近似"因对方的效果"。
///   - 离场卡需为我方所属且特征含《草帽一伙》；离场卡已不在场，按 payload cardId 在我方各区查回判定。
///   - 发动成本为"将此舞台转为休息状态"，用 ConfirmOptional 询问，确认后横置本舞台再追加休息咚。
///   - 舞台已处于休息状态则无法支付成本（RestCard 对已休息为 no-op，但此处先判断更直观）。
/// </summary>
public class OP09_080_ThousandSunny : IScriptedEffect
{
    public string CardNumber => "OP09-080";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var self = ctx.Source;

        // 【对方的回合中】
        if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex) return;

        // 离场卡需为我方所属
        int leaveOwner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (leaveOwner != ctx.OwnerIndex) return;

        // 取离场卡 id，并在我方各区查回判定特征
        var cardId = ctx.Vars.TryGetValue("cardId", out var cv) ? cv as string : null;
        if (cardId is null) return;
        CardInstance? gone = FindCard(me, cardId);
        if (gone is null || !gone.Info.HasKeyword("草帽一伙")) return;

        // 成本：将此舞台转为休息状态（已休息则无法支付）
        if (self.IsTapped) return;

        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "千里·阳光号：将此舞台转为休息状态，从咚!!卡组追加 1 张休息状态的咚!!？");
        if (!use) return;

        // 支付成本：横置舞台
        AtomicOps.RestCard(self);

        // 效果：从咚!!卡组追加最多 1 张休息状态的咚!!
        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
    }

    private static CardInstance? FindCard(PlayerState p, string id)
    {
        foreach (var c in p.Trash) if (c.Id.ToString() == id) return c;
        foreach (var c in p.Hand) if (c.Id.ToString() == id) return c;
        foreach (var c in p.Deck) if (c.Id.ToString() == id) return c;
        foreach (var c in p.LifeArea) if (c.Id.ToString() == id) return c;
        // 离场卡可能被同一效果链后续操作再次登场回场上，须搜场上角色区/舞台
        foreach (var c in p.Characters) if (c.Id.ToString() == id) return c;
        if (p.StageCard is not null && p.StageCard.Id.ToString() == id) return p.StageCard;
        return null;
    }
}
