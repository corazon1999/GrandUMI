using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-078 欧罗·杰克逊号（舞台 / 暗 / 罗杰海盗团，cost1）
/// 【每回合1次】当我方拥有的特征中包含〈罗杰海盗团〉的角色因对方的效果而离开场上时，
///   从咚!!卡组中追加最多1张休息状态的咚!!。
///
/// 实现说明：
///   - 用反应式 watcher OnCharLeaveField（角色因效果离场派发，含KO/退回/放回卡组/置入生命）。
///     payload cardId、owner。仅当离场卡属我方（owner==OwnerIndex）。
///   - "因对方的效果"：判 KOReason=="effect" 且 KOActingSide 为对方（引擎在效果驱动离场时设定）。
///   - 〈罗杰海盗团〉判定：离场卡已移出场上，按 cardId 在废弃区/手牌/卡组/生命区查回该卡读其特征。
///   - 收益：RefreshDonFromDeck(me, 1, Rest)；每回合1次。
/// </summary>
public class OP13_078_OroJackson : IScriptedEffect
{
    public string CardNumber => "OP13-078";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharLeaveField;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 离场卡须属我方
        int leaveOwner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int o ? o : -1;
        if (leaveOwner != ctx.OwnerIndex) return Task.CompletedTask;

        // "因对方的效果"
        if (ctx.State.KOReason != "effect" || ctx.State.KOActingSide != 1 - ctx.OwnerIndex)
            return Task.CompletedTask;

        var cardId = ctx.Vars.TryGetValue("cardId", out var cv) ? cv as string : null;
        if (cardId is null) return Task.CompletedTask;

        // 离场卡跨区查回以读取特征。除废弃/手牌/卡组/生命外还须搜场上角色区/舞台：
        // 离场卡可能被同一效果链后续操作再次登场回场上，OnCharLeaveField 已证明它确实离场过。
        var left = me.Trash.FirstOrDefault(c => c.Id.ToString() == cardId)
                   ?? me.Hand.FirstOrDefault(c => c.Id.ToString() == cardId)
                   ?? me.Deck.FirstOrDefault(c => c.Id.ToString() == cardId)
                   ?? me.LifeArea.FirstOrDefault(c => c.Id.ToString() == cardId)
                   ?? me.Characters.FirstOrDefault(c => c.Id.ToString() == cardId)
                   ?? (me.StageCard is { } st && st.Id.ToString() == cardId ? st : null);
        if (left is null || !left.Info.HasKeyword("罗杰海盗团")) return Task.CompletedTask;

        // 每回合1次
        var key = ctx.Source.Info.Number + "-leave" + ":" + ctx.Source.Id;
        if (me.TurnOnceUsed.Contains(key)) return Task.CompletedTask;
        me.TurnOnceUsed.Add(key);

        AtomicOps.RefreshDonFromDeck(me, 1, DonState.Rest);
        return Task.CompletedTask;
    }
}
