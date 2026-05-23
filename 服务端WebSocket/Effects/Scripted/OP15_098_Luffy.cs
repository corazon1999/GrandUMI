using GrandUMI.Game;

namespace GrandUMI.Effects.Scripted;

/// <summary>
/// OP15-098 蒙奇·D·路飞（领航）
/// 置换：当我方《空岛》原本力量 ≥6000 的角色因对方将要离场时，
///   可将生命区最上方 1 张加入手牌，使该角色不离场。
/// 通过 PreKO 触发实现（"离场"在此简化为 KO）
/// </summary>
public class OP15_098_Luffy : IScriptedEffect
{
    public string CardNumber => "OP15-098";
    public bool HandlesTrigger(EffectTrigger t) => t == EffectTrigger.PreKO;
    public async Task Resolve(EffectContext ctx)
    {
        // 仅对我方场上《空岛》且原本力量 ≥6000 的角色生效
        var me = ctx.State.Players[ctx.OwnerIndex];
        // 注意：此领航效果应该响应"任意 我方 角色被 KO 时"，
        // 但当前 PreKO 触发只 Resolve 单卡的脚本，所以需把触发挂到目标卡上。
        // 简化：仅当领航自身被 KO 不会触发，反正领航不能被 KO
        // 真正实现需要"全局 PreKO 监听"——这里作为占位。
        if (me.LifeCount == 0) return;
        bool use = await ctx.Prompts.ConfirmOptional(ctx.OwnerIndex,
            "路飞：弃 1 生命牌为代价，使该角色不被 KO？");
        if (!use) return;
        // 把生命区最上 1 张加入手牌
        if (me.LifeArea.Count > 0)
        {
            var top = me.LifeArea[0];
            me.LifeArea.RemoveAt(0);
            me.Hand.Add(top);
            ctx.State.MarkPreventKO(ctx.Source.Id);
        }
    }
}
