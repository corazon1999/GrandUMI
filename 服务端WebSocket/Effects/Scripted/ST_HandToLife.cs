using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// ST07-001 夏洛特·玲玲（领航）
/// 【咚!!×2】【攻击时】可以将我方生命区最上方的1张卡牌加入手牌：我方生命卡牌不多于2张的场合，将我方最多1张手牌加入生命区最上方。
/// （“最上方或最下方”取最上方近似）
/// </summary>
public class ST07_001_BigMom : IScriptedEffect
{
    public string CardNumber => "ST07-001";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.AttachedDonCount(ctx.Source.Id) < 2) return;
        if (ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return; // ST15-001
        if (me.LifeArea.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将生命区最上方1张加入手牌？")) return;
        var lifeTop = me.LifeArea[0]; me.LifeArea.RemoveAt(0); me.Hand.Add(lifeTop);
        if (me.LifeCount > 2 || me.Hand.Count == 0) return;
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "将我方最多1张手牌加入生命区最上方", me.Hand.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (ch.Count > 0) AtomicOps.HandToLife(me, me.Hand.First(c => c.Id.ToString() == ch[0]), toTop: true);
    }
}

/// <summary>
/// ST13-005 安普里奥·伊万科夫（角色）
/// 【阻挡者】【登场时】可以将我方生命区最上方1张放置废弃区：公开我方手牌中最多1张费用5的角色，正面朝下加入生命区最上方。
/// </summary>
public class ST13_005_Ivankov : IScriptedEffect
{
    public string CardNumber => "ST13-005";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var cands = me.Hand.Where(c => c.Info.Kind == CardKind.Character && c.Info.Cost == 5).ToList();
        if (cands.Count == 0 || me.LifeArea.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将生命区最上方1张放置废弃区，并将手牌中1张费用5角色加入生命？")) return;
        var lifeTop = me.LifeArea[0]; me.LifeArea.RemoveAt(0); me.Trash.Add(lifeTop);
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = cands.Select(c => new { id = c.Id.ToString(), number = c.Info.Number }).ToList(),
        };
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "公开手牌中最多1张费用5角色，正面朝下加入生命区顶", cands.Select(c => c.Id.ToString()).ToList(), 0, 1, extra);
        if (ch.Count == 0) return;
        var picked = cands.First(c => c.Id.ToString() == ch[0]);
        ctx.Engine?.BroadcastReveal(ctx.OwnerIndex, new List<string> { picked.Info.Number });
        AtomicOps.HandToLife(me, picked, toTop: true, faceUp: false);
    }
}

/// <summary>
/// ST29-007 托尼托尼·乔巴（角色）
/// 【KO时】可以将我方生命区最上方1张加入手牌：将我方最多1张手牌加入生命区最上方。
/// （【触发】本回合“蒙奇·D·路飞”+2000 已由 DSL ST29.json 实现）
/// </summary>
public class ST29_007_Chopper : IScriptedEffect
{
    public string CardNumber => "ST29-007";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return; // ST15-001
        if (me.LifeArea.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "将生命区最上方1张加入手牌，再将1张手牌加入生命区顶？")) return;
        var lifeTop = me.LifeArea[0]; me.LifeArea.RemoveAt(0); me.Hand.Add(lifeTop);
        if (me.Hand.Count == 0) return;
        var ch = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
            "将我方最多1张手牌加入生命区最上方", me.Hand.Select(c => c.Id.ToString()).ToList(), 0, 1);
        if (ch.Count > 0) AtomicOps.HandToLife(me, me.Hand.First(c => c.Id.ToString() == ch[0]), toTop: true);
    }
}
