using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Effects;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP08-056 莫比·迪克号（舞台）
/// 【我方的回合中】【每回合1次】我方拥有的特征中包含<白胡子海盗团>的角色因效果而离开场上时，
///   抽取1张卡牌。之后，将我方的1张手牌放回卡组最上方或最下方。
///
/// 实现说明 / 简化点：
///   - 用 Wave2 反应式 watcher OnCharLeaveField 监听"角色因效果离开场上"。
///   - 仅在我方回合、每回合1次生效；离场卡需为我方所属且特征含<白胡子海盗团>。
///   - 离场卡已不在场上，故按 payload 的 cardId 在我方各区(废弃/手牌/卡组/生命)查回该卡判定其特征。
///   - "放回卡组最上方或最下方"用 ChooseOption 让玩家二选一。
/// </summary>
public class OP08_056_MobyDick : IScriptedEffect
{
    public string CardNumber => "OP08-056";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnCharLeaveField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        // 【我方的回合中】
        if (ctx.State.CurrentTurnPlayer != ctx.OwnerIndex) return;

        // 离场卡需为我方所属
        int leaveOwner = ctx.Vars.TryGetValue("owner", out var ov) && ov is int oi ? oi : -1;
        if (leaveOwner != ctx.OwnerIndex) return;

        // 取离场卡 id
        var cardId = ctx.Vars.TryGetValue("cardId", out var cv) ? cv as string : null;
        if (cardId is null) return;

        // 在我方各区查回该卡，判定其特征是否含<白胡子海盗团>
        CardInstance? gone = FindCard(me, cardId);
        if (gone is null || !gone.Info.HasKeyword("白胡子海盗团")) return;

        // 【每回合1次】
        var key = self_Key();
        if (me.TurnOnceUsed.Contains(key)) return;
        me.TurnOnceUsed.Add(key);

        // 抽取 1 张卡牌
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);

        // 之后：将我方 1 张手牌放回卡组最上方或最下方
        if (me.Hand.Count == 0) return;
        var pick = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "将 1 张手牌放回卡组",
            me.Hand.Select(c => c.Id.ToString()).ToList(), 1, 1);
        if (pick.Count == 0) return;
        var card = me.Hand.First(c => c.Id.ToString() == pick[0]);

        int where = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "放回卡组最上方还是最下方？", new[] { "卡组最上方", "卡组最下方" });
        me.Hand.Remove(card);
        if (where == 0) me.Deck.Insert(0, card);
        else me.Deck.Add(card);
    }

    private string self_Key() => CardNumber + "-leave";

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
