using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

internal static class OfficialConsistencyHelpers
{
    public static async Task ReorderTopLife(EffectContext ctx)
    {
        var sides = Enumerable.Range(0, 2)
            .Where(side => ctx.State.Players[side].LifeArea.Count > 0)
            .ToList();
        if (sides.Count == 0) return;

        var labels = sides.Select(side => side == ctx.OwnerIndex ? "我方生命" : "对方生命").ToList();
        labels.Add("不确认");
        int choice = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "确认我方或对方生命区最上方最多 1 张卡牌", labels);
        if (choice < 0 || choice >= sides.Count) return;

        var life = ctx.State.Players[sides[choice]].LifeArea;
        var top = life[0];
        int placement = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            $"已确认 {top.Info.Number}：放置到该生命区的哪个位置？",
            new[] { "最上方", "最下方" });
        if (placement != 1 || life.Count <= 1) return;
        life.RemoveAt(0);
        life.Add(top);
    }

    public static async Task AddBattlePower(EffectContext ctx, int delta)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var target = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OwnLeaderOrCharacter",
            $"选择我方最多 1 张领袖或角色，本次战斗力量+{delta}", targets);
        if (target is not null) AtomicOps.AddPowerThisBattle(target, delta);
    }
}
/// <summary>ST07-016 强力麻糬：生命顶确认与顶/底调整。</summary>
public sealed class ST07_016_PowerMochi : IScriptedEffect
{
    public string CardNumber => "ST07-016";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.EventCounter or EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
            await AtomicOps.DrawAsync(ctx.State, ctx.OwnerIndex, 1);
        await OfficialConsistencyHelpers.ReorderTopLife(ctx);
        if (ctx.Trigger == EffectTrigger.EventCounter)
            await OfficialConsistencyHelpers.AddBattlePower(ctx, 2000);
    }
}

/// <summary>ST09-015 雷鸣八卦：低生命时把对方低费角色正面朝上置入生命。</summary>
public sealed class ST09_015_ThunderBagua : IScriptedEffect
{
    public string CardNumber => "ST09-015";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        await OfficialConsistencyHelpers.AddBattlePower(ctx, 4000);
        if (ctx.State.Players[ctx.OwnerIndex].LifeArea.Count > 2) return;

        int opponent = 1 - ctx.OwnerIndex;
        var candidates = ctx.State.Players[opponent].Characters
            .Where(card => ctx.State.CurrentCostOf(opponent, card) <= 3).ToList();
        var target = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OpponentCharacter",
            "将对方最多 1 张费用不高于 3 的角色正面朝上加入其生命区", candidates);
        if (target is null) return;

        int placement = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
            "将该角色加入对方生命区", new[] { "最上方", "最下方" });
        if (await AtomicOps.TryEffectLeaveGuard(ctx.State, opponent, target, ctx.Prompts, "life")) return;
        AtomicOps.MoveCharToLife(ctx.State, opponent, target, toTop: placement != 1);
        if (ctx.State.Players[opponent].LifeArea.Contains(target)) target.IsLifeFaceUp = true;
    }
}

/// <summary>ST13-004 爱德华·纽哥特：加生命后，从全部生命中取一张置于卡组顶并自选剩余顺序。</summary>
public sealed class ST13_004_EdwardNewgate : IScriptedEffect
{
    public string CardNumber => "ST13-004";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        AtomicOps.AddLifeFromDeckTop(me, 1);
        if (me.LifeArea.Count == 0) return;

        var life = me.LifeArea.ToList();
        var extra = new Dictionary<string, object?>
        {
            ["choiceCards"] = life.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
        };
        var deckTopChoice = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLifeAll",
            "确认所有生命卡牌，选择 1 张放置到卡组最上方",
            life.Select(card => card.Id.ToString()).ToList(), 1, 1, extra);
        if (deckTopChoice.Count == 0) return;

        var deckTop = life.First(card => card.Id.ToString() == deckTopChoice[0]);
        me.LifeArea.Remove(deckTop);
        deckTop.IsLifeFaceUp = false;
        me.Deck.Insert(0, deckTop);

        if (me.LifeArea.Count <= 1) return;
        var remaining = me.LifeArea.ToList();
        var order = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLifeReorder",
            "按从最上方到最下方的顺序排列剩余生命卡牌",
            remaining.Select(card => card.Id.ToString()).ToList(), remaining.Count, remaining.Count,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = remaining.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
            });
        if (order.Count != remaining.Count) return;
        me.LifeArea.Clear();
        foreach (var id in order)
            me.LifeArea.Add(remaining.First(card => card.Id.ToString() == id));
    }
}
