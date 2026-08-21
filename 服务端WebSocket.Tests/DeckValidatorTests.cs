using GrandUMI.Cards;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class DeckValidatorTests
{
    public DeckValidatorTests()
    {
        TestScene.New(); // 触发 CardDatabase 加载
    }

    [Fact]
    public void OP15_50_LegalDeck_Passes()
    {
        var lines = new List<string> { "OP15-001" };
        // 凑 50 张：从 OP15 中挑 50 个能匹配领航颜色的角色卡
        var leader = CardDatabase.Get("OP15-001")!;
        var pool = CardDatabase.GetBySet("OP15")
            .Where(c => c.Kind != CardKind.Leader && c.SharesColorWith(leader))
            .ToList();
        var counts = new Dictionary<string, int>();
        int i = 0;
        while (lines.Count < 51)
        {
            var c = pool[i % pool.Count];
            var cur = counts.GetValueOrDefault(c.Number, 0);
            if (cur < 4) { lines.Add(c.Number); counts[c.Number] = cur + 1; }
            i++;
        }
        var v = DeckValidator.Validate(string.Join('\n', lines));
        Assert.True(v.Ok, v.Reason);
    }

    [Fact]
    public void DeckSize49_Fails()
    {
        var lines = new List<string> { "OP15-001" };
        var leader = CardDatabase.Get("OP15-001")!;
        var pool = CardDatabase.GetBySet("OP15")
            .Where(c => c.Kind != CardKind.Leader && c.SharesColorWith(leader)).ToList();
        var counts = new Dictionary<string, int>();
        int i = 0;
        while (lines.Count < 50)
        {
            var c = pool[i % pool.Count];
            var cur = counts.GetValueOrDefault(c.Number, 0);
            if (cur < 4) { lines.Add(c.Number); counts[c.Number] = cur + 1; }
            i++;
        }
        var v = DeckValidator.Validate(string.Join('\n', lines));
        Assert.False(v.Ok);
        Assert.Contains("50 张", v.Reason ?? "");
    }

    [Fact]
    public void NonOP15Leader_Fails()
    {
        var v = DeckValidator.Validate("ST01-001\n" + string.Join("\n", Enumerable.Repeat("OP15-003", 50)));
        Assert.False(v.Ok);
    }

    [Fact]
    public void NonOP15Card_Fails()
    {
        var lines = new List<string> { "OP15-001", "ST01-002" };
        for (int i = 0; i < 49; i++) lines.Add("OP15-003");
        var v = DeckValidator.Validate(string.Join('\n', lines));
        Assert.False(v.Ok);
    }

    [Fact]
    public void P117_RejectsCardsWithoutEastBlueTrait()
    {
        var lines = new List<string> { "P-117" };
        var eastBlueCards = new[]
        {
            "EB01-029", "EB01-030", "EB02-022", "EB02-029", "EB03-023",
            "EB03-028", "OP03-041", "OP03-042", "OP03-043", "OP03-044",
            "OP03-045", "OP03-046", "OP03-047",
        };
        foreach (var number in eastBlueCards)
        {
            while (lines.Count < 50 && lines.Count(card => card == number) < 4)
                lines.Add(number);
        }
        lines.Add("OP01-073");

        var result = DeckValidator.Validate(string.Join('\n', lines));

        Assert.False(result.Ok);
        Assert.Contains("东海", result.Reason ?? "");
        Assert.Contains("OP01-073", result.Reason ?? "");
    }

    [Fact]
    public void OP12001_拒绝费用五以上卡牌()
    {
        var leader = CardDatabase.Get("OP12-001")!;
        var lowCostPool = CardDatabase.GetBySet("OP12")
            .Where(card => card.Kind != CardKind.Leader && card.Cost < 5 && card.SharesColorWith(leader))
            .ToList();
        var lines = BuildValidDeck(leader, lowCostPool);
        var highCost = CardDatabase.GetBySet("OP12")
            .First(card => card.Kind != CardKind.Leader && card.Cost >= 5 && card.SharesColorWith(leader));
        lines[^1] = highCost.Number;

        var result = DeckValidator.Validate(string.Join('\n', lines));

        Assert.False(result.Ok);
        Assert.Contains("费用为 5 或更高", result.Reason ?? "");
        Assert.Contains(highCost.Number, result.Reason ?? "");
    }

    [Fact]
    public void 标准排位拒绝角标一卡_狂野排位允许同一卡组()
    {
        var leader = CardDatabase.Get("OP01-001")!;
        Assert.Equal(1, leader.Subscript);
        var lines = BuildValidDeck(leader, CardDatabase.GetBySet("OP01"));

        var standard = DeckValidator.Validate(string.Join('\n', lines), DeckValidator.FormatStandard);
        var wild = DeckValidator.Validate(string.Join('\n', lines), DeckValidator.FormatUnrestricted);

        Assert.False(standard.Ok);
        Assert.Contains("标准排位不能使用禁限领航卡", standard.Reason ?? "");
        Assert.True(wild.Ok, wild.Reason);
    }

    [Fact]
    public void 标准排位允许当前环境卡组()
    {
        var leader = CardDatabase.Get("OP15-001")!;
        var lines = BuildValidDeck(leader, CardDatabase.GetBySet("OP15"));

        var standard = DeckValidator.Validate(string.Join('\n', lines), DeckValidator.FormatStandard);

        Assert.True(standard.Ok, standard.Reason);
    }

    private static List<string> BuildValidDeck(CardInfo leader, IReadOnlyList<CardInfo> pool)
    {
        var lines = new List<string> { leader.Number };
        foreach (var card in pool.Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader)))
        {
            for (var copy = 0; copy < 4 && lines.Count < 51; copy++) lines.Add(card.Number);
            if (lines.Count == 51) break;
        }
        Assert.Equal(51, lines.Count);
        return lines;
    }
}
