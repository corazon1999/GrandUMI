using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// 覆盖“响应一个效果选择后，续程立即触发满场挤位选择”的真实并发路径。
/// 默认 TaskCompletionSource 会把续程内联到持有房间锁的 PromptResponse 中，
/// 导致第二个 PromptResponse 无法取得房间锁，只能等待 30 秒超时。
/// </summary>
public class PromptContinuationTests
{
    private static JsonElement Json(object value)
        => JsonSerializer.SerializeToElement(value);

    private static async Task<PendingPrompt> WaitForPrompt(
        GameEngine engine,
        Func<PendingPrompt, bool> predicate,
        int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (engine.State.PendingPrompt is { } prompt && predicate(prompt)) return prompt;
            await Task.Delay(10);
        }

        throw new TimeoutException("等待测试 Prompt 超时");
    }

    private static string LegalOp17Deck()
    {
        // 借 TestScene 完成测试卡库加载，再按真实卡池生成合法的 50 张主卡组。
        _ = TestScene.New("OP17-039");
        var leader = CardDatabase.Get("OP17-039")!;
        var pool = CardDatabase.GetBySet("OP17")
            .Where(c => c.Kind != CardKind.Leader && c.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leader.Number };
        var counts = new Dictionary<string, int>();
        var index = 0;
        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            if (counts.GetValueOrDefault(card.Number) >= 4) continue;
            counts[card.Number] = counts.GetValueOrDefault(card.Number) + 1;
            lines.Add(card.Number);
        }
        return string.Join('\n', lines);
    }

    [Fact]
    public async Task PromptResponse_ThenOverflowPrompt_ShouldNotWaitForTimeout()
    {
        var deck = LegalOp17Deck();
        var engine = new GameEngine(
            "prompt-continuation",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: 20260806);

        var me = engine.State.Players[0];
        me.Hand.Clear();
        me.Deck.Clear();
        me.Characters.Clear();

        // 场上保持满员，其中 10 费洛克斯作为当前效果源；手牌只留一张可被其效果登场的角色。
        for (var i = 0; i < 4; i++)
            me.Characters.Add(new CardInstance { Info = CardDatabase.Get("OP17-045")! });
        var rocks = new CardInstance { Info = CardDatabase.Get("OP17-118")! };
        me.Characters.Add(rocks);
        var entering = new CardInstance { Info = CardDatabase.Get("OP17-040")! };
        me.Hand.Add(entering);

        // 洛克斯自身及被登场角色都会抽牌；事件卡不会成为“登场角色”的后续候选。
        me.Deck.Add(new CardInstance { Info = CardDatabase.Get("OP17-055")! });
        me.Deck.Add(new CardInstance { Info = CardDatabase.Get("OP17-055")! });

        var resolveTask = EffectRuntime.Resolve(
            engine.State,
            0,
            rocks,
            EffectTrigger.OnEnterField,
            engine.Prompts);

        var summonPrompt = await WaitForPrompt(engine, p => p.Kind == "ChooseByTotalCost");

        // 模拟 GameRoomManager：PromptResponse 在房间锁内进入引擎。
        var firstResponse = Task.Run(() =>
        {
            lock (engine)
            {
                engine.HandleAction(0, "PromptResponse", Json(new
                {
                    promptId = summonPrompt.PromptId,
                    chosen = new[] { entering.Id.ToString() },
                }));
            }
        });

        var overflowPrompt = await WaitForPrompt(
            engine,
            p => p.Kind == "OverflowTrash" && p.PromptId != summonPrompt.PromptId);
        var chosenVictim = me.Characters[2];

        var secondResponse = Task.Run(() =>
        {
            lock (engine)
            {
                engine.HandleAction(0, "PromptResponse", Json(new
                {
                    promptId = overflowPrompt.PromptId,
                    chosen = new[] { chosenVictim.Id.ToString() },
                }));
            }
        });

        var completed = await Task.WhenAny(resolveTask, Task.Delay(3000));
        Assert.Same(resolveTask, completed);
        await resolveTask;
        await Task.WhenAll(firstResponse, secondResponse);

        Assert.Contains(chosenVictim, me.Trash);
        Assert.Contains(entering, me.Characters);
        Assert.DoesNotContain(chosenVictim, me.Characters);
        Assert.Equal(5, me.Characters.Count);
        Assert.Null(engine.State.PendingPrompt);
    }
}
