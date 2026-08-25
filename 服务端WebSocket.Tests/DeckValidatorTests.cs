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
        Assert.Contains("标准模式不能使用禁限领航卡", standard.Reason ?? "");
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

    [Theory]
    [InlineData("OP03-040")]
    [InlineData("ST10-001")]
    public void 标准排位拒绝官网完全禁用领航_狂野排位允许(string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var lines = BuildValidDeck(leader, CardDatabase.GetBySet("OP15"));

        var standard = DeckValidator.Validate(string.Join('\n', lines), DeckValidator.FormatStandard);
        var wild = DeckValidator.Validate(string.Join('\n', lines), DeckValidator.FormatUnrestricted);

        Assert.False(standard.Ok);
        Assert.Contains("官方禁卡", standard.Reason ?? "");
        Assert.Contains(leaderNumber, standard.Reason ?? "");
        Assert.True(wild.Ok, wild.Reason);
    }

    [Theory]
    [InlineData("OP06-047", "OP15-039")]
    [InlineData("OP06-086", "OP16-079")]
    [InlineData("OP06-116", "OP15-098")]
    public void 标准排位拒绝官网完全禁用主卡组卡牌_狂野排位允许(string bannedCardNumber, string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var lines = BuildValidDeck(leader, CardDatabase.GetBySet("OP15"));
        lines[^1] = bannedCardNumber;

        var standard = DeckValidator.Validate(string.Join('\n', lines), DeckValidator.FormatStandard);
        var wild = DeckValidator.Validate(string.Join('\n', lines), DeckValidator.FormatUnrestricted);

        Assert.False(standard.Ok);
        Assert.Contains("官方禁卡", standard.Reason ?? "");
        Assert.Contains(bannedCardNumber, standard.Reason ?? "");
        Assert.True(wild.Ok, wild.Reason);
    }

    [Theory]
    [InlineData("OP11-040", "OP11-067", "OP11-040")]
    [InlineData("OP11-040", "OP08-069", "OP11-040")]
    [InlineData("OP07-115", "EB04-058", "OP15-098")]
    public void 标准排位拒绝官网禁用组合_狂野排位允许(string cardA, string cardB, string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var lines = BuildValidDeck(leader, CardDatabase.GetBySet("OP15"));
        if (!string.Equals(leaderNumber, cardA, StringComparison.OrdinalIgnoreCase))
            lines[^2] = cardA;
        lines[^1] = cardB;

        var standard = DeckValidator.Validate(string.Join('\n', lines), DeckValidator.FormatStandard);
        var wild = DeckValidator.Validate(string.Join('\n', lines), DeckValidator.FormatUnrestricted);

        Assert.False(standard.Ok);
        Assert.Contains("官方禁用组合", standard.Reason ?? "");
        Assert.Contains(cardA, standard.Reason ?? "");
        Assert.Contains(cardB, standard.Reason ?? "");
        Assert.True(wild.Ok, wild.Reason);
    }

    [Theory]
    [InlineData("OP15-058", "OP11-067")]
    [InlineData("OP15-058", "OP08-069")]
    [InlineData("OP15-098", "OP07-115")]
    [InlineData("OP15-098", "EB04-058")]
    public void 标准排位允许禁用组合中的卡牌单独使用(string leaderNumber, string singleCardNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var lines = BuildValidDeck(leader, CardDatabase.GetBySet("OP15"));
        lines[^1] = singleCardNumber;

        var standard = DeckValidator.Validate(string.Join('\n', lines), DeckValidator.FormatStandard);

        Assert.True(standard.Ok, standard.Reason);
    }

    [Fact]
    public void 标准排位允许禁用组合领航单独使用()
    {
        var leader = CardDatabase.Get("OP11-040")!;
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
