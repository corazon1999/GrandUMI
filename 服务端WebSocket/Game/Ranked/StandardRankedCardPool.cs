namespace GrandUMI.Game.Ranked;

internal enum StandardRankedCardPool
{
    Real,
    Preview,
    All,
}

internal static class StandardRankedCardPoolPolicy
{
    internal static bool TryResolve(string? requested, bool usesPreviewCards,
        out StandardRankedCardPool pool, out string? error)
    {
        error = null;
        // 旧客户端未传选择时，保留按卡组自动分池的行为。
        if (requested is null)
        {
            pool = usesPreviewCards ? StandardRankedCardPool.Preview : StandardRankedCardPool.Real;
            return true;
        }
        pool = requested.Trim().ToLowerInvariant() switch
        {
            "real" => StandardRankedCardPool.Real,
            "preview" => StandardRankedCardPool.Preview,
            "all" => StandardRankedCardPool.All,
            _ => (StandardRankedCardPool)(-1),
        };
        if (!Enum.IsDefined(pool))
        {
            error = "卡池选择无效，请重新选择匹配环境。";
            return false;
        }
        if (pool == StandardRankedCardPool.Real && usesPreviewCards)
        {
            error = "实卡环境不能使用 OP18、ST37、ST38，请更换卡组或选择新卡环境、全环境。";
            return false;
        }
        return true;
    }

    internal static int AllowedEnvironments(StandardRankedCardPool? selected, bool usesPreviewCards)
    {
        var pool = selected ?? (usesPreviewCards ? StandardRankedCardPool.Preview : StandardRankedCardPool.Real);
        // 两个真实环境分别占一位；全环境取交集时仍须遵守对手的实卡限制。
        return pool switch
        {
            StandardRankedCardPool.Real => usesPreviewCards ? 0 : 1,
            StandardRankedCardPool.Preview => 2,
            StandardRankedCardPool.All => usesPreviewCards ? 2 : 3,
            _ => 0,
        };
    }

    internal static string ToProtocol(StandardRankedCardPool pool) => pool switch
    {
        StandardRankedCardPool.Real => "real",
        StandardRankedCardPool.Preview => "preview",
        StandardRankedCardPool.All => "all",
        _ => throw new ArgumentOutOfRangeException(nameof(pool)),
    };
}
