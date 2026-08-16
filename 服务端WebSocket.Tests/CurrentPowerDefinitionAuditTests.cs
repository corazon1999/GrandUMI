using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

public class CurrentPowerDefinitionAuditTests
{
    private static readonly string[] CurrentPowerCards =
    [
        "EB02-027",
        "OP01-007", "OP01-017", "OP01-026",
        "OP02-011", "OP02-017", "OP02-021",
        "OP03-018",
        "OP04-008",
        "OP05-010", "OP05-011", "OP05-012", "OP05-017", "OP05-020", "OP05-068",
        "OP06-019",
        "OP07-011",
        "OP08-012", "OP08-019",
        "OP09-014", "OP09-114",
        "OP10-019", "OP10-020",
        "OP11-020",
        "OP16-006", "OP16-008",
        "ST01-015",
        "ST10-001", "ST10-015", "ST10-016",
        "ST21-016",
        "ST30-015",
    ];

    [Fact]
    public void ConfirmedFieldTargetsUseCurrentPower()
    {
        var definitions = LoadDefinitions();

        Assert.Equal(32, CurrentPowerCards.Length);
        Assert.Equal(CurrentPowerCards.Length, CurrentPowerCards.Distinct().Count());

        foreach (var number in CurrentPowerCards)
        {
            Assert.True(definitions.TryGetValue(number, out var definition), $"找不到 {number} 的 DSL 定义");
            var choices = EnumerateObjects(definition)
                .Where(IsFieldChoiceWithPowerLimit)
                .ToList();

            var expectedChoiceCount = number is "OP03-018" ? 3
                : number is "OP06-019" or "ST01-015" or "ST21-016" ? 2
                : 1;
            Assert.Equal(expectedChoiceCount, choices.Count);
            Assert.All(choices, choice =>
            {
                var filter = choice.GetProperty("filter");
                Assert.True(filter.TryGetProperty("currentPowerLte", out _),
                    $"{number} 的场上目标仍未使用 currentPowerLte");
                Assert.False(filter.TryGetProperty("originalPowerLte", out _),
                    $"{number} 的场上目标仍残留 originalPowerLte");
            });
        }
    }

    [Theory]
    [InlineData("OP16-010")]
    [InlineData("ST30-014")]
    public void ConfirmedExceptionsKeepOriginalPower(string number)
    {
        var definitions = LoadDefinitions();
        Assert.True(definitions.TryGetValue(number, out var definition), $"找不到 {number} 的 DSL 定义");

        var choices = EnumerateObjects(definition)
            .Where(IsFieldChoiceWithPowerLimit)
            .ToList();

        Assert.Equal(number == "ST30-014" ? 2 : 1, choices.Count);
        Assert.All(choices, choice =>
        {
            var filter = choice.GetProperty("filter");
            Assert.True(filter.TryGetProperty("originalPowerLte", out _),
                $"{number} 应继续使用 originalPowerLte");
            Assert.False(filter.TryGetProperty("currentPowerLte", out _),
                $"{number} 不应改为 currentPowerLte");
        });
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

    private static bool IsFieldChoiceWithPowerLimit(JsonElement element)
    {
        if (!element.TryGetProperty("op", out var op) || op.GetString() != "Choose" ||
            !element.TryGetProperty("prompt", out var prompt) ||
            !element.TryGetProperty("filter", out var filter))
            return false;

        var promptName = prompt.GetString() ?? "";
        var isFieldPrompt = promptName is "OpponentCharacter" or "OpponentLeaderOrCharacter"
            or "OwnCharacter" or "OwnLeaderOrCharacter" or "AnyCharacter";
        return isFieldPrompt &&
            (filter.TryGetProperty("currentPowerLte", out _) ||
             filter.TryGetProperty("originalPowerLte", out _));
    }
}
