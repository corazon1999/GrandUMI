using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>ST13-017 火焰龙王：反击加力并重排全部生命；触发以生命入手为成本后将最多1张手牌置于生命顶。</summary>
public sealed class ST13_017_FlameDragonKing : IScriptedEffect
{
    public string CardNumber => "ST13-017";
    public bool HandlesTrigger(EffectTrigger trigger) =>
        trigger is EffectTrigger.EventCounter or EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            if (me.LifeArea.Count == 0 ||
                !await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex, "火焰龙王【触发】：将生命顶或底1张加入手牌，以将最多1张手牌置于生命顶？"))
                return;
            int position = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, "选择加入手牌的生命", new[] { "最上方", "最下方" });
            int index = position == 0 ? 0 : me.LifeArea.Count - 1;
            var life = me.LifeArea[index];
            me.LifeArea.RemoveAt(index);
            life.IsLifeFaceUp = false;
            me.Hand.Add(life);
            var put = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnHand",
                "将最多1张手牌置于生命区最上方", me.Hand.Select(card => card.Id.ToString()).ToList(), 0, 1);
            if (put.Count > 0)
            {
                var card = me.Hand.FirstOrDefault(item => item.Id.ToString() == put[0]);
                if (card is not null) { me.Hand.Remove(card); me.LifeArea.Insert(0, card); }
            }
            return;
        }

        var targets = new[] { me.Leader }.Concat(me.Characters).ToList();
        var buff = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLeaderOrCharacter",
            "我方最多1张领袖或角色本次战斗力量+4000",
            targets.Select(card => card.Id.ToString()).ToList(), 0, 1);
        if (buff.Count > 0)
        {
            var target = targets.FirstOrDefault(card => card.Id.ToString() == buff[0]);
            if (target is not null) AtomicOps.AddPowerThisBattle(target, 4000);
        }
        await ReorderLife(ctx, me);
    }

    private static async Task ReorderLife(EffectContext ctx, PlayerState me)
    {
        if (me.LifeArea.Count <= 1) return;
        var remaining = me.LifeArea.ToList();
        var ordered = new List<CardInstance>();
        while (remaining.Count > 1)
        {
            var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnLifeAll",
                $"选择生命区从上到下第{ordered.Count + 1}张",
                remaining.Select(card => card.Id.ToString()).ToList(), 1, 1,
                new Dictionary<string, object?>
                {
                    ["choiceCards"] = remaining.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
                });
            var card = remaining.FirstOrDefault(item => item.Id.ToString() == chosen.FirstOrDefault()) ?? remaining[0];
            remaining.Remove(card);
            ordered.Add(card);
        }
        ordered.Add(remaining[0]);
        me.LifeArea.Clear();
        me.LifeArea.AddRange(ordered);
    }
}
