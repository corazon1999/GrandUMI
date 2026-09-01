using GrandUMI.Cards;
using GrandUMI.Effects;

namespace GrandUMI.Game.Debug;

/// <summary>
/// OP17 专项巡检结果。该巡检从真实对局的 GM 面板发起，但每张卡都在独立的宽松场景中
/// 运行，以免一张卡的效果污染下一张卡或破坏正在观看的对局。
/// </summary>
public sealed record OP17CoverageCardResult(
    string Number,
    string Name,
    string Color,
    bool Passed,
    IReadOnlyList<string> Triggers,
    string Message);

public sealed record OP17CoverageReport(
    string Color,
    int Total,
    int Passed,
    int Failed,
    IReadOnlyList<OP17CoverageCardResult> Results);

/// <summary>
/// 使用正式 CardDatabase、ScriptedEffectRegistry 与 EffectRuntime 跑 OP17 指定颜色的全部卡。
/// 交互选择由确定性的自动 Prompt 完成，保证批量巡检不会停在人工选择窗口。
/// </summary>
public static class OP17CoverageRunner
{
    private static readonly HashSet<string> BlankCards = new(StringComparer.Ordinal)
    {
        "OP17-006", "OP17-035", "OP17-051", "OP17-070", "OP17-088", "OP17-100",
    };

    public static async Task<OP17CoverageReport> RunColorAsync(string color)
    {
        var cards = CardDatabase.GetBySet("OP17")
            .Where(card => card.ColorList.Contains(color, StringComparer.Ordinal))
            .OrderBy(card => card.Number, StringComparer.Ordinal)
            .ToList();

        var results = new List<OP17CoverageCardResult>(cards.Count);
        foreach (var card in cards)
            results.Add(await RunCardAsync(card));

        int passed = results.Count(result => result.Passed);
        return new OP17CoverageReport(color, results.Count, passed, results.Count - passed, results);
    }

    public static IReadOnlyList<string> Colors()
        => CardDatabase.GetBySet("OP17")
            .SelectMany(card => card.ColorList)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(color => ColorOrder(color))
            .ToList();

    private static async Task<OP17CoverageCardResult> RunCardAsync(CardInfo info)
    {
        var triggerNames = new List<string>();
        var errors = new List<string>();

        bool isBlank = BlankCards.Contains(info.Number);
        var registration = ScriptedEffectRegistry.TryGet(info.Number);
        if (!isBlank && registration is null)
            errors.Add("缺少效果脚本注册");
        if (isBlank && registration is not null)
            errors.Add("白板卡不应注册效果脚本");

        var triggers = ApplicableTriggers(info).ToList();
        if (isBlank)
        {
            var state = BuildMaxScenario(info);
            var source = PlaceSource(state, info, EffectTrigger.OnEnterField);
            bool placed = info.Kind switch
            {
                CardKind.Character => state.Players[0].Characters.Contains(source),
                CardKind.Stage => ReferenceEquals(state.Players[0].StageCard, source),
                CardKind.Leader => ReferenceEquals(state.Players[0].Leader, source),
                _ => true,
            };
            triggerNames.Add("静态规则/区域放置");
            if (!placed) errors.Add("静态卡未能进入预期区域");
        }

        foreach (var trigger in triggers)
        {
            var state = BuildMaxScenario(info);
            var source = PlaceSource(state, info, trigger);
            PrepareBattle(state, source, trigger);
            var payload = BuildPayload(state, source, trigger);

            try
            {
                await EffectRuntime.Resolve(state, 0, source, trigger, new DebugAutoPromptService(), payload);
                ValidateState(state);
                triggerNames.Add(trigger.ToString());
            }
            catch (Exception ex)
            {
                errors.Add($"{trigger}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (triggerNames.Count == 0)
            triggerNames.Add("卡牌数据/静态能力");

        return new OP17CoverageCardResult(
            info.Number,
            info.Name,
            info.Color,
            errors.Count == 0,
            triggerNames,
            errors.Count == 0 ? "通过" : string.Join("；", errors));
    }

    private static IEnumerable<EffectTrigger> ApplicableTriggers(CardInfo info)
    {
        var result = new HashSet<EffectTrigger>();
        foreach (var tag in info.EffectTags)
            if (Enum.TryParse<EffectTrigger>(tag, out var trigger))
                result.Add(trigger);

        if (info.Kind == CardKind.Leader)
            result.Add(EffectTrigger.OnGameStart);
        if (info.Kind is CardKind.Character or CardKind.Stage)
            result.Add(EffectTrigger.OnEnterField);
        if (!string.IsNullOrWhiteSpace(info.Trigger))
            result.Add(EffectTrigger.OnLifeRevealTrigger);
        return result.OrderBy(trigger => trigger);
    }

    private static GameState BuildMaxScenario(CardInfo sourceInfo)
    {
        var allOp17 = CardDatabase.GetBySet("OP17").ToList();
        var myLeader = sourceInfo.Kind == CardKind.Leader
            ? sourceInfo
            : allOp17.First(card => card.Kind == CardKind.Leader && card.SharesColorWith(sourceInfo));
        var opponentLeader = allOp17.First(card => card.Kind == CardKind.Leader && !ReferenceEquals(card, myLeader));
        var myPool = allOp17.Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(myLeader)).ToList();
        var opponentPool = allOp17.Where(card => card.Kind == CardKind.Character && !card.SharesColorWith(myLeader)).ToList();
        var fallback = myPool.First(card => card.Kind == CardKind.Character);

        var state = new GameState { RoomId = $"op17-coverage-{sourceInfo.Number}", FirstPlayer = 0, RngSeed = 17 };
        state.Players[0] = new PlayerState
        {
            SessionId = "coverage-0",
            AccountName = "OP17巡检方",
            Leader = new CardInstance { Info = myLeader.WithWildcardKeywords() },
        };
        state.Players[1] = new PlayerState
        {
            SessionId = "coverage-1",
            AccountName = "OP17对照方",
            Leader = new CardInstance { Info = opponentLeader.WithWildcardKeywords() },
        };
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 2;
        state.Phase = Phase.Main;

        var me = state.Players[0];
        var opponent = state.Players[1];
        for (int i = 0; i < 10; i++)
        {
            me.CostArea.Add(new DonCard { State = DonState.Active });
            opponent.CostArea.Add(new DonCard { State = DonState.Active });
        }
        for (int i = 0; i < 2; i++)
            me.CostArea.Add(new DonCard { State = DonState.Attached, AttachedToCardId = me.Leader.Id });

        for (int i = 0; i < 50; i++)
        {
            me.Deck.Add(new CardInstance { Info = myPool[i % myPool.Count] });
            opponent.Deck.Add(new CardInstance { Info = opponentPool[i % opponentPool.Count] });
        }
        for (int i = 0; i < 5; i++)
        {
            me.LifeArea.Add(new CardInstance { Info = myPool[i % myPool.Count] });
            opponent.LifeArea.Add(new CardInstance { Info = opponentPool[i % opponentPool.Count] });
        }
        for (int i = 0; i < 10; i++)
        {
            me.Hand.Add(new CardInstance { Info = myPool[i % myPool.Count] });
            opponent.Hand.Add(new CardInstance { Info = opponentPool[i % opponentPool.Count] });
        }
        for (int i = 0; i < 4; i++)
        {
            me.Characters.Add(new CardInstance { Info = myPool.FirstOrDefault(card => card.Kind == CardKind.Character) ?? fallback, TurnPlayed = 0 });
            var target = new CardInstance { Info = opponentPool[i % opponentPool.Count], TurnPlayed = 0, IsTapped = i >= 2 };
            opponent.Characters.Add(target);
        }
        for (int i = 0; i < 6; i++)
            me.Trash.Add(new CardInstance { Info = myPool[i % myPool.Count] });

        return state;
    }

    private static CardInstance PlaceSource(GameState state, CardInfo info, EffectTrigger trigger)
    {
        if (info.Kind == CardKind.Leader)
            return state.Players[0].Leader;

        var source = new CardInstance { Info = info, TurnPlayed = trigger == EffectTrigger.ActivatedMain ? state.TurnCount : 0 };
        if (trigger is EffectTrigger.OnKO or EffectTrigger.OnLifeRevealTrigger)
            state.Players[0].Trash.Add(source);
        else if (info.Kind == CardKind.Character)
        {
            if (state.Players[0].Characters.Count >= 5)
                state.Players[0].Characters.RemoveAt(0);
            state.Players[0].Characters.Add(source);
        }
        else if (info.Kind == CardKind.Stage)
            state.Players[0].StageCard = source;
        else
            state.Players[0].Hand.Add(source);
        return source;
    }

    private static void PrepareBattle(GameState state, CardInstance source, EffectTrigger trigger)
    {
        if (trigger is not (EffectTrigger.OnAttackDeclare
            or EffectTrigger.OnOppAttackDeclare
            or EffectTrigger.OnBlockDeclare
            or EffectTrigger.OnLeaderBattle
            or EffectTrigger.OnBattleEnd))
            return;

        bool opponentAttacks = trigger == EffectTrigger.OnOppAttackDeclare;
        int attackerSide = opponentAttacks ? 1 : 0;
        var attacker = opponentAttacks
            ? state.Players[1].Leader
            : trigger == EffectTrigger.OnLeaderBattle || source.Info.Kind == CardKind.Leader
                ? state.Players[0].Leader
                : source;
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = attackerSide,
            AttackerCardId = attacker.Id,
            DefenderPlayerIndex = 1 - attackerSide,
            TargetIsLeader = true,
        };
        state.Players[attackerSide].CostArea.Add(new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = attacker.Id,
        });
    }

    private static Dictionary<string, object?> BuildPayload(GameState state, CardInstance source, EffectTrigger trigger)
        => new()
        {
            ["victimId"] = source.Id.ToString(),
            ["victimOwner"] = 0,
            ["cardId"] = source.Id.ToString(),
            ["owner"] = 0,
            ["count"] = 2,
            ["restedCardId"] = source.Id.ToString(),
            ["restedOwner"] = 0,
            ["actingSide"] = trigger == EffectTrigger.OnOppAttackDeclare ? 1 : 0,
            ["AttackerIdx"] = trigger == EffectTrigger.OnOppAttackDeclare ? 1 : 0,
            ["attackerId"] = state.CurrentBattle?.AttackerCardId.ToString(),
            ["targetLeaderOwner"] = state.CurrentBattle?.DefenderPlayerIndex,
        };

    private static void ValidateState(GameState state)
    {
        foreach (var player in state.Players)
        {
            if (player.ActiveDonCount < 0 || player.Hand.Count < 0 || player.Deck.Count < 0 || player.LifeArea.Count < 0)
                throw new InvalidOperationException("效果结算后出现非法负数区域状态");
        }
    }

    private static int ColorOrder(string color) => color switch
    {
        "红" => 0,
        "绿" => 1,
        "蓝" => 2,
        "紫" => 3,
        "黑" => 4,
        "黄" => 5,
        _ => 99,
    };

    private sealed class DebugAutoPromptService : IPromptService
    {
        public Task<List<string>> ChooseCards(int playerIdx, string kind, string text,
            IReadOnlyList<string> validChoices, int min, int max,
            Dictionary<string, object?>? extra = null)
            => Task.FromResult(validChoices.Take(Math.Min(max, validChoices.Count)).ToList());

        public Task<bool> ConfirmOptional(int playerIdx, string text) => Task.FromResult(true);

        public Task<int> ChooseOption(int playerIdx, string text, IReadOnlyList<string> options,
            Dictionary<string, object?>? extra = null)
            => Task.FromResult(0);

        public Task<bool> AskLifeTrigger(int playerIdx, CardInstance lifeCard, bool hasRealTrigger)
            => Task.FromResult(hasRealTrigger);
    }
}
