using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace GrandUMI.Tests;

public class PublicRevealRegressionTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP12_047_RevealsBothSelectedNavyCardsToOpponent()
    {
        var engine = CreateEngine("op12-047-public-reveal", out var opponentMessages);
        var player = engine.State.Players[0];
        var discard = Card("OP01-012");
        var first = Card("OP16-067");
        var second = Card("OP03-089");
        var hidden = Card("OP01-012");
        Assert.True(first.Info.HasKeyword("海军"));
        Assert.True(second.Info.HasKeyword("海军"));
        Assert.False(hidden.Info.HasKeyword("海军"));
        player.Hand.Clear();
        player.Hand.Add(discard);
        player.Deck.Clear();
        player.Deck.AddRange([first, second, hidden]);

        var resolveTask = EffectRuntime.Resolve(
            engine.State, 0, Card("OP12-047"), EffectTrigger.OnEnterField, engine.Prompts);

        Resolve(await WaitForPrompt(engine, "Option"), engine, "0");
        Resolve(await WaitForPrompt(engine, "SengokuDiscard"), engine, discard.Id.ToString());
        var revealPrompt = await WaitForPrompt(engine, "SengokuReveal");
        Assert.Equal(2, revealPrompt.MaxChoose);
        Resolve(revealPrompt, engine, first.Id.ToString(), second.Id.ToString());
        await resolveTask;

        Assert.Contains(first, player.Hand);
        Assert.Contains(second, player.Hand);
        Assert.Contains(hidden, player.Deck);
        var reveal = Assert.Single(RevealBatches(opponentMessages));
        Assert.Equal(new[] { first.Info.Number, second.Info.Number }, reveal);
        Assert.DoesNotContain(hidden.Info.Number, reveal);
    }

    [Fact]
    public async Task ST12_017_CounterCompletesPowerAndPublicTopDeckEffect()
    {
        var engine = CreateEngine("st12-017-public-reveal", out var opponentMessages);
        var player = engine.State.Players[0];
        var top = Card("OP01-012");
        Assert.Equal(2, top.Info.Cost);
        player.Deck.Clear();
        player.Deck.Add(top);
        int powerBefore = player.Leader.PowerModThisBattle;

        var resolveTask = EffectRuntime.Resolve(
            engine.State, 0, Card("ST12-017"), EffectTrigger.EventCounter, engine.Prompts);

        Resolve(await WaitForPrompt(engine, "OwnLeaderOrCharacter"), engine, player.Leader.Id.ToString());
        Resolve(await WaitForPrompt(engine, "Option"), engine, "0");
        await resolveTask;

        Assert.Equal(powerBefore + 2000, player.Leader.PowerModThisBattle);
        Assert.Contains(top, player.Characters);
        Assert.Equal(new[] { top.Info.Number }, Assert.Single(RevealBatches(opponentMessages)));
    }

    [Fact]
    public async Task OP15_065_PublicRevealDoesNotRequireAPlayerPrompt()
    {
        var engine = CreateEngine("op15-065-public-reveal", out var opponentMessages);
        var player = engine.State.Players[0];
        var top = Card("OP01-012");
        player.Deck.Clear();
        player.Deck.Add(top);
        player.DonDeck.Clear();
        player.DonDeck.Add(new DonCard());

        await EffectRuntime.Resolve(
            engine.State, 0, Card("OP15-065"), EffectTrigger.OnEnterField, engine.Prompts);

        Assert.Null(engine.State.PendingPrompt);
        Assert.Contains(player.CostArea, don => don.State == DonState.Rest);
        Assert.Equal(new[] { top.Info.Number }, Assert.Single(RevealBatches(opponentMessages)));
    }

    [Fact]
    public async Task ST22_011_RevealsExactlyTwoWhitebeardCardsToOpponent()
    {
        var engine = CreateEngine("st22-011-public-reveal", out var opponentMessages);
        var player = engine.State.Players[0];
        var first = Card("ST22-007");
        var second = Card("ST22-012");
        Assert.True(first.Info.HasKeywordContaining("白胡子海盗团"));
        Assert.True(second.Info.HasKeywordContaining("白胡子海盗团"));
        player.Hand.Clear();
        player.Hand.AddRange([first, second]);
        int powerBefore = player.Leader.PowerModThisTurn;

        var resolveTask = EffectRuntime.Resolve(
            engine.State, 0, Card("ST22-011"), EffectTrigger.OnEnterField, engine.Prompts);

        var revealPrompt = await WaitForPrompt(engine, "RevealOwnHand");
        Assert.Equal(2, revealPrompt.MaxChoose);
        Resolve(revealPrompt, engine, first.Id.ToString(), second.Id.ToString());
        await resolveTask;

        Assert.Equal(powerBefore + 2000, player.Leader.PowerModThisTurn);
        Assert.Equal(new[] { first.Info.Number, second.Info.Number },
            Assert.Single(RevealBatches(opponentMessages)));
    }

    [Fact]
    public void EveryCardTextContainingPublicRevealHasAnImplementationRoute()
    {
        var root = FindRepositoryRoot();
        var publicCards = LoadPublicRevealCardNumbers(Path.Combine(root, "卡牌数据"));
        var scriptedFiles = Directory.GetFiles(
                Path.Combine(root, "服务端WebSocket", "Effects", "Scripted"), "*.cs")
            .Select(path => File.ReadAllText(path))
            .ToList();
        var definitions = LoadDefinitionNodes(
            Path.Combine(root, "服务端WebSocket", "Effects", "Definitions"));
        var missing = new List<string>();

        foreach (var number in publicCards)
        {
            var segment = FindScriptedClassSegment(scriptedFiles, number);
            if (segment is not null)
            {
                if (!HasScriptedRevealRoute(segment)) missing.Add(number);
                continue;
            }

            if (!definitions.TryGetValue(number, out var nodes)
                || !nodes.Any(HasDefinitionRevealRoute))
                missing.Add(number);
        }

        Assert.NotEmpty(publicCards);
        Assert.True(missing.Count == 0,
            $"以下含“公开”卡面文本的卡牌没有公共公开实现路径：{string.Join(", ", missing)}");
    }

    private static GameEngine CreateEngine(string roomId, out ConcurrentQueue<string> opponentMessages)
    {
        const string deck = "OP17-099\nOP17-100";
        var engine = new GameEngine(roomId,
            ("s0", "alice", deck), ("s1", "bob", deck), 0, 47);
        opponentMessages = new ConcurrentQueue<string>();
        var messages = opponentMessages;
        engine.OnSendToPlayer = (playerIndex, payload) =>
        {
            if (playerIndex == 1) messages.Enqueue(JsonSerializer.Serialize(payload));
        };
        return engine;
    }

    private static async Task<PendingPrompt> WaitForPrompt(GameEngine engine, string kind)
    {
        for (int i = 0; i < 200; i++)
        {
            if (engine.State.PendingPrompt is { } prompt && prompt.Kind == kind) return prompt;
            await Task.Delay(10);
        }
        throw new TimeoutException($"等待提示 {kind} 超时");
    }

    private static void Resolve(PendingPrompt prompt, GameEngine engine, params string[] choices)
        => engine.Prompts.Resolve(prompt.PromptId, choices);

    private static List<List<string?>> RevealBatches(ConcurrentQueue<string> messages)
    {
        var result = new List<List<string?>>();
        foreach (var message in messages)
        {
            using var document = JsonDocument.Parse(message);
            if (!document.RootElement.TryGetProperty("lastAction", out var action)
                || action.GetString() != "RevealCards") continue;
            result.Add(document.RootElement.GetProperty("reveal").GetProperty("cardNumbers")
                .EnumerateArray().Select(value => value.GetString()).ToList());
        }
        return result;
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "卡牌数据", "_effect-audit.v1.json"))
                && Directory.Exists(Path.Combine(dir.FullName, "服务端WebSocket")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("找不到 GrandUMI 仓库根目录");
    }

    private static HashSet<string> LoadPublicRevealCardNumbers(string cardDataDirectory)
    {
        using var audit = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(cardDataDirectory, "_effect-audit.v1.json")));
        using var content = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(cardDataDirectory, "_manifest.v1.json")));
        var root = audit.RootElement;
        Assert.Equal("grandumi.card-effect-audit.v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(
            content.RootElement.GetProperty("contentSha256").GetString(),
            root.GetProperty("cardContentSha256").GetString());
        return root.GetProperty("publicRevealCards")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, List<string>> LoadDefinitionNodes(string definitionDirectory)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(definitionDirectory, "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!result.TryGetValue(property.Name, out var nodes))
                    result[property.Name] = nodes = new List<string>();
                nodes.Add(property.Value.GetRawText());
            }
        }
        return result;
    }

    private static string? FindScriptedClassSegment(IEnumerable<string> sources, string number)
    {
        var cardPattern = new Regex($"CardNumber\\s*=>\\s*\"{Regex.Escape(number)}\"");
        var sharedCardPattern = new Regex($"protected\\s+override\\s+string\\s+Number\\s*=>\\s*\"{Regex.Escape(number)}\"");
        var classPattern = new Regex("(?m)^public\\s+(?:sealed\\s+)?class\\s+");
        foreach (var source in sources)
        {
            var cardMatch = cardPattern.Match(source);
            if (cardMatch.Success)
            {
                var starts = classPattern.Matches(source[..cardMatch.Index]);
                int start = starts.Count > 0 ? starts[^1].Index : 0;
                var next = classPattern.Match(source, cardMatch.Index + cardMatch.Length);
                int end = next.Success ? next.Index : source.Length;
                return source[start..end];
            }

            // OP17 使用单一分派器承载全部卡牌逻辑，注册类只有 Number；
            // 审计对应的 Cxxx 方法，避免把共享文件中其他卡牌的公开实现误算到本卡。
            if (!sharedCardPattern.IsMatch(source)) continue;
            string suffix = number[(number.IndexOf('-') + 1)..];
            var methodPattern = new Regex($"(?m)^\\s*private\\s+static\\s+(?:async\\s+)?(?:Task|void)\\s+C{Regex.Escape(suffix)}\\s*\\(");
            var methodMatch = methodPattern.Match(source);
            if (!methodMatch.Success) return null;
            var nextMethod = new Regex("(?m)^\\s*private\\s+static\\s+(?:async\\s+)?(?:Task|void)\\s+C\\d{3}\\s*\\(")
                .Match(source, methodMatch.Index + methodMatch.Length);
            return source[methodMatch.Index..(nextMethod.Success ? nextMethod.Index : source.Length)];
        }
        return null;
    }

    private static bool HasScriptedRevealRoute(string segment)
        => segment.Contains("BroadcastReveal(")
           || segment.Contains("SearchTop(")
           || segment.Contains("DiscardOwnFiltered(")
           || segment.Contains("DslInterpreter.TryResolve(")
           || segment.Contains("RevealTopPlayCost2(")
           || segment.Contains("LookTopPickAndBottom(")
           || segment.Contains("St13Brother.Run(");

    private static bool HasDefinitionRevealRoute(string node)
        => node.Contains("\"LookTopReveal\"")
           || node.Contains("\"RevealTopThen\"")
           || node.Contains("\"SearchDeck\"")
           || node.Contains("\"revealHand\"");
}
