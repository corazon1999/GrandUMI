using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;

namespace GrandUMI.Effects.Scripted;

/// <summary>OP17 全卡集共用结算器。每张卡仍由文件末尾的独立 IScriptedEffect 类注册。</summary>
internal static class OP17Effects
{
    private static PlayerState Me(EffectContext c) => c.State.Players[c.OwnerIndex];
    private static PlayerState Opp(EffectContext c) => c.State.Players[1 - c.OwnerIndex];
    private static bool LeaderHas(EffectContext c, string keyword) => Me(c).Leader.Info.HasKeyword(keyword);
    private static bool LeaderIs(EffectContext c, string name) => Me(c).Leader.MatchesName(name);

    private static Dictionary<string, object?> ChoiceCards(IEnumerable<CardInstance> cards) => new()
    {
        ["choiceCards"] = cards.Select(x => new { id = x.Id.ToString(), number = x.Info.Number }).ToList(),
    };

    private static async Task<List<CardInstance>> Pick(
        EffectContext c, int chooser, string kind, string text,
        IEnumerable<CardInstance> source, int min, int max,
        IEnumerable<CardInstance>? displaySource = null)
    {
        var cards = source.DistinctBy(x => x.Id).ToList();
        if (cards.Count == 0 || max <= 0) return new();
        max = Math.Min(max, cards.Count);
        min = Math.Min(min, max);
        var ids = await c.Prompts.ChooseCards(chooser, kind, text,
            cards.Select(x => x.Id.ToString()).ToList(), min, max, ChoiceCards(displaySource ?? cards));
        var selected = new List<CardInstance>();
        foreach (var id in ids)
        {
            var card = cards.FirstOrDefault(x => x.Id.ToString() == id);
            if (card is not null && selected.All(x => x.Id != card.Id)) selected.Add(card);
        }
        return selected;
    }

    private static async Task<bool> DiscardOwn(EffectContext c, int n, string text)
    {
        var me = Me(c);
        if (me.Hand.Count < n) return false;
        var picked = await Pick(c, c.OwnerIndex, "OwnHandDiscard", text, me.Hand, n, n);
        if (picked.Count < n) return false;
        bool old = EffectRuntime.PayingCost;
        EffectRuntime.PayingCost = true;
        try { foreach (var card in picked) AtomicOps.DiscardHand(me, card); }
        finally { EffectRuntime.PayingCost = old; }
        return true;
    }

    private static async Task<bool> DiscardOwnFiltered(
        EffectContext c, Func<CardInstance, bool> filter, int n, string text, bool revealOnly = false)
    {
        var candidates = Me(c).Hand.Where(filter).ToList();
        if (candidates.Count < n) return false;
        var picked = await Pick(c, c.OwnerIndex, revealOnly ? "RevealOwnHand" : "OwnHandDiscard",
            text, candidates, n, n);
        if (picked.Count < n) return false;
        c.Engine?.BroadcastReveal(c.OwnerIndex, picked.Select(x => x.Info.Number).ToList());
        if (!revealOnly)
        {
            bool old = EffectRuntime.PayingCost;
            EffectRuntime.PayingCost = true;
            try { foreach (var card in picked) AtomicOps.DiscardHand(Me(c), card); }
            finally { EffectRuntime.PayingCost = old; }
        }
        return true;
    }

    private static bool RestActiveDon(EffectContext c, int n)
    {
        var active = Me(c).CostArea.Where(x => x.State == DonState.Active).Take(n).ToList();
        if (active.Count < n) return false;
        foreach (var d in active) d.State = DonState.Rest;
        return true;
    }

    private static int ActivateRestDon(PlayerState p, int n)
    {
        int count = 0;
        foreach (var d in p.CostArea.Where(x => x.State == DonState.Rest).Take(n))
        {
            d.State = DonState.Active;
            count++;
        }
        return count;
    }

    private static IEnumerable<CardInstance> OwnLeaderAndCharacters(EffectContext c)
        => new[] { Me(c).Leader }.Concat(Me(c).Characters);

    private static IEnumerable<CardInstance> OppLeaderAndCharacters(EffectContext c)
        => new[] { Opp(c).Leader }.Concat(Opp(c).Characters);

    private static bool AnyCostAtLeast(EffectContext c, int cost)
        => c.State.Players.SelectMany(p => p.Characters)
            .Any(x => c.State.CurrentCostOf(x) >= cost);

    private static async Task<List<CardInstance>> ChooseOppChars(
        EffectContext c, Func<CardInstance, bool> filter, int max, string text)
        => await Pick(c, c.OwnerIndex, "OpponentCharacter", text, Opp(c).Characters.Where(filter), 0, max);

    private static async Task<List<CardInstance>> ChooseOwnChars(
        EffectContext c, Func<CardInstance, bool> filter, int max, string text)
        => await Pick(c, c.OwnerIndex, "OwnCharacter", text, Me(c).Characters.Where(filter), 0, max);

    private static async Task KOByEffect(EffectContext c, IEnumerable<CardInstance> cards)
    {
        var victims = cards.Where(x => Opp(c).Characters.Contains(x)).DistinctBy(x => x.Id).ToList();
        if (victims.Count == 0) return;
        await AtomicOps.KOCardsByEffectAsync(c.State, 1 - c.OwnerIndex, victims, c.Prompts, c.OwnerIndex);
    }

    private static async Task SearchTop(
        EffectContext c, int count, Func<CardInstance, bool> filter, string text,
        bool trashRemainder = false, bool requireOne = false, bool reorderRemainder = false)
    {
        var me = Me(c);
        var top = me.Deck.Take(count).ToList();
        if (top.Count == 0) return;
        var candidates = top.Where(filter).ToList();
        // 即使没有符合条件的牌，也必须让玩家确认看过的牌。
        // 若直接跳过 Prompt，客户端不会收到 choiceCards，表现为【登场时】检索
        // 偶发未发动（恰好顶牌没有可加入手牌的目标时）。
        var pickedIds = await c.Prompts.ChooseCards(c.OwnerIndex, "LookTop", text,
            candidates.Select(card => card.Id.ToString()).ToList(),
            requireOne && candidates.Count > 0 ? 1 : 0, 1, ChoiceCards(top));
        var picked = pickedIds
            .Select(id => candidates.FirstOrDefault(card => card.Id.ToString() == id))
            .Where(card => card is not null)
            .Cast<CardInstance>()
            .DistinctBy(card => card.Id)
            .Take(1)
            .ToList();

        foreach (var card in top) me.Deck.Remove(card);
        if (picked.Count > 0)
        {
            me.Hand.Add(picked[0]);
            top.Remove(picked[0]);
        }
        if (trashRemainder) me.Trash.AddRange(top);
        else if (!reorderRemainder || top.Count <= 1) me.Deck.AddRange(top);
        else
        {
            var orderedIds = await c.Prompts.ChooseCards(c.OwnerIndex, "ReorderToDeckBottom",
                "将剩余卡牌自选顺序放回卡组最下方（先选的牌在较上方）",
                top.Select(card => card.Id.ToString()).ToList(), 0, top.Count,
                new Dictionary<string, object?>
                {
                    ["choiceCards"] = top.Select(card => new { id = card.Id.ToString(), number = card.Info.Number }).ToList(),
                    ["allowDefaultOrder"] = true,
                });
            var ordered = orderedIds
                .Select(id => top.FirstOrDefault(card => card.Id.ToString() == id))
                .Where(card => card is not null).Cast<CardInstance>().Distinct().ToList();
            ordered.AddRange(top.Where(card => !ordered.Contains(card)));
            me.Deck.AddRange(ordered);
        }
        if (picked.Count > 0)
            c.Engine?.BroadcastReveal(c.OwnerIndex, new[] { picked[0].Info.Number });
    }

    private static async Task<CardInstance?> PlayOneFromHand(
        EffectContext c, Func<CardInstance, bool> filter, string text)
    {
        var picked = await Pick(c, c.OwnerIndex, "OwnHandCharacter", text,
            Me(c).Hand.Where(x => x.Info.Kind == CardKind.Character && filter(x)), 0, 1);
        if (picked.Count == 0) return null;
        await AtomicOps.PlayFromHandFree(c.State, c.OwnerIndex, picked[0]);
        return picked[0];
    }

    private static async Task<CardInstance?> PlayOneFromTrash(
        EffectContext c, Func<CardInstance, bool> filter, string text)
    {
        var picked = await Pick(c, c.OwnerIndex, "OwnTrashCharacter", text,
            Me(c).Trash.Where(x => x.Info.Kind == CardKind.Character && filter(x)), 0, 1);
        if (picked.Count == 0) return null;
        await AtomicOps.PlayFromTrashFree(c.State, c.OwnerIndex, picked[0]);
        return picked[0];
    }

    private static async Task<CardInstance?> PlayOneFromHandOrTrash(
        EffectContext c, Func<CardInstance, bool> filter, string text)
    {
        var candidates = Me(c).Hand.Concat(Me(c).Trash)
            .Where(x => x.Info.Kind == CardKind.Character && filter(x)).ToList();
        var picked = await Pick(c, c.OwnerIndex, "OwnHandOrTrashCharacter", text, candidates, 0, 1);
        if (picked.Count == 0) return null;
        if (Me(c).Hand.Contains(picked[0])) await AtomicOps.PlayFromHandFree(c.State, c.OwnerIndex, picked[0]);
        else await AtomicOps.PlayFromTrashFree(c.State, c.OwnerIndex, picked[0]);
        return picked[0];
    }

    private static void RegisterContinuous(EffectContext c, params ContinuousEffect[] effects)
    {
        string id = c.Source.Id.ToString();
        c.State.ContinuousEffects.RemoveAll(x => x.SourceCardId == id);
        c.State.ContinuousEffects.AddRange(effects);
    }

    private static ContinuousEffect SelfPower(EffectContext c, int delta, Func<GameState, bool> condition)
    {
        var id = c.Source.Id;
        int owner = c.OwnerIndex;
        return new ContinuousEffect
        {
            SourceCardId = id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            PowerDelta = delta,
            Predicate = (s, side, card) => side == owner && card.Id == id && condition(s),
        };
    }

    private static ContinuousEffect SelfCost(EffectContext c, int delta, Func<GameState, bool> condition)
    {
        var id = c.Source.Id;
        int owner = c.OwnerIndex;
        return new ContinuousEffect
        {
            SourceCardId = id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            CostDelta = delta,
            Predicate = (s, side, card) => side == owner && card.Id == id
                && s.Players[owner].Characters.Any(x => x.Id == id) && condition(s),
        };
    }

    private static async Task DiscardOpponentChosen(EffectContext c, int n)
    {
        var opp = Opp(c);
        int actual = Math.Min(n, opp.Hand.Count);
        if (actual <= 0) return;
        var picked = await Pick(c, 1 - c.OwnerIndex, "OwnHandDiscard", $"丢弃 {actual} 张手牌",
            opp.Hand, actual, actual);
        if (picked.Count < actual) picked = opp.Hand.Take(actual).ToList();
        foreach (var card in picked) AtomicOps.DiscardHand(opp, card);
    }

    private static void TrashSelfAsCost(EffectContext c)
    {
        var me = Me(c);
        foreach (var d in me.CostArea.Where(x => x.AttachedToCardId == c.Source.Id))
        {
            d.State = DonState.Rest;
            d.AttachedToCardId = null;
        }
        me.Characters.Remove(c.Source);
        if (ReferenceEquals(me.StageCard, c.Source)) me.StageCard = null;
        c.State.ContinuousEffects.RemoveAll(x => x.SourceCardId == c.Source.Id.ToString());
        me.Trash.Add(c.Source);
    }

    private static async Task PlaySelfFromTrash(EffectContext c)
    {
        if (Me(c).Trash.Contains(c.Source)) await AtomicOps.PlayFromTrashFree(c.State, c.OwnerIndex, c.Source);
    }

    private static async Task<List<CardInstance>> ChooseByTotalCost(
        EffectContext c, IEnumerable<CardInstance> source, int budget, int max, string text,
        bool distinctNames = false)
    {
        var remaining = source.DistinctBy(x => x.Id).ToList();
        var selected = new List<CardInstance>();
        int left = budget;
        while (selected.Count < max)
        {
            var eligible = remaining.Where(x => c.State.CurrentCostOf(x) <= left
                && (!distinctNames || selected.All(y => y.Info.Name != x.Info.Name))).ToList();
            if (eligible.Count == 0) break;
            var one = await Pick(c, c.OwnerIndex, "ChooseByTotalCost",
                $"{text}（剩余合计费用 {left}，可结束选择）", eligible, 0, 1);
            if (one.Count == 0) break;
            selected.Add(one[0]);
            remaining.Remove(one[0]);
            left -= c.State.CurrentCostOf(one[0]);
        }
        return selected;
    }

    public static async Task Resolve(EffectContext c)
    {
        switch (c.Source.Info.Number)
        {
            case "OP17-001": await C001(c); break;
            case "OP17-002": C002(c); break;
            case "OP17-003": await C003(c); break;
            case "OP17-004": await C004(c); break;
            case "OP17-005": C005(c); break;
            case "OP17-007": await C007(c); break;
            case "OP17-008": C008(c); break;
            case "OP17-009": await C009(c); break;
            case "OP17-010": await C010(c); break;
            case "OP17-011": await C011(c); break;
            case "OP17-012": await C012(c); break;
            case "OP17-013": await C013(c); break;
            case "OP17-014": await C014(c); break;
            case "OP17-015": await C015(c); break;
            case "OP17-016": await C016(c); break;
            case "OP17-017": await C017(c); break;
            case "OP17-018": await C018(c); break;
            case "OP17-019": await C019(c); break;
            case "OP17-020": await C020(c); break;
            case "OP17-021": await C021(c); break;
            case "OP17-022": C022(c); break;
            case "OP17-023": await C023(c); break;
            case "OP17-024": await C024(c); break;
            case "OP17-025": await C025(c); break;
            case "OP17-026": await C026(c); break;
            case "OP17-027": await C027(c); break;
            case "OP17-028": await C028(c); break;
            case "OP17-029": await C029(c); break;
            case "OP17-030": await C030(c); break;
            case "OP17-031": await C031(c); break;
            case "OP17-032": await C032(c); break;
            case "OP17-033": await C033(c); break;
            case "OP17-034": C034(c); break;
            case "OP17-036": await C036(c); break;
            case "OP17-037": await C037(c); break;
            case "OP17-038": await C038(c); break;
            case "OP17-039": await C039(c); break;
            case "OP17-040": await C040(c); break;
            case "OP17-041": await C041(c); break;
            case "OP17-042": await C042(c); break;
            case "OP17-043": await C043(c); break;
            case "OP17-044": await C044(c); break;
            case "OP17-045": await C045(c); break;
            case "OP17-046": await C046(c); break;
            case "OP17-047": await C047(c); break;
            case "OP17-048": await C048(c); break;
            case "OP17-049": await C049(c); break;
            case "OP17-050": await C050(c); break;
            case "OP17-052": await C052(c); break;
            case "OP17-053": await C053(c); break;
            case "OP17-054": await C054(c); break;
            case "OP17-055": await C055(c); break;
            case "OP17-056": await C056(c); break;
            case "OP17-057": await C057(c); break;
            case "OP17-058": await C058(c); break;
            case "OP17-059": await C059(c); break;
            case "OP17-060": await C060(c); break;
            case "OP17-061": await C061(c); break;
            case "OP17-062": await C062(c); break;
            case "OP17-063": await C063(c); break;
            case "OP17-064": await C064(c); break;
            case "OP17-065": await C065(c); break;
            case "OP17-066": await C066(c); break;
            case "OP17-067": await C067(c); break;
            case "OP17-068": await C068(c); break;
            case "OP17-069": await C069(c); break;
            case "OP17-071": await C071(c); break;
            case "OP17-072": await C072(c); break;
            case "OP17-073": await C073(c); break;
            case "OP17-074": C074(c); break;
            case "OP17-075": await C075(c); break;
            case "OP17-076": await C076(c); break;
            case "OP17-077": await C077(c); break;
            case "OP17-078": await C078(c); break;
            case "OP17-079": C079(c); break;
            case "OP17-080": await C080(c); break;
            case "OP17-081": await C081(c); break;
            case "OP17-082": await C082(c); break;
            case "OP17-083": C083(c); break;
            case "OP17-084": await C084(c); break;
            case "OP17-085": await C085(c); break;
            case "OP17-086": await C086(c); break;
            case "OP17-087": await C087(c); break;
            case "OP17-089": await C089(c); break;
            case "OP17-090": await C090(c); break;
            case "OP17-091": await C091(c); break;
            case "OP17-092": await C092(c); break;
            case "OP17-093": await C093(c); break;
            case "OP17-094": C094(c); break;
            case "OP17-095": await C095(c); break;
            case "OP17-096": await C096(c); break;
            case "OP17-097": await C097(c); break;
            case "OP17-098": await C098(c); break;
            case "OP17-099": await C099(c); break;
            case "OP17-101": await C101(c); break;
            case "OP17-102": await C102(c); break;
            case "OP17-103": await C103(c); break;
            case "OP17-104": await C104(c); break;
            case "OP17-105": await C105(c); break;
            case "OP17-106": await C106(c); break;
            case "OP17-107": await C107(c); break;
            case "OP17-108": await C108(c); break;
            case "OP17-109": await C109(c); break;
            case "OP17-110": await C110(c); break;
            case "OP17-111": await C111(c); break;
            case "OP17-112": await C112(c); break;
            case "OP17-113": await C113(c); break;
            case "OP17-114": await C114(c); break;
            case "OP17-115": await C115(c); break;
            case "OP17-116": await C116(c); break;
            case "OP17-117": await C117(c); break;
            case "OP17-118": await C118(c); break;
            case "OP17-119": await C119(c); break;
        }
    }

    private static async Task C001(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnOppAttackDeclare) return;
        string key = $"OP17-001-opp-attack:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key) || Me(c).Hand.Count == 0) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张手牌，使我方1张领袖或角色本次战斗力量+4000？")) return;
        if (!await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择力量+4000的卡牌",
            OwnLeaderAndCharacters(c), 0, 1);
        if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 4000);
        Me(c).TurnOnceUsed.Add(key);
    }

    private static void C002(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        int owner = c.OwnerIndex;
        RegisterContinuous(c, SelfPower(c, 3000, s => s.CurrentTurnPlayer != owner));
    }

    private static async Task C003(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField || !(LeaderIs(c, "爱德华·纽哥特") || LeaderHas(c, "和之国"))) return;
        var pick = await ChooseOppChars(c, x => x.IsTapped, 1, "选择1张休息角色，本回合力量-6000");
        if (pick.Count > 0) AtomicOps.AddPowerThisTurn(pick[0], -6000);
    }

    private static async Task C004(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        var pick = await ChooseOwnChars(c,
            x => x.Info.HasKeyword("和之国") || x.Info.HasKeyword("白胡子海盗团"), 1,
            "选择1张《和之国》或《白胡子海盗团》角色获得【速攻】");
        if (pick.Count > 0) AtomicOps.GiveKeyword(pick[0], "速攻", KeywordDuration.ThisTurn, c.OwnerIndex);
    }

    private static void C005(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField && Me(c).Leader.Info.ColorList.Length == 1)
            AtomicOps.SetOriginalPowerUntilOppEnd(Me(c).Leader, 8000, c.OwnerIndex);
    }

    private static async Task C007(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField || !(LeaderIs(c, "爱德华·纽哥特") || LeaderHas(c, "和之国"))) return;
        await PlayOneFromHand(c, x => x.Info.Power <= 6000
            && (x.Info.HasKeyword("和之国") || x.Info.HasKeyword("白胡子海盗团")),
            "将手牌中最多1张力量≤6000的《和之国》或《白胡子海盗团》角色登场");
    }

    private static void C008(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField && LeaderIs(c, "爱德华·纽哥特"))
            AtomicOps.SetOriginalPowerUntilOppEnd(Me(c).Leader, 8000, c.OwnerIndex);
    }

    private static async Task C009(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        int owner = c.OwnerIndex;
        RegisterContinuous(c, SelfPower(c, 3000, s => s.CurrentTurnPlayer != owner));
        var pick = await ChooseOppChars(c, x => x.Info.Power <= 2000, 1, "选择原本力量≤2000的角色KO");
        await KOByEffect(c, pick);
    }

    private static async Task C010(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.ActivatedMain || !Me(c).Characters.Contains(c.Source)) return;
        string key = $"OP17-010-act:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key)
            || !Opp(c).Characters.Any(x => c.State.CurrentPowerOf(1 - c.OwnerIndex, x) >= 10000)
            || Me(c).Characters.Any(x => x.Id != c.Source.Id && x.MatchesName("佛萨"))) return;

        AtomicOps.GiveKeyword(c.Source, "阻挡者", KeywordDuration.UntilNextOpponentEndPhase, c.OwnerIndex);
        AtomicOps.AddPowerUntilOppEnd(c.Source, 2000, c.OwnerIndex);
        Me(c).TurnOnceUsed.Add(key);
        await Task.CompletedTask;
    }

    private static async Task C011(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnAttackDeclare || Me(c).AttachedDonCount(c.Source.Id) < 2) return;
        var pick = await ChooseOppChars(c, _ => true, 1, "选择对方最多1张角色，本回合力量-4000");
        if (pick.Count > 0) AtomicOps.AddPowerThisTurn(pick[0], -4000);
    }

    private static async Task C014(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField)
        {
            await KOByEffect(c, await ChooseOppChars(c, x => x.Info.Power <= 2000, 1,
                "选择最多1张原本力量≤2000的角色KO"));
            return;
        }
        if (c.Trigger != EffectTrigger.OnOppAttackDeclare || !Me(c).Characters.Contains(c.Source)) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将霍怀迪贝放置到废弃区，使我方领袖本次战斗力量+1000？")) return;
        TrashSelfAsCost(c);
        AtomicOps.AddPowerThisBattle(Me(c).Leader, 1000);
    }

    private static async Task C016(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        await KOByEffect(c, await ChooseOppChars(c, x => x.Info.Power <= 2000, 2, "选择最多2张原本力量≤2000的角色KO"));
    }

    private static async Task C018(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.EventMain)
        {
            if (Me(c).ActiveDonCount < 2
                || !await c.Prompts.ConfirmOptional(c.OwnerIndex, "将2张咚!!转为休息状态，KO对方最多1张舞台？")) return;
            RestActiveDon(c, 2);
            if (Opp(c).StageCard is { } stage) AtomicOps.KO(c.State, 1 - c.OwnerIndex, stage);
            return;
        }
        if (c.Trigger != EffectTrigger.EventCounter
            || Me(c).Characters.Count(x => c.State.CurrentPowerOf(c.OwnerIndex, x) >= 8000) < 2) return;
        var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择本次战斗力量+4000的卡牌",
            OwnLeaderAndCharacters(c), 0, 1);
        if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 4000);
    }

    private static async Task C012(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnKO) return;
        await PlayOneFromHand(c, x => x.Info.Cost == 1 && x.Info.HasKeyword("白胡子海盗团"),
            "将手牌中最多1张费用1的《白胡子海盗团》卡牌登场");
    }

    private static async Task C013(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField || !LeaderIs(c, "爱德华·纽哥特")) return;
        var pick = await ChooseOppChars(c, x => x.IsTapped, 1, "选择1张休息角色，本回合力量-6000");
        if (pick.Count > 0) AtomicOps.AddPowerThisTurn(pick[0], -6000);
    }

    private static async Task C015(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnAllyWillLeaveField)
        {
            if (!c.Vars.TryGetValue("victimId", out var raw) || raw is not string id || !Guid.TryParse(id, out var victimId)) return;
            if (victimId == c.Source.Id || !Me(c).Characters.Contains(c.Source)) return;
            if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "KO马尔高，使将要因对方效果离场的角色不离场？")) return;
            bool ko = await AtomicOps.KOByEffectAsync(c.State, c.OwnerIndex, c.Source, c.Prompts, c.OwnerIndex);
            if (ko) c.State.MarkPreventEffectLeaveBatch(c.OwnerIndex, victimId, _ => true);
            return;
        }
        if (c.Trigger != EffectTrigger.OnKO || !Me(c).Trash.Contains(c.Source)) return;
        var candidates = Me(c).Hand.Where(x => x.Info.HasKeyword("白胡子海盗团")).ToList();
        if (candidates.Count == 0 || !await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张《白胡子海盗团》卡牌，将马尔高从废弃区登场？")) return;
        if (await DiscardOwnFiltered(c, x => x.Info.HasKeyword("白胡子海盗团"), 1, "选择丢弃1张《白胡子海盗团》卡牌"))
            await PlaySelfFromTrash(c);
    }

    private static async Task C017(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.EventCounter) return;
        var own = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择《白胡子海盗团》领袖或角色，本次战斗力量+2000",
            OwnLeaderAndCharacters(c).Where(x => x.Info.HasKeyword("白胡子海盗团")), 0, 1);
        if (own.Count > 0) AtomicOps.AddPowerThisBattle(own[0], 2000);
        var opp = await Pick(c, c.OwnerIndex, "OpponentLeaderOrCharacter", "选择对方1张领袖或角色，本回合力量-2000",
            OppLeaderAndCharacters(c), 0, 1);
        if (opp.Count > 0) AtomicOps.AddPowerThisTurn(opp[0], -2000);
    }

    private static async Task C019(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.EventMain)
            await SearchTop(c, 5, x => x.Info.HasKeyword("白胡子海盗团"), "公开1张《白胡子海盗团》卡牌加入手牌", requireOne: true);
        else if (c.Trigger == EffectTrigger.OnLifeRevealTrigger)
            AtomicOps.AddPowerThisTurn(Me(c).Leader, 1000);
    }

    private static async Task C020(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.ActivatedMain) return;
        string key = $"OP17-020-act:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key)) return;
        var options = new List<string>();
        if (Me(c).Hand.Count > 0) options.Add("丢弃1张手牌");
        if (Me(c).ActiveDonCount > 0) options.Add("将1张咚!!转为休息状态");
        if (options.Count == 0) return;
        int option = options.Count == 1 ? 0 : await c.Prompts.ChooseOption(c.OwnerIndex, "选择发动成本", options);
        if (options[option].StartsWith("丢弃"))
        {
            if (!await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        }
        else if (!RestActiveDon(c, 1)) return;
        var pick = await ChooseOppChars(c, x => x.IsTapped, 1, "选择下个重置阶段不会活跃的休息角色");
        if (pick.Count > 0) AtomicOps.PreventActivateNextReset(pick[0]);
        Me(c).TurnOnceUsed.Add(key);
    }

    private static void C022(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        ActivateRestDon(Me(c), 2);
        foreach (var ch in Opp(c).Characters) AtomicOps.RestCard(ch);
    }

    private static async Task C021(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnAllyWillLeaveField
            || !c.Vars.TryGetValue("victimId", out var raw) || raw is not string id || !Guid.TryParse(id, out var victimId)) return;
        var victim = Me(c).Characters.FirstOrDefault(x => x.Id == victimId);
        if (victim is null || !victim.Info.HasKeywordContaining("红发海盗团")) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将我方1张卡牌转为休息状态，使《红发海盗团》角色不离场？")) return;
        if (await AtomicOps.PromptRestOwnCards(c, 1, "选择我方1张卡牌转为休息状态", optional: true))
            c.State.MarkPreventEffectLeaveBatch(c.OwnerIndex, victimId,
                x => x.Info.HasKeywordContaining("红发海盗团"));
    }

    private static async Task C023(EffectContext c)
    {
        if (c.Trigger is not (EffectTrigger.PreKO or EffectTrigger.OnAllyWillBeKOd)
            || !Me(c).Characters.Contains(c.Source) || c.Source.IsTapped) return;

        CardInstance? victim;
        if (c.Trigger == EffectTrigger.PreKO) victim = c.Source;
        else if (c.Vars.TryGetValue("victimId", out var raw) && raw is string id && Guid.TryParse(id, out var victimId))
            victim = Me(c).Characters.FirstOrDefault(x => x.Id == victimId);
        else return;

        if (victim is null || !(victim.Info.HasKeyword("东海") || victim.Info.HasKeyword("草帽一伙"))) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将奈美转为休息状态，使该角色不被KO？")) return;
        AtomicOps.RestCard(c.Source);
        c.State.MarkPreventEffectLeaveBatch(c.OwnerIndex, victim.Id,
            x => x.Info.HasKeyword("东海") || x.Info.HasKeyword("草帽一伙"), isKoReplacement: true);
    }

    private static async Task C024(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        var pick = await ChooseOppChars(c, x => !x.IsTapped, 1, "选择对方1张角色转为休息状态");
        if (pick.Count > 0) AtomicOps.RestCard(pick[0]);
    }

    private static async Task C025(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnKO)
        {
            await KOByEffect(c, await ChooseOppChars(c, x => x.IsTapped && c.State.CurrentCostOf(x) <= 6, 1,
                "选择1张休息状态且费用≤6的角色KO"));
            return;
        }
        if (c.Trigger != EffectTrigger.ActivatedMain || !LeaderIs(c, "杰克斯")) return;
        string key = $"OP17-025-act:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key)) return;
        AtomicOps.AttachDonFromCost(Me(c), Me(c).Leader.Id, 1, DonState.Rest);
        Me(c).TurnOnceUsed.Add(key);
    }

    private static async Task C026(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnKO) { AtomicOps.Draw(c.State, c.OwnerIndex, 1); return; }
        if (c.Trigger != EffectTrigger.OnAttackDeclare || !LeaderHas(c, "红发海盗团")) return;
        var pick = await ChooseOppChars(c, x => c.State.CurrentCostOf(x) <= 2 && !x.IsTapped, 1, "选择费用≤2的角色转为休息状态");
        if (pick.Count > 0) AtomicOps.RestCard(pick[0]);
    }

    private static async Task C027(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField || !LeaderHas(c, "红发海盗团")) return;
        AtomicOps.Draw(c.State, c.OwnerIndex, 1);
        foreach (var card in await ChooseOppChars(c, x => !x.IsTapped, 2, "选择最多2张角色转为休息状态")) AtomicOps.RestCard(card);
    }

    private static async Task C028(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField)
            await KOByEffect(c, await ChooseOppChars(c, x => x.IsTapped && c.State.CurrentCostOf(x) <= 6, 1,
                "选择1张休息状态且费用≤6的角色KO"));
    }

    private static async Task C029(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        ActivateRestDon(Me(c), 1);
        foreach (var card in await ChooseOppChars(c, x => c.State.CurrentCostOf(x) <= 2 && !x.IsTapped, 2,
            "选择最多2张费用≤2的角色转为休息状态")) AtomicOps.RestCard(card);
    }

    private static async Task C030(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField)
        {
            if (Me(c).ActiveDonCount > 0
                && await c.Prompts.ConfirmOptional(c.OwnerIndex, "将1张咚!!转为休息状态，使此角色本回合获得【速攻】？")
                && RestActiveDon(c, 1))
                AtomicOps.GiveKeyword(c.Source, "速攻", KeywordDuration.ThisTurn, c.OwnerIndex);
            return;
        }
        if (c.Trigger != EffectTrigger.ActivatedMain || Me(c).Hand.Count > 5) return;
        string key = $"OP17-030-act:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key)) return;
        ActivateRestDon(Me(c), 1);
        Me(c).TurnOnceUsed.Add(key);
    }

    private static async Task C031(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField)
        {
            AtomicOps.Draw(c.State, c.OwnerIndex, 1);
            foreach (var card in await ChooseOppChars(c, x => c.State.CurrentCostOf(x) <= 8 && !x.IsTapped, 1,
                "选择费用≤8的角色转为休息状态")) AtomicOps.RestCard(card);
        }
        else if (c.Trigger == EffectTrigger.OnMyTurnEnd)
        {
            foreach (var card in await ChooseOwnChars(c, x => x.Info.HasKeyword("红发海盗团") && x.IsTapped, 1,
                "选择1张《红发海盗团》角色转为活跃状态")) AtomicOps.ActivateCard(card);
        }
    }

    private static async Task C032(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField)
            await SearchTop(c, 3, x => x.Info.HasKeywordContaining("红发海盗团"), "公开最多1张《红发海盗团》卡牌加入手牌");
    }

    private static async Task C033(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField)
        {
            await SearchTop(c, 3, x => x.Info.HasKeywordContaining("红发海盗团"), "公开最多1张《红发海盗团》卡牌加入手牌");
            return;
        }
        if (c.Trigger != EffectTrigger.OnOppAttackDeclare || !Me(c).Characters.Contains(c.Source)) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将幸运·鲁放置废弃区，使对方1张领袖或角色转为休息状态？")) return;
        TrashSelfAsCost(c);
        var pick = await Pick(c, c.OwnerIndex, "OpponentLeaderOrCharacter", "选择对方1张领袖或角色转为休息状态",
            OppLeaderAndCharacters(c).Where(x => !x.IsTapped), 0, 1);
        if (pick.Count > 0) AtomicOps.RestCard(pick[0]);
    }

    private static void C034(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.ActivatedMain || c.State.CurrentPowerOf(1 - c.OwnerIndex, Opp(c).Leader) < 6000) return;
        string key = $"OP17-034-act:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key)) return;
        ActivateRestDon(Me(c), 1);
        if (LeaderHas(c, "红发海盗团")) AtomicOps.SetOriginalPowerUntilOppEnd(Me(c).Leader, 6000, c.OwnerIndex);
        Me(c).TurnOnceUsed.Add(key);
    }

    private static async Task C036(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.EventCounter)
        {
            var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择1张“杰克斯”，本次战斗力量+4000",
                OwnLeaderAndCharacters(c).Where(x => x.MatchesName("杰克斯")), 0, 1);
            if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 4000);
            return;
        }
        if (c.Trigger != EffectTrigger.EventMain || Me(c).ActiveDonCount < 6) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将我方6张活跃咚!!转为休息状态，结算事件主要效果？")) return;
        if (!RestActiveDon(c, 6)) return;
        foreach (var card in await ChooseOppChars(c, x => !x.IsTapped, 1, "选择对方1张角色转为休息状态")) AtomicOps.RestCard(card);
        await KOByEffect(c, await ChooseOppChars(c, x => x.IsTapped && c.State.CurrentCostOf(x) <= 6, 2,
            "选择最多2张休息状态且费用≤6的角色KO"));
    }

    private static async Task C037(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.EventMain)
        {
            await SearchTop(c, 5, x => x.Info.HasKeywordContaining("红发海盗团"), "公开最多1张《红发海盗团》卡牌加入手牌");
            return;
        }
        if (c.Trigger != EffectTrigger.EventCounter || AtomicOps.RestableCount(Me(c)) < 1) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将我方1张卡牌转为休息状态，使我方1张领袖或角色力量+3000？")) return;
        if (!await AtomicOps.PromptRestOwnCards(c, 1, "选择我方1张卡牌转为休息状态", optional: true)) return;
        var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择本次战斗力量+3000的卡牌", OwnLeaderAndCharacters(c), 0, 1);
        if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 3000);
    }

    private static async Task C038(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.EventMain)
        {
            if (AtomicOps.RestableCount(Me(c)) < 4
                || !await c.Prompts.ConfirmOptional(c.OwnerIndex, "将我方4张卡牌转为休息状态，使对方1张角色转为休息状态？")
                || !await AtomicOps.PromptRestOwnCards(c, 4, "选择我方4张卡牌转为休息状态", optional: true)) return;
            foreach (var card in await ChooseOppChars(c, x => !x.IsTapped, 1, "选择对方1张角色转为休息状态")) AtomicOps.RestCard(card);
            return;
        }
        if (c.Trigger != EffectTrigger.EventCounter || Me(c).Hand.Count == 0
            || !await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张手牌，使我方1张领袖或角色力量+3000？")
            || !await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择本次战斗力量+3000的卡牌", OwnLeaderAndCharacters(c), 0, 1);
        if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 3000);
    }

    private static async Task C039(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnAttackDeclare || Me(c).Hand.Count == 0) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张手牌，公开卡组顶1张卡牌？")
            || !await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        var top = Me(c).Deck.FirstOrDefault();
        if (top is null) return;
        c.Engine?.BroadcastReveal(c.OwnerIndex, new[] { top.Info.Number });
        if (top.Info.HasKeywordContaining("洛克斯海盗团")) AtomicOps.Draw(c.State, c.OwnerIndex, 2);
    }

    private static async Task C040(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField)
        {
            AtomicOps.Draw(c.State, c.OwnerIndex, 1);
            return;
        }
        if (c.Trigger != EffectTrigger.OnLeaderBattle || c.State.CurrentBattle is not { } b) return;
        bool ownLeaderInBattle = b.AttackerCardId == Me(c).Leader.Id
            || (b.TargetIsLeader && b.DefenderPlayerIndex == c.OwnerIndex);
        if (!ownLeaderInBattle) return;
        string key = $"OP17-040-leader-battle:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key) || Me(c).Hand.Count == 0) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张手牌，使我方领袖本次战斗力量+3000？")
            || !await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        AtomicOps.AddPowerThisBattle(Me(c).Leader, 3000);
        Me(c).TurnOnceUsed.Add(key);
    }

    private static async Task C041(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField || Me(c).Hand.Count == 0) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张手牌，将对方全部原本费用为1的角色放回卡组底？")
            || !await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        var targets = Opp(c).Characters.Where(x => x.Info.Cost == 1).ToList();
        if (targets.Count == 0) return;
        var ordered = await Pick(c, 1 - c.OwnerIndex, "OrderOwnCharacters", "选择放回卡组底的顺序（先选者先放入）",
            targets, targets.Count, targets.Count);
        if (ordered.Count != targets.Count) ordered = targets;
        await AtomicOps.ProcessEffectLeavesAsync(c.State, 1 - c.OwnerIndex, ordered, c.Prompts,
            "deck-bottom", AtomicOps.ReturnFieldToDeckBottom);
    }

    private static async Task C042(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        var revealable = Me(c).Hand.Count(x => x.Info.HasKeyword("洛克斯海盗团"));
        if (revealable < 3 || !await c.Prompts.ConfirmOptional(c.OwnerIndex,
            "公开手牌中3张《洛克斯海盗团》卡牌，使对方1张角色本回合力量-3000？")) return;
        if (!await DiscardOwnFiltered(c, x => x.Info.HasKeyword("洛克斯海盗团"), 3,
            "公开手牌中3张《洛克斯海盗团》卡牌", revealOnly: true)) return;
        var pick = await ChooseOppChars(c, _ => true, 1, "选择对方1张角色，本回合力量-3000");
        if (pick.Count > 0) AtomicOps.AddPowerThisTurn(pick[0], -3000);
    }

    private static async Task C043(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField)
        {
            AtomicOps.SetOriginalPowerUntilOppEnd(Me(c).Leader, 6000, c.OwnerIndex);
            return;
        }
        if (c.Trigger is not (EffectTrigger.PreKO or EffectTrigger.OnAllyWillLeaveField) || Me(c).Hand.Count < 2) return;
        Guid victimId = c.Source.Id;
        if (c.Trigger == EffectTrigger.OnAllyWillLeaveField)
        {
            if (!c.Vars.TryGetValue("victimId", out var raw) || raw is not string id || !Guid.TryParse(id, out victimId)
                || victimId != c.Source.Id) return;
        }
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃2张手牌，使此角色不离场？")
            || !await DiscardOwn(c, 2, "选择丢弃2张手牌")) return;
        if (c.Trigger == EffectTrigger.PreKO) c.State.MarkPreventKO(victimId);
        else c.State.MarkPreventLeave(victimId);
    }

    private static async Task C044(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.ActivatedMain
            || c.Source.IsTapped
            || c.Source.HasRestriction(RestrictionKind.CannotBeRested)
            || c.State.HasContinuousRestriction(c.Source, RestrictionKind.CannotBeRested)) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将约翰船长转为休息状态，抽1张并丢弃1张手牌？")) return;
        AtomicOps.RestCard(c.Source);
        AtomicOps.Draw(c.State, c.OwnerIndex, 1);
        if (Me(c).Hand.Count > 0) await DiscardOwn(c, 1, "选择丢弃1张手牌");
    }

    private static async Task C045(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField)
        {
            AtomicOps.Draw(c.State, c.OwnerIndex, 1);
            return;
        }
        if (c.Trigger != EffectTrigger.OnAllyWillLeaveField || Me(c).Hand.Count < 2
            || !c.Vars.TryGetValue("victimId", out var raw) || raw is not string id || !Guid.TryParse(id, out var victimId)) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃2张手牌，使我方角色不离场？")
            || !await DiscardOwn(c, 2, "选择丢弃2张手牌")) return;
        c.State.MarkPreventEffectLeaveBatch(c.OwnerIndex, victimId, _ => true);
    }

    private static async Task C046(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        var all = Me(c).Characters.Concat(Opp(c).Characters)
            .Where(x => c.State.CurrentCostOf(x) <= 5).ToList();
        var pick = await Pick(c, c.OwnerIndex, "AnyCharacter", "选择最多1张费用≤5的角色放回持有者卡组底", all, 0, 1);
        if (pick.Count == 0) return;
        int owner = Me(c).Characters.Contains(pick[0]) ? c.OwnerIndex : 1 - c.OwnerIndex;
        if (!await AtomicOps.TryEffectLeaveGuard(c.State, owner, pick[0], c.Prompts, "deck-bottom"))
            AtomicOps.ReturnFieldToDeckBottom(c.State, owner, pick[0]);
    }

    private static async Task C047(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnMyTurnEnd || Me(c).Hand.Count > 2 || Opp(c).Hand.Count == 0) return;
        var pick = await Pick(c, 1 - c.OwnerIndex, "OwnHandToDeckBottom",
            "选择1张手牌放回卡组最下方", Opp(c).Hand, 1, 1);
        var card = pick.Count == 1 ? pick[0] : Opp(c).Hand[0];
        AtomicOps.ReturnHandToDeckBottom(Opp(c), card);
    }

    private static async Task C048(EffectContext c)
    {
        if (c.Trigger is not (EffectTrigger.OnAttackDeclare or EffectTrigger.OnOppAttackDeclare)) return;
        string key = $"OP17-048-battle:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key)) return;
        var costs = Me(c).Hand.Where(x => x.Info.HasKeywordContaining("洛克斯海盗团")).ToList();
        if (costs.Count == 0 || !await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张《洛克斯海盗团》卡牌，使对方1张角色力量-3000？")
            || !await DiscardOwnFiltered(c, x => x.Info.HasKeywordContaining("洛克斯海盗团"), 1, "选择丢弃1张特征中包含《洛克斯海盗团》的卡牌")) return;
        var pick = await ChooseOppChars(c, _ => true, 1, "选择对方1张角色，本回合力量-3000");
        if (pick.Count > 0) AtomicOps.AddPowerThisTurn(pick[0], -3000);
        Me(c).TurnOnceUsed.Add(key);
    }

    private static async Task C049(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField)
        {
            int choice = await c.Prompts.ChooseOption(1 - c.OwnerIndex, "选择夏洛特·玲玲的登场时效果",
                new[] { "效果控制者抽取2张卡牌", "你丢弃2张手牌" });
            if (choice == 0) AtomicOps.Draw(c.State, c.OwnerIndex, 2);
            else await DiscardOpponentChosen(c, 2);
            return;
        }
        if (c.Trigger != EffectTrigger.OnOppAttackDeclare) return;
        string key = $"OP17-049-opp-attack:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key) || Me(c).Hand.Count == 0) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张手牌，使我方1张领袖或角色本次战斗力量+1000？")
            || !await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择本次战斗力量+1000的卡牌", OwnLeaderAndCharacters(c), 0, 1);
        if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 1000);
        Me(c).TurnOnceUsed.Add(key);
    }

    private static async Task C050(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        var top = Me(c).Deck.Take(2).ToList();
        if (top.Count == 0) return;
        var order = await Pick(c, c.OwnerIndex, "OrderDeckTop", "选择卡牌排列顺序（先选者在上/先放入）", top, top.Count, top.Count);
        if (order.Count != top.Count) order = top;
        int where = await c.Prompts.ChooseOption(c.OwnerIndex, "将这组卡牌放置到哪里？", new[] { "卡组最上方", "卡组最下方" });
        foreach (var card in top) Me(c).Deck.Remove(card);
        if (where == 0) Me(c).Deck.InsertRange(0, order);
        else Me(c).Deck.AddRange(order);
        AtomicOps.Draw(c.State, c.OwnerIndex, 1);
    }

    private static async Task C052(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        var pick = await Pick(c, c.OwnerIndex, "OwnTrash", "选择废弃区中1张费用0的蓝色事件卡牌加入手牌",
            Me(c).Trash.Where(x => x.Info.Kind == CardKind.Event && x.Info.Cost == 0 && x.Info.ColorList.Contains("蓝")), 0, 1);
        if (pick.Count > 0) AtomicOps.TrashToHand(Me(c), pick[0]);
    }

    private static async Task C053(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnKO)
        {
            var opp = Opp(c);
            int n = Math.Min(2, opp.Hand.Count);
            var order = await Pick(c, 1 - c.OwnerIndex, "OwnHandToDeckBottom", $"选择{n}张手牌按顺序放回卡组底", opp.Hand, n, n);
            if (order.Count != n) order = opp.Hand.Take(n).ToList();
            foreach (var card in order) AtomicOps.ReturnHandToDeckBottom(opp, card);
            return;
        }
        if (c.Trigger != EffectTrigger.ActivatedMain) return;
        string key = $"OP17-053-act:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key) || Me(c).Hand.Count == 0) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张手牌，使巴贝尔本回合力量+3000？")
            || !await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        AtomicOps.AddPowerThisTurn(c.Source, 3000);
        Me(c).TurnOnceUsed.Add(key);
    }

    private static async Task C054(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField)
        {
            var pick = await ChooseOppChars(c, x => x.Info.Cost <= 6, 1, "选择原本费用≤6的角色，使其无法攻击");
            if (pick.Count > 0) AtomicOps.AddRestriction(pick[0], RestrictionKind.CannotAttack,
                KeywordDuration.UntilNextOpponentEndPhase, c.OwnerIndex);
            return;
        }
        if (c.Trigger != EffectTrigger.ActivatedMain || c.Source.IsTapped || Me(c).ActiveDonCount < 3) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将3张咚!!和此角色转为休息状态，使对方1张角色无法攻击？")) return;
        RestActiveDon(c, 3);
        AtomicOps.RestCard(c.Source);
        var target = await ChooseOppChars(c, _ => true, 1, "选择无法攻击的角色");
        if (target.Count > 0) AtomicOps.AddRestriction(target[0], RestrictionKind.CannotAttack,
            KeywordDuration.UntilNextOpponentEndPhase, c.OwnerIndex);
    }

    private static async Task C055(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.EventCounter)
        {
            var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择《洛克斯海盗团》领袖或角色，本次战斗力量+2000",
                OwnLeaderAndCharacters(c).Where(x => x.Info.HasKeyword("洛克斯海盗团")), 0, 1);
            if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 2000);
            return;
        }
        if (c.Trigger != EffectTrigger.EventMain || Me(c).ActiveDonCount < 1
            || !await c.Prompts.ConfirmOptional(c.OwnerIndex, "将1张咚!!转为休息状态，使洛克斯·D·吉贝克获得【不可阻挡】？")) return;
        RestActiveDon(c, 1);
        var target = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择1张洛克斯·D·吉贝克获得【不可阻挡】",
            OwnLeaderAndCharacters(c).Where(x => x.MatchesName("洛克斯·D·吉贝克")), 0, 1);
        if (target.Count > 0) AtomicOps.GiveKeyword(target[0], "不可阻挡", KeywordDuration.ThisTurn, c.OwnerIndex);
    }

    private static async Task C056(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.EventCounter)
        {
            var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择《洛克斯海盗团》领袖或角色，本次战斗力量+2000",
                OwnLeaderAndCharacters(c).Where(x => x.Info.HasKeyword("洛克斯海盗团")), 0, 1);
            if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 2000);
            return;
        }
        if (c.Trigger != EffectTrigger.EventMain || Me(c).ActiveDonCount < 5
            || !await c.Prompts.ConfirmOptional(c.OwnerIndex, "将5张咚!!转为休息状态，使对方费用≤6角色回手？")) return;
        RestActiveDon(c, 5);
        var target = await ChooseOppChars(c, x => c.State.CurrentCostOf(x) <= 6, 1, "选择费用≤6的角色放回手牌");
        if (target.Count > 0 && !await AtomicOps.TryEffectLeaveGuard(c.State, 1 - c.OwnerIndex, target[0], c.Prompts, "hand"))
            AtomicOps.BounceToHand(c.State, 1 - c.OwnerIndex, target[0]);
    }

    private static async Task C057(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnOppAttackDeclare || c.Source.IsTapped || Me(c).Hand.Count == 0) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将哈奇诺斯转为休息并丢弃1张手牌，使我方卡牌力量+1000？")
            || !await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        AtomicOps.RestCard(c.Source);
        var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择《洛克斯海盗团》领袖或角色，本次战斗力量+1000",
            OwnLeaderAndCharacters(c).Where(x => x.Info.HasKeyword("洛克斯海盗团")), 0, 1);
        if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 1000);
    }

    private static async Task C058(EffectContext c)
    {
        if (c.Trigger is not (EffectTrigger.OnAttackDeclare or EffectTrigger.OnOppAttackDeclare)) return;
        string key = $"OP17-058-battle:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key) || Me(c).TotalDonInCostArea < 1) return;
        if (!await AtomicOps.PromptReturnDonToDeck(c, 1)) return;
        var pick = await ChooseOppChars(c, _ => true, 1, "选择对方1张角色，本回合力量-2000");
        if (pick.Count > 0) AtomicOps.AddPowerThisTurn(pick[0], -2000);
        Me(c).TurnOnceUsed.Add(key);
    }

    private static async Task C059(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField || !await AtomicOps.PromptReturnDonToDeck(c, 1)) return;
        AtomicOps.Draw(c.State, c.OwnerIndex, 1);
        await KOByEffect(c, await ChooseOppChars(c, x => c.State.CurrentCostOf(x) <= 2, 2, "选择最多2张费用≤2的角色KO"));
    }

    private static async Task C060(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField || !LeaderHas(c, "百兽海盗团")) return;
        AtomicOps.RefreshDonFromDeck(Me(c), 1, DonState.Active);
        var pick = await ChooseOppChars(c, x => c.State.CurrentPowerOf(1 - c.OwnerIndex, x) <= 3000, 1, "选择力量≤3000的角色KO");
        await KOByEffect(c, pick);
    }

    private static async Task C061(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField)
        {
            if (!await AtomicOps.PromptReturnDonToDeck(c, 1)) return;
            if (LeaderHas(c, "百兽海盗团")) AtomicOps.AddLifeFromDeckTop(Me(c), 1);
            return;
        }
        if (c.Trigger != EffectTrigger.ActivatedMain || !Me(c).Characters.Contains(c.Source)) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将大看板放置废弃区，从手牌登场1张“烬”“昆因”或“杰克”？")) return;
        TrashSelfAsCost(c);
        await PlayOneFromHand(c, x => x.MatchesName("烬") || x.MatchesName("昆因") || x.MatchesName("杰克"),
            "将手牌中最多1张“烬”“昆因”或“杰克”登场");
    }

    private static async Task C062(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnDonReturnedToDeck || c.State.CurrentTurnPlayer != c.OwnerIndex) return;
        if (c.Vars.TryGetValue("owner", out var raw) && raw is int owner && owner != c.OwnerIndex) return;
        string key = $"OP17-062-don:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key)) return;
        AtomicOps.RefreshDonFromDeck(Me(c), 1, DonState.Active);
        ActivateRestDon(Me(c), 1);
        Me(c).TurnOnceUsed.Add(key);
        await Task.CompletedTask;
    }

    private static async Task C063(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.ActivatedMain) return;
        string key = $"OP17-063-act:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key) || !await AtomicOps.PromptReturnDonToDeck(c, 1)) return;
        var pick = await ChooseOppChars(c, x => c.State.CurrentCostOf(x) <= 6, 1, "选择费用≤6的角色，使其效果无效并KO");
        if (pick.Count > 0)
        {
            AtomicOps.NullifyEffects(pick[0], KeywordDuration.ThisTurn);
            await KOByEffect(c, pick);
        }
        Me(c).TurnOnceUsed.Add(key);
    }

    private static async Task C064(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnOppAttackDeclare) return;
        string key = $"OP17-064-opp-attack:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key) || Me(c).Hand.Count == 0) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张手牌，使我方1张领袖或角色本次战斗力量+2000？")
            || !await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择本次战斗力量+2000的卡牌", OwnLeaderAndCharacters(c), 0, 1);
        if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 2000);
        Me(c).TurnOnceUsed.Add(key);
    }

    private static async Task C065(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField || !await AtomicOps.PromptReturnDonToDeck(c, 1)) return;
        AtomicOps.Draw(c.State, c.OwnerIndex, 1);
        foreach (var target in await ChooseOppChars(c, x => c.State.CurrentCostOf(x) <= 5, 2, "选择最多2张费用≤5的角色，使其无法攻击"))
            AtomicOps.AddRestriction(target, RestrictionKind.CannotAttack, KeywordDuration.UntilNextOpponentEndPhase, c.OwnerIndex);
    }

    private static async Task C066(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField || !await AtomicOps.PromptReturnDonToDeck(c, 1)) return;
        if (!Me(c).Characters.Any(x => c.State.CurrentCostOf(x) >= 10)) return;
        AtomicOps.Draw(c.State, c.OwnerIndex, 2);
        if (Me(c).Hand.Count > 0) await DiscardOwn(c, 1, "选择丢弃1张手牌");
    }

    private static async Task C067(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField
            || !Me(c).Characters.Any(x => c.State.CurrentCostOf(x) >= 10)
            || !await AtomicOps.PromptReturnDonToDeck(c, 1)) return;
        var target = await ChooseOppChars(c, x => !x.IsTapped, 1, "选择对方最多1张角色转为休息状态");
        if (target.Count > 0) AtomicOps.RestCard(target[0]);
    }

    private static async Task C068(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnAttackDeclare || !LeaderHas(c, "百兽海盗团") || Me(c).Hand.Count < 2) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃2张手牌，从咚!!卡组追加最多2张休息咚？")
            || !await DiscardOwn(c, 2, "选择丢弃2张手牌")) return;
        AtomicOps.RefreshDonFromDeck(Me(c), 2, DonState.Rest);
    }

    private static async Task C069(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField || !await AtomicOps.PromptReturnDonToDeck(c, 1) || !LeaderHas(c, "百兽海盗团")) return;
        var pick = await ChooseOppChars(c, _ => true, 1, "选择对方1张角色，本回合力量-2000");
        if (pick.Count > 0) AtomicOps.AddPowerThisTurn(pick[0], -2000);
    }

    private static async Task C073(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField || !LeaderHas(c, "百兽海盗团") || Me(c).Hand.Count == 0) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张手牌，从咚!!卡组追加1张活跃咚？")
            || !await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        AtomicOps.RefreshDonFromDeck(Me(c), 1, DonState.Active);
    }

    private static async Task C071(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnLifeRevealTrigger) { await PlaySelfFromTrash(c); return; }
        if (c.Trigger != EffectTrigger.OnEnterField || !await AtomicOps.PromptReturnDonToDeck(c, 1)) return;
        await KOByEffect(c, await ChooseOppChars(c, x => c.State.CurrentCostOf(x) <= 2, 2, "选择最多2张费用≤2的角色KO"));
    }

    private static async Task C072(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnOppAttackDeclare) return;
        string key = $"OP17-072-opp-attack:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key) || Me(c).Hand.Count == 0) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张手牌，使我方1张领袖或角色本次战斗力量+1000？")
            || !await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择本次战斗力量+1000的卡牌", OwnLeaderAndCharacters(c), 0, 1);
        if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 1000);
        Me(c).TurnOnceUsed.Add(key);
    }

    private static async Task C075(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField || !await AtomicOps.PromptReturnDonToDeck(c, 2)) return;
        if (c.Engine is not null) AtomicOps.OpponentDiscardRandom(c.Engine, 1 - c.OwnerIndex, 1);
        else if (Opp(c).Hand.Count > 0) AtomicOps.DiscardHand(Opp(c), Opp(c).Hand[0]);
    }

    private static void C074(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField) AtomicOps.RefreshDonFromDeck(Me(c), 1, DonState.Rest);
    }

    private static async Task C076(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            if (await AtomicOps.PromptReturnDonToDeck(c, 1)) AtomicOps.Draw(c.State, c.OwnerIndex, 2);
            return;
        }
        if (c.Trigger != EffectTrigger.EventCounter || Me(c).Hand.Count == 0
            || !await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张手牌，使我方1张领袖或角色本次战斗力量+3000？")
            || !await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择本次战斗力量+3000的卡牌", OwnLeaderAndCharacters(c), 0, 1);
        if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 3000);
    }

    private static async Task C077(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.EventCounter)
        {
            if (!await AtomicOps.PromptReturnDonToDeck(c, 1)) return;
            AtomicOps.AddPowerThisBattle(Me(c).Leader, 4000);
            return;
        }
        if (c.Trigger != EffectTrigger.EventMain || Me(c).ActiveDonCount < 3 || Me(c).Hand.Count < 2) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex,
                "将3张咚!!转为休息状态并丢弃2张手牌，追加最多3张休息咚!!？")) return;
        if (!await DiscardOwn(c, 2, "选择丢弃2张手牌")) return;
        RestActiveDon(c, 3);
        if (LeaderHas(c, "百兽海盗团")) AtomicOps.RefreshDonFromDeck(Me(c), 3, DonState.Rest);
    }

    private static async Task C078(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.EventCounter)
        {
            var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择本次战斗力量+4000的卡牌", OwnLeaderAndCharacters(c), 0, 1);
            if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 4000);
            return;
        }
        if (c.Trigger != EffectTrigger.EventMain || Me(c).ActiveDonCount < 2 || Me(c).Hand.Count < 2 || !LeaderHas(c, "百兽海盗团")) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将2张咚!!转为休息并丢弃2张手牌，追加3张休息咚？")) return;
        RestActiveDon(c, 2);
        if (!await DiscardOwn(c, 2, "选择丢弃2张手牌")) return;
        AtomicOps.RefreshDonFromDeck(Me(c), 3, DonState.Rest);
    }

    private static void C079(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnGameStart) return;
        int owner = c.OwnerIndex;
        var id = c.Source.Id;
        RegisterContinuous(c, new ContinuousEffect
        {
            SourceCardId = id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "阻挡者",
            Predicate = (s, side, card) => side == owner
                && s.Players[owner].Characters.Contains(card)
                && s.CurrentCostOf(owner, card) >= 12,
        });
    }

    private static async Task C080(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        RegisterContinuous(c, SelfPower(c, 3000, s => s.Players.SelectMany(p => p.Characters).Any(x => s.CurrentCostOf(x) >= 12)));
        await SearchTop(c, 3, x => x.Info.HasKeyword("埃鲁巴夫"), "公开最多1张《埃鲁巴夫》卡牌加入手牌", trashRemainder: true);
    }

    private static async Task C081(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        int owner = c.OwnerIndex;
        RegisterContinuous(c, SelfCost(c, 12, s => s.Players[owner].Leader.Info.HasKeyword("埃鲁巴夫")));
        var hand = Me(c).Hand;
        if (hand.Count == 0 || !await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张手牌，将废弃区中1张费用≤8的其他角色加入手牌？")
            || !await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        var pick = await Pick(c, c.OwnerIndex, "OwnTrashCharacter", "选择费用≤8且卡名不是葛尔兹的角色加入手牌",
            Me(c).Trash.Where(x => x.Info.Kind == CardKind.Character && x.Info.Cost <= 8 && !x.MatchesName("葛尔兹")), 0, 1);
        if (pick.Count > 0) AtomicOps.TrashToHand(Me(c), pick[0]);
    }

    private static async Task C082(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        RegisterContinuous(c, SelfPower(c, 3000, s => s.Players.SelectMany(p => p.Characters).Any(x => s.CurrentCostOf(x) >= 12)));
        AtomicOps.Draw(c.State, c.OwnerIndex, 2);
        if (Me(c).Hand.Count > 0) await DiscardOwn(c, Math.Min(2, Me(c).Hand.Count), "选择丢弃2张手牌");
    }

    private static void C083(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        var id = c.Source.Id;
        int owner = c.OwnerIndex;
        Func<GameState, bool> condition = s => s.Players.SelectMany(p => p.Characters).Any(x => s.CurrentCostOf(x) >= 12);
        RegisterContinuous(c,
            SelfPower(c, 3000, condition),
            new ContinuousEffect
            {
                SourceCardId = id.ToString(),
                Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
                GrantKeyword = "阻挡者",
                Predicate = (s, side, card) => side == owner && card.Id == id && condition(s),
            });
    }

    private static async Task C084(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField || !AnyCostAtLeast(c, 12)) return;
        var pick = await ChooseOwnChars(c, _ => true, 1, "选择我方1张角色，本回合获得【不可阻挡】");
        if (pick.Count > 0) AtomicOps.GiveKeyword(pick[0], "不可阻挡", KeywordDuration.ThisTurn, c.OwnerIndex);
    }

    private static async Task C085(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        RegisterContinuous(c, SelfCost(c, 12, _ => true));
        if (!LeaderHas(c, "埃鲁巴夫")) return;
        var played = await PlayOneFromHandOrTrash(c, x => x.Info.Cost <= 5 && x.MatchesName("布洛基"),
            "从手牌或废弃区将最多1张费用≤5的“布洛基”登场");
        if (played is not null) c.State.NoPlayCharacterThisTurn.Add(c.OwnerIndex);
    }

    private static async Task C086(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        if (!await DiscardOwnFiltered(c, x => x.Info.HasKeyword("埃鲁巴夫"), 1, "选择丢弃1张《埃鲁巴夫》卡牌")) return;
        AtomicOps.Draw(c.State, c.OwnerIndex, 2);
    }

    private static async Task C087(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        RegisterContinuous(c, SelfPower(c, 3000, s => s.Players.SelectMany(p => p.Characters).Any(x => s.CurrentCostOf(x) >= 12)));
        if (!AnyCostAtLeast(c, 12)) return;
        var pick = await ChooseOppChars(c, _ => true, 1, "选择对方1张角色，本回合力量-3000");
        if (pick.Count > 0) AtomicOps.AddPowerThisTurn(pick[0], -3000);
    }

    private static async Task C089(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        RegisterContinuous(c, SelfCost(c, 12, _ => true));
        await SearchTop(c, 3, x => x.Info.HasKeyword("埃鲁巴夫"), "公开最多1张《埃鲁巴夫》卡牌加入手牌", trashRemainder: true);
    }

    private static async Task C090(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        RegisterContinuous(c, SelfPower(c, 3000, s => s.Players.SelectMany(p => p.Characters).Any(x => s.CurrentCostOf(x) >= 12)));
        if (AnyCostAtLeast(c, 12))
            await KOByEffect(c, await ChooseOppChars(c, x => c.State.CurrentCostOf(x) <= 2, 1, "选择费用≤2的角色KO"));
    }

    private static async Task C091(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        RegisterContinuous(c, SelfPower(c, 3000, s => s.Players.SelectMany(p => p.Characters).Any(x => s.CurrentCostOf(x) >= 12)));
        if (!AnyCostAtLeast(c, 12)) return;
        await AtomicOps.OpponentDiscardChosen(c.State, c.Prompts, 1 - c.OwnerIndex, 1);
    }

    private static async Task C092(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        RegisterContinuous(c, SelfCost(c, 12, _ => true));
        if (!LeaderHas(c, "埃鲁巴夫")) return;
        var played = await PlayOneFromHandOrTrash(c, x => x.Info.Cost <= 5 && x.MatchesName("东利"),
            "从手牌或废弃区将最多1张费用≤5的“东利”登场");
        if (played is not null) c.State.NoPlayCharacterThisTurn.Add(c.OwnerIndex);
    }

    private static async Task C093(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        var id = c.Source.Id;
        int owner = c.OwnerIndex;
        RegisterContinuous(c, new ContinuousEffect
        {
            SourceCardId = id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            GrantKeyword = "速攻",
            Predicate = (s, side, card) => side == owner && card.Id == id
                && s.Players.SelectMany(p => p.Characters).Any(x => s.CurrentCostOf(x) >= 12),
        });
        AtomicOps.Draw(c.State, c.OwnerIndex, 1);
        await PlayOneFromTrash(c, x => x.Info.Cost <= 2, "将废弃区中最多1张费用≤2的角色登场");
    }

    private static void C094(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        int owner = c.OwnerIndex;
        RegisterContinuous(c, SelfCost(c, 12, s => s.Players[owner].Leader.Info.HasKeyword("埃鲁巴夫")));
    }

    private static async Task C095(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField)
        {
            RegisterContinuous(c, SelfPower(c, 3000, s => s.Players.SelectMany(p => p.Characters).Any(x => s.CurrentCostOf(x) >= 12)));
            return;
        }
        if (c.Trigger != EffectTrigger.OnAllyWillLeaveField || Me(c).Trash.Count < 3
            || !c.Vars.TryGetValue("victimId", out var raw) || raw is not string id || !Guid.TryParse(id, out var victimId)) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将废弃区3张卡牌放回卡组底，使角色不离场？")) return;
        var order = await Pick(c, c.OwnerIndex, "OwnTrashToDeckBottom", "选择3张废弃区卡牌及放回顺序",
            Me(c).Trash, 3, 3);
        if (order.Count < 3) return;
        foreach (var card in order) AtomicOps.ReturnTrashToDeckBottom(Me(c), card);
        c.State.MarkPreventEffectLeaveBatch(c.OwnerIndex, victimId, _ => true);
    }

    private static async Task C096(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            var pick = await Pick(c, c.OwnerIndex, "OwnTrashCard", "将废弃区中最多1张《埃鲁巴夫》卡牌加入手牌",
                Me(c).Trash.Where(x => x.Info.HasKeyword("埃鲁巴夫")), 0, 1);
            if (pick.Count > 0)
            {
                Me(c).Trash.Remove(pick[0]);
                Me(c).Hand.Add(pick[0]);
            }
            return;
        }
        if (c.Trigger != EffectTrigger.EventCounter || !AnyCostAtLeast(c, 12)) return;
        var target = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择本次战斗力量+4000的卡牌",
            OwnLeaderAndCharacters(c), 0, 1);
        if (target.Count > 0) AtomicOps.AddPowerThisBattle(target[0], 4000);
    }

    private static async Task C097(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.EventCounter)
        {
            AtomicOps.AddPowerThisBattle(Me(c).Leader, 3000);
            return;
        }
        if (c.Trigger != EffectTrigger.EventMain) return;
        foreach (var character in Opp(c).Characters)
            AtomicOps.AddCostModifier(character, -1, KeywordDuration.ThisTurn);
        await Task.CompletedTask;
    }

    private static async Task C098(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.EventCounter)
        {
            AtomicOps.AddPowerThisBattle(Me(c).Leader, 3000);
            return;
        }
        if (c.Trigger != EffectTrigger.EventMain || Me(c).ActiveDonCount < 6 || !AnyCostAtLeast(c, 12)) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将6张咚!!转为休息状态，KO对方最多2张费用≤6角色？")) return;
        RestActiveDon(c, 6);
        await KOByEffect(c, await ChooseOppChars(c, x => c.State.CurrentCostOf(x) <= 6, 2, "选择最多2张费用≤6的角色KO"));
    }

    private static async Task C099(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnAttackDeclare || Me(c).Hand.Count == 0) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张手牌，让对方选择效果？")
            || !await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
        var options = new List<string>
        {
            "效果控制者可以丢弃1张手牌，并将卡组顶最多1张加入生命",
            "随机丢弃你的1张手牌",
        };
        int pick = await c.Prompts.ChooseOption(1 - c.OwnerIndex, "选择夏洛特·玲玲的攻击时效果", options);
        if (pick == 0)
        {
            if (Me(c).Hand.Count == 0
                || !await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃我方1张手牌？")
                || !await DiscardOwn(c, 1, "选择丢弃1张手牌"))
                return;
            if (Me(c).Deck.Count > 0
                && await c.Prompts.ConfirmOptional(c.OwnerIndex, "将我方卡组最上方最多1张卡牌加入生命区最上方？"))
                AtomicOps.AddLifeFromDeckTop(Me(c), 1);
        }
        else
        {
            if (c.Engine is not null) AtomicOps.OpponentDiscardRandom(c.Engine, 1 - c.OwnerIndex, 1);
            else if (Opp(c).Hand.Count > 0)
            {
                int index = c.State.Rng.Next(Opp(c).Hand.Count);
                AtomicOps.DiscardHand(Opp(c), Opp(c).Hand[index]);
            }
        }
    }

    private static async Task C101(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            if (Me(c).Hand.Count == 0 || !await c.Prompts.ConfirmOptional(c.OwnerIndex, "丢弃1张手牌，KO对方费用≤5角色？")
                || !await DiscardOwn(c, 1, "选择丢弃1张手牌")) return;
            await KOByEffect(c, await ChooseOppChars(c, x => c.State.CurrentCostOf(x) <= 5, 1, "选择费用≤5的角色KO"));
            return;
        }
        if (c.Trigger != EffectTrigger.ActivatedMain || Me(c).LifeArea.Count == 0) return;
        string key = $"OP17-101-act:{c.Source.Id}";
        if (Me(c).TurnOnceUsed.Contains(key)) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将生命顶1张加入手牌，使对方1张角色力量-3000？")) return;
        var life = Me(c).LifeArea[0]; Me(c).LifeArea.RemoveAt(0); life.IsLifeFaceUp = false; Me(c).Hand.Add(life);
        var target = await ChooseOppChars(c, _ => true, 1, "选择对方1张角色，本回合力量-3000");
        if (target.Count > 0) AtomicOps.AddPowerThisTurn(target[0], -3000);
        Me(c).TurnOnceUsed.Add(key);
    }

    private static async Task C102(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnLifeRevealTrigger) { await PlaySelfFromTrash(c); return; }
        if (c.Trigger != EffectTrigger.OnKO) return;
        await PlayOneFromTrash(c, x => !x.MatchesName("夏洛特·烤箱") && x.Info.Power <= 4000,
            "将废弃区中最多1张其他力量≤4000的角色登场");
    }

    private static async Task C103(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnLifeRevealTrigger) { await PlaySelfFromTrash(c); return; }
        if (c.Trigger != EffectTrigger.OnEnterField || c.State.CurrentTurnPlayer != c.OwnerIndex || !LeaderHas(c, "大妈海盗团")) return;
        if (Me(c).Deck.Count > 0
            && await c.Prompts.ConfirmOptional(c.OwnerIndex, "将卡组最上方最多1张卡牌加入生命区最上方？"))
            AtomicOps.AddLifeFromDeckTop(Me(c), 1);
        var target = await ChooseOppChars(c, _ => true, 1, "选择对方1张角色，本回合力量-3000");
        if (target.Count > 0) AtomicOps.AddPowerThisTurn(target[0], -3000);
    }

    private static async Task C104(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnLifeRevealTrigger) { await PlaySelfFromTrash(c); return; }
        if (c.Trigger != EffectTrigger.OnEnterField || c.State.CurrentTurnPlayer != c.OwnerIndex
            || !LeaderHas(c, "大妈海盗团") || Me(c).ActiveDonCount < 2) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将2张咚!!转为休息状态，将卡组顶1张加入生命？")) return;
        RestActiveDon(c, 2);
        AtomicOps.AddLifeFromDeckTop(Me(c), 1);
    }

    private static async Task C105(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        if (!await DiscardOwnFiltered(c, x => !string.IsNullOrEmpty(x.Info.Trigger), 1, "选择丢弃1张拥有【触发】的手牌")) return;
        var target = await ChooseOppChars(c, x => !string.IsNullOrEmpty(x.Info.Trigger), 1, "选择1张拥有【触发】的角色放回手牌");
        if (target.Count > 0 && !await AtomicOps.TryEffectLeaveGuard(c.State, 1 - c.OwnerIndex, target[0], c.Prompts, "hand"))
            AtomicOps.BounceToHand(c.State, 1 - c.OwnerIndex, target[0]);
    }

    private static async Task C106(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnLifeRevealTrigger) { await PlaySelfFromTrash(c); return; }
        if (c.Trigger != EffectTrigger.OnEnterField || c.State.CurrentTurnPlayer != c.OwnerIndex || Me(c).ActiveDonCount < 2) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将2张咚!!转为休息状态，将卡组顶1张加入生命并使对方丢弃1张手牌？")) return;
        RestActiveDon(c, 2);
        AtomicOps.AddLifeFromDeckTop(Me(c), 1);
        await DiscardOpponentChosen(c, 1);
    }

    private static async Task C107(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnLifeRevealTrigger) await PlaySelfFromTrash(c);
    }

    private static async Task C108(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnLifeRevealTrigger) return;
        var target = await ChooseOppChars(c, x => c.State.CurrentCostOf(x) <= 6 && !x.IsTapped, 1, "选择费用≤6的角色转为休息状态");
        if (target.Count > 0) AtomicOps.RestCard(target[0]);
    }

    private static async Task C109(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnLifeRevealTrigger)
        {
            await SearchTop(c, 5, x => x.Info.HasKeyword("大妈海盗团"), "公开最多1张《大妈海盗团》卡牌加入手牌");
            return;
        }
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        if (!await DiscardOwnFiltered(c, x => !string.IsNullOrEmpty(x.Info.Trigger), 1, "选择丢弃1张拥有【触发】的手牌")) return;
        AtomicOps.Draw(c.State, c.OwnerIndex, 3);
    }

    private static async Task C110(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnLifeRevealTrigger) { await PlaySelfFromTrash(c); return; }
        if (c.Trigger != EffectTrigger.OnEnterField || c.State.CurrentTurnPlayer != c.OwnerIndex) return;
        await PlayOneFromHand(c, x => x.Info.Cost <= 6 && x.Info.HasKeyword("大妈海盗团"),
            "将手牌中最多1张费用≤6的《大妈海盗团》角色登场");
        AtomicOps.GiveKeyword(c.Source, "速攻", KeywordDuration.ThisTurn, c.OwnerIndex);
    }

    private static async Task C111(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnLifeRevealTrigger) { await PlaySelfFromTrash(c); return; }
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex,
            "是否公开手牌中2张拥有【触发】的卡牌，并KO对方最多2张费用≤1的角色？")) return;
        if (!await DiscardOwnFiltered(c, x => !string.IsNullOrEmpty(x.Info.Trigger), 2,
            "公开手牌中2张拥有【触发】的卡牌", revealOnly: true)) return;
        await KOByEffect(c, await ChooseOppChars(c, x => c.State.CurrentCostOf(x) <= 1, 2, "选择最多2张费用≤1的角色KO"));
    }

    private static async Task C112(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        int owner = c.OwnerIndex;
        var id = c.Source.Id;
        var source = c.Source;
        RegisterContinuous(c, new ContinuousEffect
        {
            SourceCardId = id.ToString(),
            Scope = new ContinuousScope { Side = 0, IncludeLeader = false, IncludeCharacters = true },
            OriginalPowerOverride = 8000,
            Predicate = (s, side, card) => !source.IsEffectsNullified && !s.IsContinuouslyNullified(source)
                && side == owner && s.CurrentTurnPlayer == owner
                && s.Players[owner].Characters.Contains(card)
                && card.Info.Power == 4000 && !string.IsNullOrEmpty(card.Info.Trigger),
        });
        AtomicOps.Draw(c.State, c.OwnerIndex, 1);
        var options = new List<string>();
        if (Me(c).Deck.Count > 0) options.Add("将我方卡组顶1张加入生命顶");
        if (Opp(c).LifeArea.Count > 0) options.Add("将对方生命顶1张加入其手牌");
        if (options.Count == 0) return;
        int choice = options.Count == 1 ? 0 : await c.Prompts.ChooseOption(c.OwnerIndex, "选择夏洛特·玲玲的登场效果", options);
        if (options[choice].StartsWith("将我方")) AtomicOps.AddLifeFromDeckTop(Me(c), 1);
        else
        {
            var life = Opp(c).LifeArea[0]; Opp(c).LifeArea.RemoveAt(0); life.IsLifeFaceUp = false; Opp(c).Hand.Add(life);
        }
    }

    private static async Task C113(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnEnterField)
            await SearchTop(c, 3, x => x.Info.HasKeyword("大妈海盗团"), "公开最多1张《大妈海盗团》卡牌加入手牌",
                reorderRemainder: true);
    }

    private static async Task C114(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.OnLifeRevealTrigger) { await PlaySelfFromTrash(c); return; }
        if (c.Trigger != EffectTrigger.OnEnterField
            || c.State.CurrentTurnPlayer != c.OwnerIndex
            || Me(c).ActiveDonCount < 2) return;
        if (!await c.Prompts.ConfirmOptional(c.OwnerIndex,
                "将2张咚!!转为休息状态，发动抽牌、追加生命并降低对方角色力量的效果？")) return;
        RestActiveDon(c, 2);
        AtomicOps.Draw(c.State, c.OwnerIndex, 1);
        if (Me(c).Deck.Count > 0
            && await c.Prompts.ConfirmOptional(c.OwnerIndex, "将卡组最上方最多1张卡牌加入生命区最上方？"))
            AtomicOps.AddLifeFromDeckTop(Me(c), 1);
        foreach (var target in await ChooseOppChars(c, _ => true, 2, "选择最多2张角色，本回合力量-3000"))
            AtomicOps.AddPowerThisTurn(target, -3000);
    }

    private static async Task C115(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.EventMain)
        {
            if (Me(c).Leader.MatchesName("夏洛特·玲玲")) AtomicOps.GiveKeyword(Me(c).Leader, "不可阻挡", KeywordDuration.ThisTurn, c.OwnerIndex);
            return;
        }
        if (c.Trigger != EffectTrigger.EventCounter) return;
        var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择1张夏洛特·玲玲，本次战斗力量+4000",
            OwnLeaderAndCharacters(c).Where(x => x.MatchesName("夏洛特·玲玲")), 0, 1);
        if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 4000);
    }

    private static async Task C116(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.EventMain)
        {
            var stage = Opp(c).StageCard;
            if (Me(c).ActiveDonCount < 2 || stage is null) return;
            if (!await c.Prompts.ConfirmOptional(c.OwnerIndex, "将2张咚!!转为休息状态，KO对方舞台？")) return;
            RestActiveDon(c, 2);
            AtomicOps.KO(c.State, 1 - c.OwnerIndex, stage);
            return;
        }
        if (c.Trigger != EffectTrigger.EventCounter || Me(c).Characters.Count(x => !string.IsNullOrEmpty(x.Info.Trigger)) < 2) return;
        var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择本次战斗力量+4000的卡牌", OwnLeaderAndCharacters(c), 0, 1);
        if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 4000);
    }

    private static async Task C117(EffectContext c)
    {
        if (c.Trigger == EffectTrigger.EventCounter)
        {
            var pick = await Pick(c, c.OwnerIndex, "OwnLeaderOrCharacter", "选择1张夏洛特·玲玲，本次战斗力量+3000",
                OwnLeaderAndCharacters(c).Where(x => x.MatchesName("夏洛特·玲玲")), 0, 1);
            if (pick.Count > 0) AtomicOps.AddPowerThisBattle(pick[0], 3000);
            return;
        }
        if (c.Trigger != EffectTrigger.OnLifeRevealTrigger) return;
        bool discard = Opp(c).Hand.Count >= 3 && await c.Prompts.ConfirmOptional(1 - c.OwnerIndex, "丢弃3张手牌以避免角色被KO？");
        if (discard)
        {
            await DiscardOpponentChosen(c, 3);
            return;
        }
        await KOByEffect(c, await ChooseOppChars(c, x => c.State.CurrentCostOf(x) <= 6, 1, "选择费用≤6的角色KO"));
    }

    private static async Task C118(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        AtomicOps.Draw(c.State, c.OwnerIndex, 1);
        var selected = await ChooseByTotalCost(c,
            Me(c).Hand.Where(x => x.Info.HasKeyword("洛克斯海盗团") &&
                                  x.Info.Kind is CardKind.Character or CardKind.Stage),
            9, 2, "选择不同名称的《洛克斯海盗团》角色或舞台登场", distinctNames: true);
        foreach (var card in selected) await AtomicOps.PlayFromHandFree(c.State, c.OwnerIndex, card);
    }

    private static async Task C119(EffectContext c)
    {
        if (c.Trigger != EffectTrigger.OnEnterField) return;
        int owner = c.OwnerIndex;
        RegisterContinuous(c,
            SelfCost(c, 12, _ => true),
            SelfPower(c, 3000, s => s.CurrentTurnPlayer != owner));
        var selected = await ChooseByTotalCost(c, Opp(c).Characters, 4, 5, "选择费用合计≤4的角色KO");
        await KOByEffect(c, selected);
    }
}

public abstract class OP17CardEffect : IScriptedEffect
{
    protected abstract string Number { get; }
    public string CardNumber => Number;
    public bool HandlesTrigger(EffectTrigger trigger) => true;
    public Task Resolve(EffectContext ctx) => OP17Effects.Resolve(ctx);
}

public sealed class OP17_001_Effect : OP17CardEffect { protected override string Number => "OP17-001"; }
public sealed class OP17_002_Effect : OP17CardEffect { protected override string Number => "OP17-002"; }
public sealed class OP17_003_Effect : OP17CardEffect { protected override string Number => "OP17-003"; }
public sealed class OP17_004_Effect : OP17CardEffect { protected override string Number => "OP17-004"; }
public sealed class OP17_005_Effect : OP17CardEffect { protected override string Number => "OP17-005"; }
public sealed class OP17_007_Effect : OP17CardEffect { protected override string Number => "OP17-007"; }
public sealed class OP17_008_Effect : OP17CardEffect { protected override string Number => "OP17-008"; }
public sealed class OP17_009_Effect : OP17CardEffect { protected override string Number => "OP17-009"; }
public sealed class OP17_010_Effect : OP17CardEffect { protected override string Number => "OP17-010"; }
public sealed class OP17_011_Effect : OP17CardEffect { protected override string Number => "OP17-011"; }
public sealed class OP17_012_Effect : OP17CardEffect { protected override string Number => "OP17-012"; }
public sealed class OP17_013_Effect : OP17CardEffect { protected override string Number => "OP17-013"; }
public sealed class OP17_014_Effect : OP17CardEffect { protected override string Number => "OP17-014"; }
public sealed class OP17_015_Effect : OP17CardEffect { protected override string Number => "OP17-015"; }
public sealed class OP17_016_Effect : OP17CardEffect { protected override string Number => "OP17-016"; }
public sealed class OP17_017_Effect : OP17CardEffect { protected override string Number => "OP17-017"; }
public sealed class OP17_018_Effect : OP17CardEffect { protected override string Number => "OP17-018"; }
public sealed class OP17_019_Effect : OP17CardEffect { protected override string Number => "OP17-019"; }
public sealed class OP17_020_Effect : OP17CardEffect { protected override string Number => "OP17-020"; }
public sealed class OP17_021_Effect : OP17CardEffect { protected override string Number => "OP17-021"; }
public sealed class OP17_022_Effect : OP17CardEffect { protected override string Number => "OP17-022"; }
public sealed class OP17_023_Effect : OP17CardEffect { protected override string Number => "OP17-023"; }
public sealed class OP17_024_Effect : OP17CardEffect { protected override string Number => "OP17-024"; }
public sealed class OP17_025_Effect : OP17CardEffect { protected override string Number => "OP17-025"; }
public sealed class OP17_026_Effect : OP17CardEffect { protected override string Number => "OP17-026"; }
public sealed class OP17_027_Effect : OP17CardEffect { protected override string Number => "OP17-027"; }
public sealed class OP17_028_Effect : OP17CardEffect { protected override string Number => "OP17-028"; }
public sealed class OP17_029_Effect : OP17CardEffect { protected override string Number => "OP17-029"; }
public sealed class OP17_030_Effect : OP17CardEffect { protected override string Number => "OP17-030"; }
public sealed class OP17_031_Effect : OP17CardEffect { protected override string Number => "OP17-031"; }
public sealed class OP17_032_Effect : OP17CardEffect { protected override string Number => "OP17-032"; }
public sealed class OP17_033_Effect : OP17CardEffect { protected override string Number => "OP17-033"; }
public sealed class OP17_034_Effect : OP17CardEffect { protected override string Number => "OP17-034"; }
public sealed class OP17_036_Effect : OP17CardEffect { protected override string Number => "OP17-036"; }
public sealed class OP17_037_Effect : OP17CardEffect { protected override string Number => "OP17-037"; }
public sealed class OP17_038_Effect : OP17CardEffect { protected override string Number => "OP17-038"; }
public sealed class OP17_039_Effect : OP17CardEffect { protected override string Number => "OP17-039"; }
public sealed class OP17_040_Effect : OP17CardEffect { protected override string Number => "OP17-040"; }
public sealed class OP17_041_Effect : OP17CardEffect { protected override string Number => "OP17-041"; }
public sealed class OP17_042_Effect : OP17CardEffect { protected override string Number => "OP17-042"; }
public sealed class OP17_043_Effect : OP17CardEffect { protected override string Number => "OP17-043"; }
public sealed class OP17_044_Effect : OP17CardEffect { protected override string Number => "OP17-044"; }
public sealed class OP17_045_Effect : OP17CardEffect { protected override string Number => "OP17-045"; }
public sealed class OP17_046_Effect : OP17CardEffect { protected override string Number => "OP17-046"; }
public sealed class OP17_047_Effect : OP17CardEffect { protected override string Number => "OP17-047"; }
public sealed class OP17_048_Effect : OP17CardEffect { protected override string Number => "OP17-048"; }
public sealed class OP17_049_Effect : OP17CardEffect { protected override string Number => "OP17-049"; }
public sealed class OP17_050_Effect : OP17CardEffect { protected override string Number => "OP17-050"; }
public sealed class OP17_052_Effect : OP17CardEffect { protected override string Number => "OP17-052"; }
public sealed class OP17_053_Effect : OP17CardEffect { protected override string Number => "OP17-053"; }
public sealed class OP17_054_Effect : OP17CardEffect { protected override string Number => "OP17-054"; }
public sealed class OP17_055_Effect : OP17CardEffect { protected override string Number => "OP17-055"; }
public sealed class OP17_056_Effect : OP17CardEffect { protected override string Number => "OP17-056"; }
public sealed class OP17_057_Effect : OP17CardEffect { protected override string Number => "OP17-057"; }
public sealed class OP17_058_Effect : OP17CardEffect { protected override string Number => "OP17-058"; }
public sealed class OP17_059_Effect : OP17CardEffect { protected override string Number => "OP17-059"; }
public sealed class OP17_060_Effect : OP17CardEffect { protected override string Number => "OP17-060"; }
public sealed class OP17_061_Effect : OP17CardEffect { protected override string Number => "OP17-061"; }
public sealed class OP17_062_Effect : OP17CardEffect { protected override string Number => "OP17-062"; }
public sealed class OP17_063_Effect : OP17CardEffect { protected override string Number => "OP17-063"; }
public sealed class OP17_064_Effect : OP17CardEffect { protected override string Number => "OP17-064"; }
public sealed class OP17_065_Effect : OP17CardEffect { protected override string Number => "OP17-065"; }
public sealed class OP17_066_Effect : OP17CardEffect { protected override string Number => "OP17-066"; }
public sealed class OP17_067_Effect : OP17CardEffect { protected override string Number => "OP17-067"; }
public sealed class OP17_068_Effect : OP17CardEffect { protected override string Number => "OP17-068"; }
public sealed class OP17_069_Effect : OP17CardEffect { protected override string Number => "OP17-069"; }
public sealed class OP17_071_Effect : OP17CardEffect { protected override string Number => "OP17-071"; }
public sealed class OP17_072_Effect : OP17CardEffect { protected override string Number => "OP17-072"; }
public sealed class OP17_073_Effect : OP17CardEffect { protected override string Number => "OP17-073"; }
public sealed class OP17_074_Effect : OP17CardEffect { protected override string Number => "OP17-074"; }
public sealed class OP17_075_Effect : OP17CardEffect { protected override string Number => "OP17-075"; }
public sealed class OP17_076_Effect : OP17CardEffect { protected override string Number => "OP17-076"; }
public sealed class OP17_077_Effect : OP17CardEffect { protected override string Number => "OP17-077"; }
public sealed class OP17_078_Effect : OP17CardEffect { protected override string Number => "OP17-078"; }
public sealed class OP17_079_Effect : OP17CardEffect { protected override string Number => "OP17-079"; }
public sealed class OP17_080_Effect : OP17CardEffect { protected override string Number => "OP17-080"; }
public sealed class OP17_081_Effect : OP17CardEffect { protected override string Number => "OP17-081"; }
public sealed class OP17_082_Effect : OP17CardEffect { protected override string Number => "OP17-082"; }
public sealed class OP17_083_Effect : OP17CardEffect { protected override string Number => "OP17-083"; }
public sealed class OP17_084_Effect : OP17CardEffect { protected override string Number => "OP17-084"; }
public sealed class OP17_085_Effect : OP17CardEffect { protected override string Number => "OP17-085"; }
public sealed class OP17_086_Effect : OP17CardEffect { protected override string Number => "OP17-086"; }
public sealed class OP17_087_Effect : OP17CardEffect { protected override string Number => "OP17-087"; }
public sealed class OP17_089_Effect : OP17CardEffect { protected override string Number => "OP17-089"; }
public sealed class OP17_090_Effect : OP17CardEffect { protected override string Number => "OP17-090"; }
public sealed class OP17_091_Effect : OP17CardEffect { protected override string Number => "OP17-091"; }
public sealed class OP17_092_Effect : OP17CardEffect { protected override string Number => "OP17-092"; }
public sealed class OP17_093_Effect : OP17CardEffect { protected override string Number => "OP17-093"; }
public sealed class OP17_094_Effect : OP17CardEffect { protected override string Number => "OP17-094"; }
public sealed class OP17_095_Effect : OP17CardEffect { protected override string Number => "OP17-095"; }
public sealed class OP17_096_Effect : OP17CardEffect { protected override string Number => "OP17-096"; }
public sealed class OP17_097_Effect : OP17CardEffect { protected override string Number => "OP17-097"; }
public sealed class OP17_098_Effect : OP17CardEffect { protected override string Number => "OP17-098"; }
public sealed class OP17_099_Effect : OP17CardEffect { protected override string Number => "OP17-099"; }
public sealed class OP17_101_Effect : OP17CardEffect { protected override string Number => "OP17-101"; }
public sealed class OP17_102_Effect : OP17CardEffect { protected override string Number => "OP17-102"; }
public sealed class OP17_103_Effect : OP17CardEffect { protected override string Number => "OP17-103"; }
public sealed class OP17_104_Effect : OP17CardEffect { protected override string Number => "OP17-104"; }
public sealed class OP17_105_Effect : OP17CardEffect { protected override string Number => "OP17-105"; }
public sealed class OP17_106_Effect : OP17CardEffect { protected override string Number => "OP17-106"; }
public sealed class OP17_107_Effect : OP17CardEffect { protected override string Number => "OP17-107"; }
public sealed class OP17_108_Effect : OP17CardEffect { protected override string Number => "OP17-108"; }
public sealed class OP17_109_Effect : OP17CardEffect { protected override string Number => "OP17-109"; }
public sealed class OP17_110_Effect : OP17CardEffect { protected override string Number => "OP17-110"; }
public sealed class OP17_111_Effect : OP17CardEffect { protected override string Number => "OP17-111"; }
public sealed class OP17_112_Effect : OP17CardEffect { protected override string Number => "OP17-112"; }
public sealed class OP17_113_Effect : OP17CardEffect { protected override string Number => "OP17-113"; }
public sealed class OP17_114_Effect : OP17CardEffect { protected override string Number => "OP17-114"; }
public sealed class OP17_115_Effect : OP17CardEffect { protected override string Number => "OP17-115"; }
public sealed class OP17_116_Effect : OP17CardEffect { protected override string Number => "OP17-116"; }
public sealed class OP17_117_Effect : OP17CardEffect { protected override string Number => "OP17-117"; }
public sealed class OP17_118_Effect : OP17CardEffect { protected override string Number => "OP17-118"; }
public sealed class OP17_119_Effect : OP17CardEffect { protected override string Number => "OP17-119"; }
