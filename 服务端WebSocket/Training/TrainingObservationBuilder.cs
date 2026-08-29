using System.Text.Json;
using GrandUMI.Effects;
using GrandUMI.Game;

namespace GrandUMI.Training;

public sealed record TrainingObservation(
    string Schema,
    JsonElement Payload,
    string StableHash);

public sealed record ObservationPrivacyReport(
    bool Safe,
    IReadOnlyList<string> Violations);

/// <summary>
/// 动作前训练 observation。只按固定白名单输出 actor 当时可见的信息，不复用私有快照。
/// </summary>
public static class TrainingObservationBuilder
{
    public const string Schema = "grandumi.training_observation.v1";

    public static TrainingObservation Build(GameState state, int actorSeat)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (actorSeat is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(actorSeat));
        var opponentSeat = 1 - actorSeat;
        var payload = JsonSerializer.SerializeToElement(new
        {
            schema = Schema,
            rulesetId = state.RulesetId,
            phase = state.Phase.ToString(),
            turnCount = state.TurnCount,
            tick = state.Tick,
            isSelfTurn = state.CurrentTurnPlayer == actorSeat,
            firstPlayerChosen = state.StartingPlayerChosen,
            firstPlayerIsSelf = state.StartingPlayerChosen && state.FirstPlayer == actorSeat,
            openingStage = state.OpeningStage.ToString(),
            isGameOver = state.IsGameOver,
            winner = state.IsGameOver
                ? state.IsDraw ? "draw" : state.WinnerIndex == actorSeat ? "self" : "opponent"
                : null,
            self = BuildPlayer(state, actorSeat, revealHand: true),
            opponent = BuildPlayer(state, opponentSeat, revealHand: false),
            battle = BuildBattle(state, actorSeat),
            prompt = BuildPrompt(state, actorSeat),
        });
        var canonical = CanonicalJson.NormalizeObject(payload);
        var observation = new TrainingObservation(Schema, canonical, CanonicalJson.Hash(canonical));
        var privacy = TrainingObservationPrivacyScanner.Scan(observation, state);
        if (!privacy.Safe)
            throw new InvalidOperationException(
                $"训练 observation 隐私扫描失败：{string.Join("; ", privacy.Violations)}");
        return observation;
    }

    private static object BuildPlayer(GameState state, int seat, bool revealHand)
    {
        var player = state.Players[seat];
        var hand = revealHand
            ? new
            {
                count = player.Hand.Count,
                cards = player.Hand.Select((card, index) => new
                {
                    index,
                    number = card.Info.Number,
                    kind = card.Info.Kind.ToString(),
                    effectiveCost = state.HandPlayCost(seat, card),
                    counter = HandStaticCounter.Value(state, seat, card),
                    effectTags = card.Info.EffectTags.Order(StringComparer.Ordinal).ToArray(),
                }).ToArray(),
            }
            : null;

        return new
        {
            leader = BuildFieldCard(state, seat, player.Leader, "leader", 0),
            characters = player.Characters.Select((card, index) =>
                BuildFieldCard(state, seat, card, "character", index)).ToArray(),
            stage = player.StageCard is null
                ? null
                : BuildFieldCard(state, seat, player.StageCard, "stage", 0),
            handCount = player.Hand.Count,
            hand,
            trash = player.Trash.Select((card, index) => new
            {
                index,
                number = card.Info.Number,
                kind = card.Info.Kind.ToString(),
            }).ToArray(),
            deckCount = player.Deck.Count,
            life = player.LifeArea.Select((card, index) => new
            {
                index,
                faceUp = card.IsLifeFaceUp,
                number = card.IsLifeFaceUp ? card.Info.Number : null,
            }).ToArray(),
            activeDon = player.ActiveDonCount,
            restDon = player.RestDonCount,
            attachedDon = player.CostArea.Count(don => don.State == DonState.Attached),
            donDeckCount = player.DonDeck.Count,
            mulliganDone = player.MulliganDone,
            canRedraw = player.HasReDraw,
        };
    }

    private static object BuildFieldCard(
        GameState state,
        int owner,
        CardInstance card,
        string zone,
        int index)
        => new
        {
            zone,
            index,
            number = card.Info.Number,
            kind = card.Info.Kind.ToString(),
            tapped = card.IsTapped,
            currentPower = state.CurrentPowerOf(owner, card),
            currentCost = state.CurrentCostOf(owner, card),
            attachedDon = state.Players[owner].AttachedDonCount(card.Id),
            turnPlayed = card.TurnPlayed,
            effectsNullified = card.IsEffectsNullified || state.IsContinuouslyNullified(card),
            cannotActivateNextReset = card.CannotActivateNextReset,
            gainedKeywords = card.GainedKeywords.Select(keyword => keyword.Keyword)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            restrictions = card.Restrictions.Select(restriction => restriction.Kind.ToString())
                .Order(StringComparer.Ordinal)
                .ToArray(),
            effectTags = card.Info.EffectTags.Order(StringComparer.Ordinal).ToArray(),
        };

    private static object? BuildBattle(GameState state, int actorSeat)
    {
        if (state.CurrentBattle is not { } battle) return null;
        return new
        {
            attacker = RelativeCardReference(state, actorSeat, battle.AttackerCardId),
            target = battle.TargetIsLeader
                ? battle.DefenderPlayerIndex == actorSeat ? "self.leader" : "opponent.leader"
                : battle.TargetCardId.HasValue
                    ? RelativeCardReference(state, actorSeat, battle.TargetCardId.Value)
                    : null,
            blocker = battle.ReplacedByBlockerCardId.HasValue
                ? RelativeCardReference(state, actorSeat, battle.ReplacedByBlockerCardId.Value)
                : null,
            attackerBonus = battle.AttackerBattleBonus,
            defenderBonus = battle.DefenderBattleBonus,
            actorIsDefender = battle.DefenderPlayerIndex == actorSeat,
        };
    }

    private static object? BuildPrompt(GameState state, int actorSeat)
    {
        if (state.PendingPrompt is not { } prompt || prompt.PlayerIndex != actorSeat) return null;
        var options = ReadStringList(prompt.Extra, "options");
        var choiceCards = ReadChoiceCardMap(prompt.Extra);
        var choiceZones = ReadChoiceZoneMap(prompt.Extra);
        return new
        {
            kind = prompt.Kind,
            minChoose = prompt.MinChoose,
            maxChoose = prompt.MaxChoose,
            choices = prompt.ValidChoices.Select((choice, index) =>
            {
                var reference = Guid.TryParse(choice, out var cardId)
                    ? RelativeCardReference(state, actorSeat, cardId)
                    : null;
                choiceCards.TryGetValue(choice, out var number);
                choiceZones.TryGetValue(choice, out var zone);
                string? label = null;
                if (int.TryParse(choice, out var optionIndex)
                    && optionIndex >= 0
                    && optionIndex < options.Length)
                    label = options[optionIndex];
                return new
                {
                    index,
                    reference,
                    number,
                    zone,
                    value = reference is null && number is null ? choice : null,
                    label,
                };
            }).ToArray(),
            sourceNumber = ReadString(prompt.Extra, "sourceNumber"),
            isCost = ReadBoolean(prompt.Extra, "isCost"),
            lifeCardNumber = ReadString(prompt.Extra, "lifeCardNumber"),
            hasRealTrigger = ReadBoolean(prompt.Extra, "hasRealTrigger"),
        };
    }

    private static string? RelativeCardReference(GameState state, int actorSeat, Guid cardId)
    {
        for (var seat = 0; seat < state.Players.Length; seat++)
        {
            var prefix = seat == actorSeat ? "self" : "opponent";
            var player = state.Players[seat];
            if (player.Leader.Id == cardId) return $"{prefix}.leader";
            if (player.StageCard?.Id == cardId) return $"{prefix}.stage";
            var characterIndex = player.Characters.ToList().FindIndex(card => card.Id == cardId);
            if (characterIndex >= 0) return $"{prefix}.character.{characterIndex}";
            if (seat == actorSeat)
            {
                var handIndex = player.Hand.FindIndex(card => card.Id == cardId);
                if (handIndex >= 0) return $"self.hand.{handIndex}";
            }
            var trashIndex = player.Trash.FindIndex(card => card.Id == cardId);
            if (trashIndex >= 0) return $"{prefix}.trash.{trashIndex}";
            var lifeIndex = player.LifeArea.FindIndex(card => card.Id == cardId);
            if (lifeIndex >= 0 && player.LifeArea[lifeIndex].IsLifeFaceUp)
                return $"{prefix}.life.{lifeIndex}";
        }
        return null;
    }

    private static string[] ReadStringList(IReadOnlyDictionary<string, object?> extra, string key)
    {
        if (!extra.TryGetValue(key, out var raw) || raw is null) return Array.Empty<string>();
        if (raw is IEnumerable<string> strings) return strings.ToArray();
        var element = JsonSerializer.SerializeToElement(raw);
        if (element.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
    }

    private static Dictionary<string, string> ReadChoiceCardMap(IReadOnlyDictionary<string, object?> extra)
        => ReadPairMap(extra, "choiceCards", "id", "number");

    private static Dictionary<string, string> ReadChoiceZoneMap(IReadOnlyDictionary<string, object?> extra)
        => ReadPairMap(extra, "choiceCardZones", "id", "zone");

    private static Dictionary<string, string> ReadPairMap(
        IReadOnlyDictionary<string, object?> extra,
        string key,
        string keyProperty,
        string valueProperty)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!extra.TryGetValue(key, out var raw) || raw is null) return result;
        var element = JsonSerializer.SerializeToElement(raw);
        if (element.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty(keyProperty, out var mapKey)
                || mapKey.ValueKind != JsonValueKind.String
                || !item.TryGetProperty(valueProperty, out var mapValue)
                || mapValue.ValueKind != JsonValueKind.String)
                continue;
            result[mapKey.GetString() ?? string.Empty] = mapValue.GetString() ?? string.Empty;
        }
        return result;
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> extra, string key)
    {
        if (!extra.TryGetValue(key, out var raw) || raw is null) return null;
        if (raw is string value) return value;
        var element = JsonSerializer.SerializeToElement(raw);
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static bool? ReadBoolean(IReadOnlyDictionary<string, object?> extra, string key)
    {
        if (!extra.TryGetValue(key, out var raw) || raw is null) return null;
        if (raw is bool value) return value;
        var element = JsonSerializer.SerializeToElement(raw);
        return element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : null;
    }
}

/// <summary>防止字段回归或敏感字符串意外进入训练 observation。</summary>
public static class TrainingObservationPrivacyScanner
{
    private static readonly HashSet<string> ForbiddenProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "account",
        "accountName",
        "session",
        "sessionId",
        "displayName",
        "visibleName",
        "playerName",
        "replayHands",
        "deckCards",
        "deckCardNumbers",
        "lifeNumbers",
    };

    public static ObservationPrivacyReport Scan(TrainingObservation observation, GameState state)
    {
        var violations = new List<string>();
        var sensitiveValues = state.Players
            .SelectMany(player => new[] { player.AccountName, player.SessionId, player.DisplayName, player.VisibleName })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        ScanElement(observation.Payload, "$", sensitiveValues, violations);
        return new ObservationPrivacyReport(
            violations.Count == 0,
            Array.AsReadOnly(violations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
    }

    private static void ScanElement(
        JsonElement element,
        string path,
        IReadOnlySet<string> sensitiveValues,
        ICollection<string> violations)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var childPath = $"{path}.{property.Name}";
                if (ForbiddenProperties.Contains(property.Name))
                    violations.Add($"forbidden_property:{childPath}");
                ScanElement(property.Value, childPath, sensitiveValues, violations);
            }
            return;
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                ScanElement(item, $"{path}[{index++}]", sensitiveValues, violations);
            return;
        }
        if (element.ValueKind == JsonValueKind.String
            && sensitiveValues.Contains(element.GetString() ?? string.Empty))
            violations.Add($"sensitive_value:{path}");
    }
}
