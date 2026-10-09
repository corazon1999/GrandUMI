namespace GrandUMI.Persistence;

public sealed partial class PlayerDataStore
{
    /// <summary>按公开昵称批量解析身份；不把登录账号返回给客户端，重名旧数据拒绝猜测归属。</summary>
    public IReadOnlyDictionary<string, string> ResolvePublicPlayerAccounts(IReadOnlyList<string> names)
    {
        if (names.Count > 40 || names.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > MaxDisplayNameLength))
            throw new PlayerDataValidationException("每次最多查询 40 个有效玩家昵称。");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT account FROM players WHERE display_name=$name COLLATE NOCASE LIMIT 2;";
        var parameter = command.Parameters.AddWithValue("$name", "");
        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            parameter.Value = name;
            using var reader = command.ExecuteReader();
            if (!reader.Read()) continue;
            var account = reader.GetString(0);
            if (!reader.Read()) result[name] = account;
        }
        return result;
    }
}
