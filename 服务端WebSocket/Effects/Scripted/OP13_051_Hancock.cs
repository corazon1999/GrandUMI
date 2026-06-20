using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP13-051 波尔·汉库珂（角色）
/// 【KO时】我方领袖为“波尔·汉库珂”或多种颜色的场合，抽取 2 张卡牌。
///
/// 说明：DSL 的 if 节为 AND 组合且没有“领袖多种颜色”条件，故用脚本实现“或”分支。
/// “多种颜色” = 领袖的颜色数 ≥ 2。
/// </summary>
public class OP13_051_Hancock : IScriptedEffect
{
    public string CardNumber => "OP13-051";

    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.OnKO;

    public Task Resolve(EffectContext ctx)
    {
        var me = ctx.State.Players[ctx.OwnerIndex];
        bool leaderIsHancock = me.Leader.Info.NameIs("波尔·汉库珂");
        bool leaderMultiColor = me.Leader.Info.ColorList.Length >= 2;
        if (leaderIsHancock || leaderMultiColor)
            AtomicOps.Draw(ctx.State, ctx.OwnerIndex, 2);
        return Task.CompletedTask;
    }
}
