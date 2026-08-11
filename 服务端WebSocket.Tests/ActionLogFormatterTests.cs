using System.Text.Json;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class ActionLogFormatterTests
{
    [Fact]
    public void 出牌日志_包含卡号与卡名()
    {
        var state = TestScene.New().Build();
        var payload = JsonSerializer.SerializeToElement(new { player = 0, cardNumber = "OP16-021" });

        var text = ActionLogFormatter.Format(state, 0, "PlayCard", payload);

        Assert.StartsWith("[出牌]", text);
        Assert.Contains("OP16-021", text);
        Assert.Contains("莫比", text);
    }

    [Fact]
    public void 攻击日志_显示宣言时双方当前力量和卡名()
    {
        var state = TestScene.New(oppLeaderNumber: "ST01-001").MyCharacter("OP05-007").Build();
        var attacker = state.Players[0].Characters.Single();
        attacker.PowerModThisTurn = 1000 - attacker.Info.Power;
        var payload = JsonSerializer.SerializeToElement(new
        {
            attacker = attacker.Id.ToString(),
            targetIsLeader = true,
            targetId = (string?)null,
        });

        var self = ActionLogFormatter.Format(state, 0, "Attack", payload);
        var spectator = ActionLogFormatter.Format(state, -1, "Attack", payload);

        Assert.Equal("[攻击] 我方【萨波】1000 vs 对手【蒙奇·D·路飞】5000", self);
        Assert.Equal("[攻击] 玩家1【萨波】1000 vs 玩家2【蒙奇·D·路飞】5000", spectator);
    }

    [Fact]
    public void 效果选择日志_公开目标双方均可看到详情()
    {
        var state = TestScene.New().Build();
        var payload = JsonSerializer.SerializeToElement(new
        {
            player = 0,
            sourceNumber = "OP16-021",
            text = "选择1张角色",
            labels = new[] { "OP16-010 耐休尔" },
            detailVisibility = "public",
        });

        var self = ActionLogFormatter.Format(state, 0, "PromptResolved", payload);
        var opponent = ActionLogFormatter.Format(state, 1, "PromptResolved", payload);

        Assert.Contains("OP16-010 耐休尔", self);
        Assert.Contains("OP16-010 耐休尔", opponent);
    }

    [Fact]
    public void 效果选择日志_隐藏区详情只向选择方显示()
    {
        var state = TestScene.New().Build();
        var payload = JsonSerializer.SerializeToElement(new
        {
            player = 0,
            sourceNumber = "OP16-021",
            text = "从卡组选择1张牌",
            labels = new[] { "OP16-010 耐休尔" },
            detailVisibility = "restricted",
            detailViewers = new[] { 0 },
        });

        var self = ActionLogFormatter.Format(state, 0, "PromptResolved", payload);
        var opponent = ActionLogFormatter.Format(state, 1, "PromptResolved", payload);
        var spectator = ActionLogFormatter.Format(state, -1, "PromptResolved", payload);

        Assert.Contains("OP16-010 耐休尔", self);
        Assert.DoesNotContain("OP16-010", opponent);
        Assert.DoesNotContain("OP16-010", spectator);
        Assert.Contains("非公开选择", opponent);
    }

    [Fact]
    public void 公开日志_列出全部公开卡牌()
    {
        var state = TestScene.New().Build();
        var payload = JsonSerializer.SerializeToElement(new
        {
            player = 0,
            cardNumbers = new[] { "OP16-010", "OP16-021" },
        });

        var text = ActionLogFormatter.Format(state, 1, "RevealCards", payload);

        Assert.StartsWith("[公开]", text);
        Assert.Contains("OP16-010", text);
        Assert.Contains("OP16-021", text);
    }

    [Fact]
    public void 快照_可在同一Tick携带多条日志()
    {
        var state = TestScene.New().Build();
        var queued = new[]
        {
            new ActionLogEvent("PromptResolved", new
            {
                player = 0,
                sourceNumber = "OP16-021",
                text = "是否发动效果",
                labels = new[] { "是" },
                detailVisibility = "public",
            }),
            new ActionLogEvent("RevealCards", new
            {
                player = 0,
                cardNumbers = new[] { "OP16-010" },
            }),
        };

        var snapshot = StateSnapshotBuilder.Build(state, 0, "EffectChoice", null, queued);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));
        var lines = json.RootElement.GetProperty("logLines").EnumerateArray().Select(x => x.GetString()).ToArray();

        Assert.Equal(2, lines.Length);
        Assert.StartsWith("[效果选择]", lines[0]);
        Assert.StartsWith("[公开]", lines[1]);
    }
}
