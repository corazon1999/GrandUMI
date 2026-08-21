using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP11-110 鲛星。
/// 将要被 KO 时，可横置我方一张《鱼人岛》卡或“白星”领袖代替；登场时可取生命顶/底并 KO 费用不高于 1 的角色。
/// </summary>
public sealed class OP11_110_Samezvezda : IScriptedEffect
{
    public string CardNumber => "OP11-110";

    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.PreKO or EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];

        if (ctx.Trigger == EffectTrigger.PreKO)
        {
            var costs = new List<CardInstance>();
            if (!me.Leader.IsTapped && me.Leader.Info.NameIs("白星")) costs.Add(me.Leader);
            costs.AddRange(me.Characters.Where(card =>
                card.Id != ctx.Source.Id && !card.IsTapped && card.Info.HasKeyword("鱼人岛")));
            if (me.StageCard is { IsTapped: false } stage && stage.Info.HasKeyword("鱼人岛")) costs.Add(stage);
            if (costs.Count == 0) return;

            var chosen = await ctx.Prompts.ChooseCards(
                ctx.OwnerIndex,
                "OwnCardToRest",
                "将我方 1 张《鱼人岛》卡或“白星”领袖转为休息状态，使鲛星不会被 KO（可放弃）",
                costs.Select(card => card.Id.ToString()).ToList(),
                0,
                1,
                new Dictionary<string, object?>
                {
                    ["choiceCards"] = costs.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
                });
            if (chosen.Count == 0) return;
            var cost = costs.First(card => card.Id.ToString() == chosen[0]);
            AtomicOps.RestCard(cost);
            if (cost.IsTapped) ctx.State.MarkPreventKO(ctx.Source.Id);
            return;
        }

        if (me.LifeArea.Count == 0 || ctx.State.NoEffectLifeToHandThisTurn.Contains(ctx.OwnerIndex)) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "鲛星【登场时】：将生命区最上方或最下方 1 张加入手牌，并 KO 对方最多 1 张费用不高于 1 的角色？"))
            return;

        int position = 0;
        if (me.LifeArea.Count > 1)
        {
            int option = await ctx.Prompts.ChooseOption(ctx.OwnerIndex,
                "选择加入手牌的生命卡", new[] { "最上方", "最下方" });
            position = option == 1 ? me.LifeArea.Count - 1 : 0;
        }
        var life = me.LifeArea[position];
        me.LifeArea.RemoveAt(position);
        life.IsLifeFaceUp = false;
        me.Hand.Add(life);

        int opponent = 1 - ctx.OwnerIndex;
        var candidates = ctx.State.Players[opponent].Characters
            .Where(card => ctx.State.CurrentCostOf(opponent, card) <= 1).ToList();
        var target = await ConfirmedMissingHelpers.ChooseUpToOne(
            ctx, "OpponentCharacter", "KO 对方最多 1 张费用不高于 1 的角色", candidates);
        if (target is not null)
            await AtomicOps.KOByEffectAsync(ctx.State, opponent, target, ctx.Prompts, ctx.OwnerIndex);
    }
}
