using GrandUMI.Game;

namespace GrandUMI.Effects;

/// <summary>
/// 效果脚本通过该接口与玩家交互（选择目标、选项、是否发动可选效果等）
/// 服务端实现：PromptSystem
/// </summary>
public interface IPromptService
{
    /// <summary>
    /// 让 playerIdx 玩家从 validChoices（卡 GUID 字符串列表）中选 min..max 张
    /// 返回玩家选择的 ID 列表；超时或不选则返回空列表
    /// </summary>
    Task<List<string>> ChooseCards(int playerIdx, string kind, string text,
        IReadOnlyList<string> validChoices, int min, int max,
        Dictionary<string, object?>? extra = null);

    /// <summary>是否发动可选效果（"可以…：…"）</summary>
    Task<bool> ConfirmOptional(int playerIdx, string text);

    /// <summary>从多个选项中选 1 个（"选择以下的 1 项"）</summary>
    Task<int> ChooseOption(int playerIdx, string text, IReadOnlyList<string> options);

    /// <summary>
    /// 生命牌反信息泄露 Prompt：让 playerIdx 决定"发动触发 / 加入手牌"
    /// hasRealTrigger=true 时玩家选发动会真正执行；=false 则只是占位等待
    /// 返回 true = 发动触发，false = 加入手牌
    /// </summary>
    Task<bool> AskLifeTrigger(int playerIdx, CardInstance lifeCard, bool hasRealTrigger);
}
