using GrandUMI.Effects;
using GrandUMI.Game;

namespace GrandUMI.Tests;

/// <summary>
/// 测试用 Mock：按 FIFO 队列回放预设答案
///
/// 用法：
///   var p = new MockPromptService();
///   p.QueueChoose("cardId-A");     // ChooseCards 返回 ["cardId-A"]
///   p.QueueConfirm(true);          // ConfirmOptional 返回 true
///   p.QueueOption(0);              // ChooseOption 返回 0
///   p.QueueLifeTrigger(false);     // AskLifeTrigger 返回 false
///
/// 若队列为空：ChooseCards 默认全选；ConfirmOptional 默认 true；ChooseOption 默认 0；
///            AskLifeTrigger 默认 false（视为放弃触发）
/// </summary>
public class MockPromptService : IPromptService
{
    private readonly Queue<List<string>> _chooseAnswers = new();
    private readonly Queue<bool>          _confirmAnswers = new();
    private readonly Queue<int>           _optionAnswers = new();
    private readonly Queue<bool>          _lifeTriggerAnswers = new();

    public List<(string kind, string text, IReadOnlyList<string> choices, int min, int max)> ChooseHistory { get; } = new();
    public List<string> ConfirmHistory { get; } = new();

    public MockPromptService QueueChoose(params string[] ids)
    {
        _chooseAnswers.Enqueue(ids.ToList());
        return this;
    }
    public MockPromptService QueueChooseEmpty()
    {
        _chooseAnswers.Enqueue(new List<string>());
        return this;
    }
    public MockPromptService QueueConfirm(bool yes)
    {
        _confirmAnswers.Enqueue(yes);
        return this;
    }
    public MockPromptService QueueOption(int idx)
    {
        _optionAnswers.Enqueue(idx);
        return this;
    }
    public MockPromptService QueueLifeTrigger(bool useTrigger)
    {
        _lifeTriggerAnswers.Enqueue(useTrigger);
        return this;
    }

    public Task<List<string>> ChooseCards(int playerIdx, string kind, string text,
        IReadOnlyList<string> validChoices, int min, int max,
        Dictionary<string, object?>? extra = null)
    {
        ChooseHistory.Add((kind, text, validChoices, min, max));
        if (_chooseAnswers.Count > 0)
        {
            var ans = _chooseAnswers.Dequeue();
            // 校验答案在 validChoices 中
            var filtered = ans.Where(validChoices.Contains).ToList();
            return Task.FromResult(filtered);
        }
        // 默认：max <= validChoices.Count 时返回前 max 个，否则全选
        int take = Math.Min(max, validChoices.Count);
        return Task.FromResult(validChoices.Take(take).ToList());
    }

    public Task<bool> ConfirmOptional(int playerIdx, string text)
    {
        ConfirmHistory.Add(text);
        return Task.FromResult(_confirmAnswers.Count > 0 ? _confirmAnswers.Dequeue() : true);
    }

    public Task<int> ChooseOption(int playerIdx, string text, IReadOnlyList<string> options)
    {
        return Task.FromResult(_optionAnswers.Count > 0 ? _optionAnswers.Dequeue() : 0);
    }

    public Task<bool> AskLifeTrigger(int playerIdx, CardInstance lifeCard, bool hasRealTrigger)
    {
        return Task.FromResult(_lifeTriggerAnswers.Count > 0 ? _lifeTriggerAnswers.Dequeue() : false);
    }
}
