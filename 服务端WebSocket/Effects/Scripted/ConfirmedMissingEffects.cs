using GrandUMI.Cards;
using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>已确认缺失卡效共用的小型选择与战斗辅助。</summary>
internal static class ConfirmedMissingHelpers
{
    public static bool HasProperty(CardInstance card, string property)
        => card.Info.Property.Split('/', StringSplitOptions.RemoveEmptyEntries).Contains(property);

    public static CardInstance? BattleOpponent(GameState state, int owner, Guid selfId)
    {
        var battle = state.CurrentBattle;
        if (battle is null) return null;
        if (battle.AttackerCardId == selfId)
        {
            int defender = battle.DefenderPlayerIndex;
            if (battle.TargetIsLeader) return state.Players[defender].Leader;
            var targetId = battle.ReplacedByBlockerCardId ?? battle.TargetCardId;
            return targetId is null
                ? null
                : state.Players[defender].Characters.FirstOrDefault(card => card.Id == targetId.Value);
        }

        var defendedId = battle.ReplacedByBlockerCardId ?? battle.TargetCardId;
        if (defendedId != selfId) return null;
        int attacker = battle.AttackerPlayerIndex;
        var attackerSide = state.Players[attacker];
        return attackerSide.Leader.Id == battle.AttackerCardId
            ? attackerSide.Leader
            : attackerSide.Characters.FirstOrDefault(card => card.Id == battle.AttackerCardId);
    }

    public static async Task<CardInstance?> ChooseUpToOne(
        EffectContext ctx, string zone, string text, IReadOnlyList<CardInstance> cards)
    {
        if (cards.Count == 0) return null;
        var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, zone, text,
            cards.Select(card => card.Id.ToString()).ToList(), 0, 1,
            new Dictionary<string, object?>
            {
                ["choiceCards"] = cards.Select(card => new
                {
                    id = card.Id.ToString(),
                    number = card.Info.Number,
                }).ToList(),
            });
        return chosen.Count == 0 ? null : cards.FirstOrDefault(card => card.Id.ToString() == chosen[0]);
    }

    public static void LifeTopToHand(PlayerState player)
    {
        if (player.LifeArea.Count == 0) return;
        var card = player.LifeArea[0];
        player.LifeArea.RemoveAt(0);
        card.IsLifeFaceUp = false;
        player.Hand.Add(card);
    }
}

/// <summary>OP12-021 一本松：满足条件时不会因对方效果休息，并拥有阻挡者。</summary>
public sealed class OP12_021_Ipponmatsu : IScriptedEffect
{
    public string CardNumber => "OP12-021";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        Guid selfId = ctx.Source.Id;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantRestriction = RestrictionKind.CannotBeRested,
            Predicate = (state, side, card) =>
                side == owner
                && card.Id == selfId
                && ConfirmedMissingHelpers.HasProperty(state.Players[owner].Leader, "斩")
                && state.Players[owner].CostArea.Count(don => don.State == DonState.Rest) >= 6
                && EffectRuntime.CurrentActingSide == 1 - owner,
        });
        return Task.CompletedTask;
    }
}

/// <summary>OP12-036 罗罗诺亚·佐罗：斩属性对战时防 KO 并获得力量。</summary>
public sealed class OP12_036_RoronoaZoro : IScriptedEffect
{
    public string CardNumber => "OP12-036";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public Task Resolve(EffectContext ctx)
    {
        int owner = ctx.OwnerIndex;
        Guid selfId = ctx.Source.Id;
        ctx.State.ContinuousEffects.RemoveAll(effect => effect.SourceCardId == selfId.ToString());
        ctx.State.ContinuousEffects.Add(new ContinuousEffect
        {
            SourceCardId = selfId.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = 1000,
            KoGuard = "battle",
            Predicate = (state, side, card) =>
                side == owner
                && card.Id == selfId
                && ConfirmedMissingHelpers.HasProperty(state.Players[owner].Leader, "斩")
                && ConfirmedMissingHelpers.BattleOpponent(state, owner, selfId) is { } opponent
                && ConfirmedMissingHelpers.HasProperty(opponent, "斩"),
        });
        return Task.CompletedTask;
    }
}

/// <summary>OP12-072 卓夫：我方咚回卡组时，山智领袖令其本回合获得速攻。</summary>
public sealed class OP12_072_Zeff : IScriptedEffect
{
    public string CardNumber => "OP12-072";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnDonReturnedToDeck;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        int returnedOwner = ctx.Vars.TryGetValue("owner", out var value) && value is int owner ? owner : -1;
        if (returnedOwner == ctx.OwnerIndex && me.Leader.Info.NameIs("山智"))
            AtomicOps.GiveKeyword(ctx.Source, "速攻", KeywordDuration.ThisTurn, ctx.OwnerIndex);
        return Task.CompletedTask;
    }
}

/// <summary>OP12-081 克尔拉：攻击抽牌及对方高费/角色效果登场监听。</summary>
public sealed class OP12_081_Koala : IScriptedEffect
{
    public string CardNumber => "OP12-081";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.OnAttackDeclare || trigger == EffectTrigger.OnAllyCharEnter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnAttackDeclare)
        {
            var battle = ctx.State.CurrentBattle;
            if (battle?.AttackerCardId != ctx.Source.Id || !battle.TargetIsLeader) return;
            if (me.Characters.Count(card => card.Info.Cost >= 8) >= 2)
                AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        int enteredOwner = ctx.Vars.TryGetValue("owner", out var ownerValue) && ownerValue is int owner ? owner : -1;
        if (enteredOwner != 1 - ctx.OwnerIndex) return;
        if (!ctx.Vars.TryGetValue("cardId", out var idValue)
            || !Guid.TryParse(idValue?.ToString(), out var enteredId)) return;
        var entered = ctx.State.Players[enteredOwner].Characters.FirstOrDefault(card => card.Id == enteredId);
        if (entered is null) return;
        bool highCost = entered.Info.Cost >= 8;
        bool throughCharacterEffect = ctx.Vars.TryGetValue("effectSourceKind", out var kind)
            && string.Equals(kind?.ToString(), CardKind.Character.ToString(), StringComparison.Ordinal);
        if (!highCost && !throughCharacterEffect) return;

        string key = $"OP12-081-trigger:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key) || ctx.State.Players[enteredOwner].LifeArea.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "克尔拉【每回合1次】：让对方将生命区最上方 1 张卡牌加入手牌？")) return;
        me.TurnOnceUsed.Add(key);
        ConfirmedMissingHelpers.LifeTopToHand(ctx.State.Players[enteredOwner]);
    }
}

/// <summary>ST36-001 凯文迪修。</summary>
public sealed class ST36_001_Cavendish : IScriptedEffect
{
    public string CardNumber => "ST36-001";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Hand.Count == 0 || me.Deck.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "凯文迪修【KO时】：丢弃 1 张手牌，将卡组顶最多 1 张加入生命区顶？")) return;
        var discarded = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OwnHandDiscard",
            "选择必须丢弃的 1 张手牌", me.Hand);
        if (discarded is null) return;
        AtomicOps.DiscardHand(me, discarded);
        AtomicOps.AddLifeFromDeckTop(me, 1);
    }
}

/// <summary>ST36-002 基拉。</summary>
public sealed class ST36_002_Killer : IScriptedEffect
{
    public string CardNumber => "ST36-002";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.OnEnterField || trigger == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (ctx.State.CurrentTurnPlayer == ctx.OwnerIndex
                && me.Leader.Info.HasKeyword("基德海盗团"))
                AtomicOps.AddLifeFromDeckTop(me, 1);
            return;
        }

        if (ctx.State.Players[1 - ctx.OwnerIndex].LifeArea.Count <= 3 && me.Trash.Contains(ctx.Source))
            await AtomicOps.PlayFromTrashFree(ctx.State, ctx.OwnerIndex, ctx.Source);
    }
}

/// <summary>ST36-004 巴尔托洛梅奥。</summary>
public sealed class ST36_004_Bartolomeo : IScriptedEffect
{
    public string CardNumber => "ST36-004";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.OnEnterField;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        var costs = me.Hand.Where(card => card.Info.HasKeyword("超新星")).ToList();
        if (costs.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "巴尔托洛梅奥【登场时】：丢弃 1 张《超新星》手牌并抽 2 张？")) return;
        var discarded = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OwnHandDiscard",
            "选择必须丢弃的 1 张《超新星》卡牌", costs);
        if (discarded is null) return;
        AtomicOps.DiscardHand(me, discarded);
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
    }
}

/// <summary>ST36-005 尤斯塔斯·基德。</summary>
public sealed class ST36_005_EustassKid : IScriptedEffect
{
    public string CardNumber => "ST36-005";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.OnOppAttackDeclare || trigger == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        string key = $"ST36-005-{ctx.Trigger}:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key) || me.LifeArea.Count == 0) return;

        bool turnFaceUp = ctx.Trigger == EffectTrigger.ActivatedMain;
        var edgeCards = new List<CardInstance>();
        if (me.LifeArea[0].IsLifeFaceUp != turnFaceUp) edgeCards.Add(me.LifeArea[0]);
        if (me.LifeArea.Count > 1 && me.LifeArea[^1].IsLifeFaceUp != turnFaceUp) edgeCards.Add(me.LifeArea[^1]);
        if (edgeCards.Count == 0) return;

        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            if (!me.CostArea.Any(don => don.State == DonState.Rest)) return;
            if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                    "尤斯塔斯·基德【启动主要】：将生命顶或底翻至正面，赋予领袖 1 张休息咚？")) return;
            var life = await ChooseLifeEdge(ctx, edgeCards, "选择翻至正面朝上的生命牌");
            if (life is null) return;
            life.IsLifeFaceUp = true;
            AtomicOps.AttachDonFromCost(me, me.Leader.Id, 1, DonState.Rest);
            me.TurnOnceUsed.Add(key);
            return;
        }

        var redirectTargets = me.Characters
            .Where(card => card.Info.Power >= 5000 && card.MatchesName("尤斯塔斯·基德"))
            .ToList();
        if (redirectTargets.Count == 0 || ctx.State.CurrentBattle is null) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "尤斯塔斯·基德【对方的攻击时】：将生命顶或底翻至背面并变更攻击对象？")) return;
        var paidLife = await ChooseLifeEdge(ctx, edgeCards, "选择翻至背面朝下的生命牌");
        if (paidLife is null) return;
        var target = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OwnCharacter",
            "选择新的攻击对象“尤斯塔斯·基德”", redirectTargets);
        if (target is null) return;
        paidLife.IsLifeFaceUp = false;
        me.TurnOnceUsed.Add(key);
        ctx.State.CurrentBattle.TargetIsLeader = false;
        ctx.State.CurrentBattle.TargetCardId = target.Id;
    }

    private static async Task<CardInstance?> ChooseLifeEdge(
        EffectContext ctx, IReadOnlyList<CardInstance> cards, string text)
    {
        if (cards.Count == 1) return cards[0];
        int choice = await ctx.Prompts.ChooseOption(ctx.OwnerIndex, text, new[] { "生命区最上方", "生命区最下方" });
        var me = ctx.State.Players[ctx.OwnerIndex];
        var picked = choice == 0 ? me.LifeArea[0] : me.LifeArea[^1];
        return cards.Contains(picked) ? picked : null;
    }
}

/// <summary>EB03-008 云雀。</summary>
public sealed class EB03_008_Hibari : IScriptedEffect
{
    public string CardNumber => "EB03-008";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger is EffectTrigger.OnEnterField or EffectTrigger.OnAttackDeclare or EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            string key = $"EB03-008-act:{ctx.Source.Id}";
            if (me.TurnOnceUsed.Contains(key)) return;
            var target = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OpponentCharacter",
                "选择对方最多 1 张角色，本回合力量-1000", ctx.State.Players[1 - ctx.OwnerIndex].Characters);
            if (target is null) return;
            target.PowerModThisTurn -= 1000;
            me.TurnOnceUsed.Add(key);
            return;
        }

        var candidates = new List<CardInstance>();
        if (me.Leader.Info.HasKeyword("利刃")) candidates.Add(me.Leader);
        candidates.AddRange(me.Characters.Where(card => card.Info.HasKeyword("利刃")));
        var chosen = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OwnLeaderOrCharacter",
            "选择最多 1 张《利刃》领袖或角色，本回合可攻击活跃角色", candidates);
        if (chosen is not null)
            AtomicOps.GiveKeyword(chosen, "可攻击活跃", KeywordDuration.ThisTurn, ctx.OwnerIndex);
    }
}

/// <summary>EB04-016 鸟类。</summary>
public sealed class EB04_016_Bird : IScriptedEffect
{
    public string CardNumber => "EB04-016";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.ActivatedMain || trigger == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.ActivatedMain)
        {
            var resting = me.CostArea.Where(don => don.State == DonState.Rest).ToList();
            if (resting.Count > 0)
            {
                var chosen = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "OwnDon",
                    "选择最多 1 张休息咚转为活跃状态", resting.Select(don => don.Id.ToString()).ToList(), 0, 1,
                    new Dictionary<string, object?>
                    {
                        ["donChoices"] = resting.Select(don => new { id = don.Id.ToString(), state = don.State.ToString() }).ToList(),
                    });
                if (chosen.Count > 0)
                {
                    var don = resting.FirstOrDefault(item => item.Id.ToString() == chosen[0]);
                    if (don is not null) don.State = DonState.Active;
                }
            }
            ctx.State.NoActivateDonByCharacterEffectThisTurn.Add(ctx.OwnerIndex);
            return;
        }

        if (me.Characters.Count(card => card.Info.HasKeyword("海王类")) < 3) return;
        var candidates = ctx.State.Players[1 - ctx.OwnerIndex].Characters
            .Where(card => ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, card) <= 8)
            .ToList();
        var target = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OpponentCharacter",
            "选择对方最多 1 张费用不高于 8 的角色转为休息状态", candidates);
        if (target is not null) AtomicOps.RestCard(target);
    }
}

/// <summary>OP11-028 近海之王。</summary>
public sealed class OP11_028_LordOfTheCoast : IScriptedEffect
{
    public string CardNumber => "OP11-028";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.OnEnterField || trigger == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        var opponent = ctx.State.Players[1 - ctx.OwnerIndex];
        var candidates = opponent.Characters.Where(card => card.IsTapped
            && (ctx.Trigger != EffectTrigger.OnLifeRevealTrigger
                || ctx.State.CurrentCostOf(1 - ctx.OwnerIndex, card) <= 3)).ToList();
        var target = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OpponentCharacter",
            ctx.Trigger == EffectTrigger.OnLifeRevealTrigger
                ? "选择对方最多 1 张休息且费用不高于 3 的角色 KO"
                : "选择对方最多 1 张休息角色，使其下个重置阶段不转为活跃",
            candidates);
        if (target is null) return;
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
            AtomicOps.KO(ctx.State, 1 - ctx.OwnerIndex, target);
        else
            AtomicOps.PreventActivateNextReset(target);
    }
}

/// <summary>OP11-084 库赞。</summary>
public sealed class OP11_084_Kuzan : IScriptedEffect
{
    public string CardNumber => "OP11-084";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.OnEnterField || trigger == EffectTrigger.OnAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            AtomicOps.MillTop(me, 3);
            return;
        }
        var candidates = new List<CardInstance>();
        if (me.Leader.Info.HasKeyword("海军")) candidates.Add(me.Leader);
        candidates.AddRange(me.Characters.Where(card => card.Info.HasKeyword("海军")));
        var target = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OwnLeaderOrCharacter",
            "选择最多 1 张《海军》领袖或角色，本回合可攻击活跃角色", candidates);
        if (target is not null)
            AtomicOps.GiveKeyword(target, "可攻击活跃", KeywordDuration.ThisTurn, ctx.OwnerIndex);
    }
}

/// <summary>OP15-012 巴奇。</summary>
public sealed class OP15_012_Buggy : IScriptedEffect
{
    public string CardNumber => "OP15-012";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.OnAttackDeclare || trigger == EffectTrigger.OnKO;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }

        var candidates = new List<(int Owner, CardInstance Card)>();
        for (int owner = 0; owner < 2; owner++)
        {
            var side = ctx.State.Players[owner];
            if (!side.CostArea.Any(don => don.State == DonState.Rest)) continue;
            candidates.Add((owner, side.Leader));
            candidates.AddRange(side.Characters.Select(card => (owner, card)));
        }
        var target = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "LeaderOrCharacter",
            "选择最多 1 张领袖或角色，赋予其持有者的 1 张休息咚", candidates.Select(item => item.Card).ToList());
        if (target is null) return;
        var item = candidates.First(candidate => candidate.Card.Id == target.Id);
        AtomicOps.AttachDonFromCost(ctx.State.Players[item.Owner], target.Id, 1, DonState.Rest);
    }
}

/// <summary>OP15-037 是强是弱只用结果来说话。</summary>
public sealed class OP15_037_ResultsSpeak : IScriptedEffect
{
    public string CardNumber => "OP15-037";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.EventMain || trigger == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }
        var me = ctx.State.Players[ctx.OwnerIndex];
        var top = me.Deck.Take(5).ToList();
        if (top.Count == 0) return;
        var candidates = top.Where(card => card.Info.Number != CardNumber && card.Info.HasKeyword("东海")).ToList();
        var picked = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "LookTopReveal",
            "公开最多 1 张本卡以外的《东海》卡牌加入手牌", candidates);
        if (picked is not null)
        {
            me.Deck.Remove(picked);
            me.Hand.Add(picked);
        }
        var rest = top.Where(me.Deck.Contains).ToList();
        if (rest.Count > 1)
        {
            var order = await ctx.Prompts.ChooseCards(ctx.OwnerIndex, "ReorderDeckBottom",
                "按选择顺序将剩余卡牌放回卡组最下方", rest.Select(card => card.Id.ToString()).ToList(),
                rest.Count, rest.Count,
                new Dictionary<string, object?>
                {
                    ["choiceCards"] = rest.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
                });
            if (order.Count == rest.Count)
                rest = order.Select(id => rest.First(card => card.Id.ToString() == id)).ToList();
        }
        foreach (var card in rest)
        {
            me.Deck.Remove(card);
            me.Deck.Add(card);
        }
    }
}

/// <summary>OP15-038 这是命令。谁都不许违抗我!!</summary>
public sealed class OP15_038_ThisIsAnOrder : IScriptedEffect
{
    public string CardNumber => "OP15-038";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.EventMain || trigger == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        if (ctx.Trigger == EffectTrigger.EventMain)
        {
            int opponent = 1 - ctx.OwnerIndex;
            var candidates = ctx.State.Players[opponent].Characters.Where(card =>
                card.IsTapped
                && ctx.State.CurrentCostOf(opponent, card) <= 8
                && ctx.State.Players[opponent].AttachedDonCount(card.Id) >= 2).ToList();
            var target = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OpponentCharacter",
                "选择对方最多 1 张满足条件的角色，使其下个重置阶段不转为活跃", candidates);
            if (target is not null) AtomicOps.PreventActivateNextReset(target);
            return;
        }

        var me = ctx.State.Players[ctx.OwnerIndex];
        var candidatesCounter = new List<CardInstance>();
        if (me.Leader.MatchesName("克里克")) candidatesCounter.Add(me.Leader);
        candidatesCounter.AddRange(me.Characters.Where(card => card.MatchesName("克里克")));
        var counterTarget = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OwnLeaderOrCharacter",
            "选择最多 1 张“克里克”，本次战斗力量+4000", candidatesCounter);
        if (counterTarget is not null) AtomicOps.AddPowerThisBattle(counterTarget, 4000);
    }
}

/// <summary>OP15-041 欧隆布斯。</summary>
public sealed class OP15_041_Orlumbus : IScriptedEffect
{
    public string CardNumber => "OP15-041";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.OnKO || trigger == EffectTrigger.ActivatedMain;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnKO)
        {
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }
        string key = $"OP15-041-act:{ctx.Source.Id}";
        if (me.TurnOnceUsed.Contains(key) || me.Characters.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "欧隆布斯【启动主要】：将我方 1 张角色放回卡组底，使此角色本回合获得速攻？")) return;
        var cost = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OwnCharacter",
            "选择必须放回卡组最下方的 1 张角色", me.Characters);
        if (cost is null) return;
        AtomicOps.ReturnFieldToDeckBottom(ctx.State, ctx.OwnerIndex, cost);
        me.TurnOnceUsed.Add(key);
        if (me.Characters.Contains(ctx.Source))
            AtomicOps.GiveKeyword(ctx.Source, "速攻", KeywordDuration.ThisTurn, ctx.OwnerIndex);
    }
}

/// <summary>OP15-056 『燃烧之果』，我吃可以吗？</summary>
public sealed class OP15_056_FlameFlameFruit : IScriptedEffect
{
    public string CardNumber => "OP15-056";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.EventMain || trigger == EffectTrigger.OnLifeRevealTrigger;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
        if (ctx.Trigger == EffectTrigger.EventMain && me.Leader.MatchesName("路西"))
        {
            AtomicOps.GiveKeyword(me.Leader, "双重攻击", KeywordDuration.ThisTurn, ctx.OwnerIndex);
            AtomicOps.AddPowerThisTurn(me.Leader, 3000);
        }
        return Task.CompletedTask;
    }
}

/// <summary>OP15-057 德莱斯罗兹王国。</summary>
public sealed class OP15_057_DressrosaKingdom : IScriptedEffect
{
    public string CardNumber => "OP15-057";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.OnEnterField || trigger == EffectTrigger.OnOppAttackDeclare;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField)
        {
            if (me.Leader.Info.HasKeyword("德莱斯罗兹")) AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
            return;
        }
        if (ctx.Source.IsTapped) return;
        var discardCosts = me.Hand.Where(card => card.Info.Kind is CardKind.Event or CardKind.Stage).ToList();
        if (discardCosts.Count == 0) return;
        if (!await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
                "德莱斯罗兹王国【对方的攻击时】：休息此舞台并丢弃 1 张事件或舞台，使我方卡牌+2000？")) return;
        var discarded = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OwnHandDiscard",
            "选择必须丢弃的 1 张事件或舞台卡牌", discardCosts);
        if (discarded is null) return;
        AtomicOps.RestCard(ctx.Source);
        AtomicOps.DiscardHand(me, discarded);
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var target = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OwnLeaderOrCharacter",
            "选择我方最多 1 张领袖或角色，本次战斗力量+2000", targets);
        if (target is not null) AtomicOps.AddPowerThisBattle(target, 2000);
    }
}

/// <summary>OP15-084 豪格巴克医生。</summary>
public sealed class OP15_084_DrHogback : IScriptedEffect
{
    public string CardNumber => "OP15-084";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.OnEnterField || trigger == EffectTrigger.OnKO;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField && me.Leader.Info.HasKeyword("恐怖之船海盗团"))
            AtomicOps.MillTop(me, 5);
        else if (ctx.Trigger == EffectTrigger.OnKO && me.Hand.Count <= 6)
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 1);
        return Task.CompletedTask;
    }
}

/// <summary>OP15-115 冲击贝。</summary>
public sealed class OP15_115_ImpactDial : IScriptedEffect
{
    public string CardNumber => "OP15-115";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.EventMain || trigger == EffectTrigger.OnLifeRevealTrigger;

    public async Task Resolve(EffectContext ctx)
    {
        int opponent = 1 - ctx.OwnerIndex;
        var candidates = ctx.State.Players[opponent].Characters
            .Where(card => ctx.State.CurrentCostOf(opponent, card) <= 4).ToList();
        var target = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OpponentCharacter",
            "选择对方最多 1 张费用不高于 4 的角色 KO", candidates);
        if (target is not null) AtomicOps.KO(ctx.State, opponent, target);
        if (ctx.Trigger == EffectTrigger.EventMain)
            ConfirmedMissingHelpers.LifeTopToHand(ctx.State.Players[ctx.OwnerIndex]);
    }
}

/// <summary>OP16-057 我们的救世主!!巴奇船长!!</summary>
public sealed class OP16_057_OurSaviorCaptainBuggy : IScriptedEffect
{
    public string CardNumber => "OP16-057";
    public bool HandlesTrigger(EffectTrigger trigger) => trigger == EffectTrigger.EventCounter;

    public async Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (me.Characters.Count(card => card.MatchesName("因佩尔地狱的囚犯")) < 2) return;
        var targets = new List<CardInstance> { me.Leader };
        targets.AddRange(me.Characters);
        var target = await ConfirmedMissingHelpers.ChooseUpToOne(ctx, "OwnLeaderOrCharacter",
            "选择我方最多 1 张领袖或角色，本次战斗力量+4000", targets);
        if (target is not null) AtomicOps.AddPowerThisBattle(target, 4000);
    }
}

/// <summary>OP16-068 特拉法尔加·罗。</summary>
public sealed class OP16_068_TrafalgarLaw : IScriptedEffect
{
    public string CardNumber => "OP16-068";
    public bool HandlesTrigger(EffectTrigger trigger)
        => trigger == EffectTrigger.OnEnterField || trigger == EffectTrigger.OnAttackDeclare;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        if (ctx.Trigger == EffectTrigger.OnEnterField)
            AtomicOps.RefreshDonFromDeck(me, 1, DonState.Active);
        else if (me.Leader.Info.HasKeyword("堂吉诃德海盗团"))
            AtomicOps.AddPowerThisTurn(ctx.Source, 3000);
        return Task.CompletedTask;
    }
}
