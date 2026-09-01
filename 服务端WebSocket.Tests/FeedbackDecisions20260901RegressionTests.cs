using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

public class FeedbackDecisions20260901RegressionTests
{
    private static readonly string[] UpToActiveOwnDonCards =
    [
        "EB03-017", "OP01-034", "OP01-046", "OP02-029", "OP02-048",
        "OP04-019", "OP04-029", "OP04-038", "OP06-028", "OP07-021",
        "OP08-032", "OP08-039", "OP10-067", "OP10-072", "OP11-021",
        "OP13-118", "OP14-022", "OP14-024", "OP14-039", "P-102",
        "P-108", "PRB02-004", "ST02-015", "ST02-016", "ST11-004",
        "ST24-002", "ST24-003",
    ];

    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public void AuditedUpToActiveOwnDonDefinitionsRequireExplicitQuantityChoice()
    {
        var definitions = LoadDefinitions();

        foreach (var cardNumber in UpToActiveOwnDonCards)
        {
            Assert.True(definitions.TryGetValue(cardNumber, out var definition), $"缺少 {cardNumber} 的 DSL 定义。");
            var operations = EnumerateObjects(definition)
                .Where(element => element.TryGetProperty("op", out var op)
                    && op.GetString() == "ActiveOwnDon")
                .ToList();

            Assert.NotEmpty(operations);
            Assert.All(operations, operation =>
                Assert.True(operation.TryGetProperty("chooseCount", out var chooseCount)
                    && chooseCount.ValueKind == JsonValueKind.True,
                    $"{cardNumber} 的 ActiveOwnDon 必须显式启用 0..N 数量选择。"));
        }
    }

    [Fact]
    public async Task OP13_064_OnEnterPowerReductionUsesResolutionTimeCharacterSnapshot()
    {
        var state = TestScene.New()
            .MyCharacter("OP13-064")
            .MyActiveDon(3)
            .OppCharacter("OP15-003")
            .Build();
        var roger = Assert.Single(state.Players[0].Characters);
        var existingOpponent = Assert.Single(state.Players[1].Characters);

        await EffectRuntime.Resolve(
            state, 0, roger, EffectTrigger.OnEnterField, new MockPromptService());

        var laterOpponent = Card("OP15-004");
        state.Players[1].Characters.Add(laterOpponent);

        Assert.Equal(-2000, Assert.Single(existingOpponent.PowerModsUntilOppEnd).Delta);
        Assert.Empty(laterOpponent.PowerModsUntilOppEnd);
        Assert.Equal(2000, Assert.Single(state.Players[0].Leader.PowerModsUntilOppEnd).Delta);
    }

    [Fact]
    public async Task OP13_064_NullifiedRogerLeaderDoesNotAttachDonDuringDonPhase()
    {
        var state = TestScene.New("OP13-003")
            .MyCharacter("OP13-064")
            .MyActiveDon(1)
            .Build();
        state.TurnCount = 2;
        var me = state.Players[0];
        me.DonDeck.AddRange([
            new DonCard { State = DonState.InDeck },
            new DonCard { State = DonState.InDeck },
        ]);

        await EffectRuntime.Resolve(
            state, 0, me.Leader, EffectTrigger.OnGameStart, new MockPromptService());
        await EffectRuntime.Resolve(
            state,
            0,
            Assert.Single(me.Characters),
            EffectTrigger.OnEnterField,
            new MockPromptService());

        Assert.True(state.IsContinuouslyNullified(me.Leader));
        Assert.Equal(me.Leader.Info.Power, state.CurrentPowerOf(0, me.Leader));

        TurnEngine.EnterDonPhase(state);

        Assert.Equal(3, me.ActiveDonCount);
        Assert.Equal(0, me.AttachedDonCount(me.Leader.Id));
    }

    [Fact]
    public async Task OP13_068_FormerRogerPiratesTraitDoesNotMatchRogerPiratesExactly()
    {
        var state = TestScene.New()
            .MyCharacter("OP13-064")
            .MyCharacter("OP13-068")
            .Build();
        var roger = state.Players[0].Characters.Single(card => card.Info.Number == "OP13-064");
        var formerRogerPirates = state.Players[0].Characters.Single(card => card.Info.Number == "OP13-068");

        await EffectRuntime.Resolve(
            state, 0, roger, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.True(formerRogerPirates.Info.HasKeyword("原罗杰海盗团"));
        Assert.False(formerRogerPirates.Info.HasKeyword("罗杰海盗团"));
        Assert.True(state.IsContinuouslyNullified(formerRogerPirates));
    }

    private static Dictionary<string, JsonElement> LoadDefinitions()
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(FindDefinitionsDirectory(), "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var property in document.RootElement.EnumerateObject())
                result[property.Name] = property.Value.Clone();
        }
        return result;
    }

    private static string FindDefinitionsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "服务端WebSocket", "Effects", "Definitions");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("找不到 Effects/Definitions 目录");
    }

    private static IEnumerable<JsonElement> EnumerateObjects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (var property in element.EnumerateObject())
            foreach (var child in EnumerateObjects(property.Value))
                yield return child;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            foreach (var child in EnumerateObjects(item))
                yield return child;
        }
    }
}
