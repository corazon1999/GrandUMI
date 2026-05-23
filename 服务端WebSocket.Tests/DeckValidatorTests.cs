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
}
