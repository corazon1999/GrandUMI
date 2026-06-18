namespace GrandUMI.Game;

/// <summary>
/// 确定性卡实例 ID 生成。
///
/// 背景：对局重启恢复采用"重放重建"——用同一 RngSeed + 同一动作磁带重新执行对局。
/// 若 CardInstance.Id 走 Guid.NewGuid()，重放产生的 ID 与原局不同，动作 data 里引用的旧
/// GUID 就对不上。故让每个引擎在自身生命周期内用"由 seed 派生的单调序列"生成 ID：
/// 同 seed + 同执行序 → 同一串 GUID，重放后动作 data 原样可用。
///
/// 通过 AsyncLocal 暴露"当前引擎的 ID 工厂"，使其能随 async/await 续延流动（效果解析大量
/// 即发即忘 async，会在线程池任意线程恢复，ThreadStatic 不可靠）。未设置工厂时回退
/// Guid.NewGuid()（如单元测试里直接 new CardInstance 的旧场景）。
/// </summary>
public static class DeterministicId
{
    private static readonly AsyncLocal<Func<Guid>?> _current = new();

    /// <summary>当前执行上下文使用的 ID 工厂；为 null 时回退随机 GUID。由 GameEngine 设置。</summary>
    public static Func<Guid>? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <summary>生成下一个卡实例 ID（有工厂走工厂，否则随机）。</summary>
    public static Guid Next() => Current?.Invoke() ?? Guid.NewGuid();

    /// <summary>
    /// 由 int seed 派生一个"确定性 GUID 序列"工厂。返回的委托闭包持有单调计数器，
    /// 每次调用产出 seed + 序号 拼成的 16 字节 GUID。
    /// 同一 seed 的两个工厂，在相同调用次序下产出完全相同的 GUID 串。
    /// 注意：工厂实例自身有状态（计数器），一局应共用同一个实例。
    /// </summary>
    public static Func<Guid> SeededFactory(int seed)
    {
        long counter = 0;
        return () =>
        {
            long c = System.Threading.Interlocked.Increment(ref counter);
            Span<byte> b = stackalloc byte[16];
            BitConverter.TryWriteBytes(b.Slice(0, 4), seed);
            BitConverter.TryWriteBytes(b.Slice(4, 8), c);
            BitConverter.TryWriteBytes(b.Slice(12, 4), seed ^ unchecked((int)c));
            return new Guid(b);
        };
    }
}
