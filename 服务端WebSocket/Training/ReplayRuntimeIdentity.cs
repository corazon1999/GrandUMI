using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GrandUMI.Effects;
using GrandUMI.Effects.Rules;

namespace GrandUMI.Training;

/// <summary>启动时冻结、供所有新对局复用的发布工件身份。</summary>
public sealed record ReplayRuntimeBuildIdentity(
    string EngineCommit,
    string BinarySha256,
    string CardDbContentHash);

/// <summary>一局钉住的完整运行身份；任一内容变化都会生成不同的 engineArtifactId。</summary>
public sealed record ReplayRuntimeIdentity(
    string MatchLogSchema,
    string EventAdapterVersion,
    string EngineArtifactId,
    string EngineCommit,
    string BinarySha256,
    string RulesVersion,
    string RulesetManifestHash,
    string CardDbContentHash,
    string RngAlgorithmVersion,
    string DeterministicIdVersion,
    string OpeningProtocolVersion,
    string ReplayConfigSchema,
    string ManifestHash);

/// <summary>match_start 中冻结的玩家输入；账号只属于原始敏感日志，不进入身份哈希。</summary>
public sealed record ReplayMatchStartPlayer(
    int Index,
    string AccountName,
    string DeckRaw,
    bool AlwaysPromptOnLifeReveal);

/// <summary>由唯一工厂创建的 match_start payload，避免各入口自行拼接版本字段。</summary>
public sealed record ReplayMatchStartPayload(
    IReadOnlyList<ReplayMatchStartPlayer> Players,
    int FirstPlayer,
    int StartingPlayerChooser,
    IReadOnlyList<object> StartingDiceRolls,
    int RngSeed,
    bool OpeningSetupAfterFirstPlayerChoice,
    string MatchKind,
    string MatchLogSchema,
    string EventAdapterVersion,
    string EngineArtifactId,
    string EngineCommit,
    string BinarySha256,
    string RulesVersion,
    string RulesetManifestHash,
    string CardDbContentHash,
    string RngAlgorithmVersion,
    string DeterministicIdVersion,
    string OpeningProtocolVersion,
    string ReplayConfigSchema,
    string ReplayRuntimeManifestHash,
    object ReplayConfig);

public static class ReplayRuntimeIdentityFactory
{
    public const string DeterministicIdVersion = "grandumi.seed-counter-guid.le.v1";
    public const string OpeningProtocolVersion = "grandumi.opening.effects-before-dice.deferred-setup.v1";
    public const string ReplayConfigSchema = "grandumi.replay-config.v1";

    private static readonly Regex CommitPattern = new(
        "^(?:[0-9a-f]{40}|[0-9a-f]{64})$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new(
        "^sha256:[0-9a-f]{64}$",
        RegexOptions.CultureInvariant);
    private static readonly ConcurrentDictionary<string, string> AssemblyHashes = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public static ReplayRuntimeIdentity Create(
        ReplayRuntimeBuildIdentity build,
        CardRuleset ruleset,
        Version? runtimeVersion = null)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(ruleset);
        ValidateBuild(build);
        RequireSha256(ruleset.ManifestHash, nameof(ruleset.ManifestHash));
        if (!BitConverter.IsLittleEndian)
            throw new InvalidOperationException("当前确定性实例 ID 协议只支持小端运行时，拒绝生成不可重放身份。");

        var runtime = runtimeVersion ?? Environment.Version;
        var rngAlgorithmVersion = $"dotnet-system-random-seeded-{runtime}.v1";
        var manifestHash = ComputeManifestHash(
            MatchLogEventAdapter.SupportedSchema,
            MatchLogEventAdapter.CurrentAdapterVersion,
            build.EngineCommit,
            build.BinarySha256,
            ruleset.Id,
            ruleset.ManifestHash,
            build.CardDbContentHash,
            rngAlgorithmVersion,
            DeterministicIdVersion,
            OpeningProtocolVersion,
            ReplayConfigSchema);
        var artifactId = "grandumi-runtime-" + manifestHash["sha256:".Length..];
        return new ReplayRuntimeIdentity(
            MatchLogEventAdapter.SupportedSchema,
            MatchLogEventAdapter.CurrentAdapterVersion,
            artifactId,
            build.EngineCommit,
            build.BinarySha256,
            ruleset.Id,
            ruleset.ManifestHash,
            build.CardDbContentHash,
            rngAlgorithmVersion,
            DeterministicIdVersion,
            OpeningProtocolVersion,
            ReplayConfigSchema,
            manifestHash);
    }

    /// <summary>验证一份外部归档声明的运行身份仍满足当前冻结算法。</summary>
    internal static void ValidateIdentity(ReplayRuntimeIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ValidateBuild(new ReplayRuntimeBuildIdentity(
            identity.EngineCommit,
            identity.BinarySha256,
            identity.CardDbContentHash));
        RequireSha256(identity.RulesetManifestHash, nameof(identity.RulesetManifestHash));
        RequireSha256(identity.ManifestHash, nameof(identity.ManifestHash));

        var fields = new[]
        {
            identity.MatchLogSchema,
            identity.EventAdapterVersion,
            identity.EngineArtifactId,
            identity.RulesVersion,
            identity.RngAlgorithmVersion,
            identity.DeterministicIdVersion,
            identity.OpeningProtocolVersion,
            identity.ReplayConfigSchema,
        };
        if (fields.Any(string.IsNullOrWhiteSpace)
            || fields.Any(value => !string.Equals(value, value.Trim(), StringComparison.Ordinal)))
            throw new InvalidOperationException("运行身份包含空字符串或首尾空白。拒绝接受归档。");

        var expectedManifestHash = ComputeManifestHash(
            identity.MatchLogSchema,
            identity.EventAdapterVersion,
            identity.EngineCommit,
            identity.BinarySha256,
            identity.RulesVersion,
            identity.RulesetManifestHash,
            identity.CardDbContentHash,
            identity.RngAlgorithmVersion,
            identity.DeterministicIdVersion,
            identity.OpeningProtocolVersion,
            identity.ReplayConfigSchema);
        if (!string.Equals(expectedManifestHash, identity.ManifestHash, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"运行身份 manifestHash 不一致：声明 {identity.ManifestHash}，计算 {expectedManifestHash}");

        var expectedArtifactId = "grandumi-runtime-" + expectedManifestHash["sha256:".Length..];
        if (!string.Equals(expectedArtifactId, identity.EngineArtifactId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"运行身份 engineArtifactId 不一致：声明 {identity.EngineArtifactId}，计算 {expectedArtifactId}");
    }

    private static string ComputeManifestHash(
        string matchLogSchema,
        string eventAdapterVersion,
        string engineCommit,
        string binarySha256,
        string rulesVersion,
        string rulesetManifestHash,
        string cardDbContentHash,
        string rngAlgorithmVersion,
        string deterministicIdVersion,
        string openingProtocolVersion,
        string replayConfigSchema)
    {
        var manifest = JsonSerializer.SerializeToElement(new
        {
            matchLogSchema,
            eventAdapterVersion,
            engineCommit,
            binarySha256,
            rulesVersion,
            rulesetManifestHash,
            cardDbContentHash,
            rngAlgorithmVersion,
            deterministicIdVersion,
            openingProtocolVersion,
            replayConfigSchema,
        });
        return CanonicalJson.Hash(manifest);
    }

    public static ReplayMatchStartPayload CreateMatchStartPayload(
        ReplayRuntimeIdentity identity,
        IReadOnlyList<ReplayMatchStartPlayer> players,
        int firstPlayer,
        int startingPlayerChooser,
        IReadOnlyList<object> startingDiceRolls,
        int rngSeed,
        bool openingSetupAfterFirstPlayerChoice,
        string matchKind,
        bool leaderKeywordWildcard)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(startingDiceRolls);
        if (players.Count != 2 || players.Select(player => player.Index).Order().SequenceEqual([0, 1]) is false)
            throw new InvalidOperationException("match_start 必须唯一包含 0、1 两个席位。");

        return new ReplayMatchStartPayload(
            players.ToArray(),
            firstPlayer,
            startingPlayerChooser,
            startingDiceRolls.ToArray(),
            rngSeed,
            openingSetupAfterFirstPlayerChoice,
            matchKind,
            identity.MatchLogSchema,
            identity.EventAdapterVersion,
            identity.EngineArtifactId,
            identity.EngineCommit,
            identity.BinarySha256,
            identity.RulesVersion,
            identity.RulesetManifestHash,
            identity.CardDbContentHash,
            identity.RngAlgorithmVersion,
            identity.DeterministicIdVersion,
            identity.OpeningProtocolVersion,
            identity.ReplayConfigSchema,
            identity.ManifestHash,
            new
            {
                leaderKeywordWildcard,
            });
    }

    internal static void ValidateBuild(ReplayRuntimeBuildIdentity build)
    {
        if (!CommitPattern.IsMatch(build.EngineCommit))
            throw new InvalidOperationException("engineCommit 必须是完整的小写 Git 对象 ID，拒绝生成运行身份。");
        RequireSha256(build.BinarySha256, nameof(build.BinarySha256));
        RequireSha256(build.CardDbContentHash, nameof(build.CardDbContentHash));
    }

    internal static string ComputeRulesetManifestHash(
        string id,
        string? baseRulesetId,
        string description,
        IReadOnlyDictionary<string, IScriptedEffect> scriptedEffects,
        IReadOnlyDictionary<string, JsonElement> dslDefinitions,
        IReadOnlyCollection<string> changedCards,
        string? sourcePackageHash)
    {
        var scripts = scriptedEffects
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                var type = pair.Value.GetType();
                var assemblyPath = type.Assembly.Location;
                if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
                    throw new InvalidOperationException($"规则实现程序集不可读：{type.FullName}");
                var assemblyHash = AssemblyHashes.GetOrAdd(
                    Path.GetFullPath(assemblyPath),
                    ReplayContentManifest.HashFile);
                return new
                {
                    cardNumber = pair.Key,
                    implementationType = type.FullName ?? type.Name,
                    assemblyHash,
                };
            })
            .ToArray();
        var definitions = dslDefinitions
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new
            {
                cardNumber = pair.Key,
                definition = pair.Value,
            })
            .ToArray();
        var manifest = JsonSerializer.SerializeToElement(new
        {
            schema = "grandumi.ruleset-manifest.v1",
            id,
            baseRulesetId,
            description,
            changedCards = changedCards.Order(StringComparer.Ordinal).ToArray(),
            sourcePackageHash,
            scripts,
            definitions,
        });
        return CanonicalJson.Hash(manifest);
    }

    private static void RequireSha256(string value, string field)
    {
        if (!Sha256Pattern.IsMatch(value))
            throw new InvalidOperationException($"{field} 必须是小写 sha256: 加 64 位十六进制。");
    }
}

/// <summary>进程级只初始化一次；卡表和程序集哈希均不进入逐局或逐动作热路径。</summary>
public static class ReplayRuntimeIdentityProvider
{
    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<string, ReplayRuntimeIdentity> ByRuleset = new(StringComparer.Ordinal);
    private static ReplayRuntimeBuildIdentity? _build;

    public static void InitializeFromCurrentProcess(string engineCommit, string cardDbContentHash)
    {
        var assembly = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException("无法定位服务端入口程序集，拒绝生成重放身份。");
        var assemblyPath = assembly.Location;
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            throw new InvalidOperationException("无法读取正在运行的核心程序集，拒绝生成重放身份。");
        Initialize(new ReplayRuntimeBuildIdentity(
            engineCommit,
            ReplayContentManifest.HashFile(assemblyPath),
            cardDbContentHash));
    }

    public static void Initialize(ReplayRuntimeBuildIdentity build)
    {
        ReplayRuntimeIdentityFactory.ValidateBuild(build);
        lock (Gate)
        {
            if (_build is not null && _build != build)
                throw new InvalidOperationException("运行身份已经冻结，进程内禁止替换提交、二进制或卡表身份。");
            _build ??= build;
        }
    }

    public static ReplayRuntimeIdentity For(CardRuleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        var build = Volatile.Read(ref _build)
            ?? throw new InvalidOperationException("运行身份尚未在启动阶段初始化，拒绝创建缺少精确身份的新对局。");
        return ByRuleset.GetOrAdd(
            ruleset.ManifestHash,
            _ => ReplayRuntimeIdentityFactory.Create(build, ruleset));
    }
}

/// <summary>规范相对路径 + 原始文件字节的长度前缀内容清单。</summary>
internal static class ReplayContentManifest
{
    private static readonly byte[] SchemaPrefix = Encoding.UTF8.GetBytes("grandumi.content-manifest.v1\n");

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string HashFiles(string root, IEnumerable<string> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(files);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var entries = files.Select(file =>
        {
            var fullPath = Path.GetFullPath(file);
            if (!fullPath.StartsWith(normalizedRoot, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
                throw new InvalidOperationException($"内容清单文件越过根目录：{file}");
            var relative = Path.GetRelativePath(normalizedRoot, fullPath)
                .Replace('\\', '/')
                .Normalize(NormalizationForm.FormC);
            return (Relative: relative, FullPath: fullPath);
        }).OrderBy(entry => entry.Relative, StringComparer.Ordinal).ToArray();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(SchemaPrefix);
        Span<byte> length = stackalloc byte[8];
        foreach (var entry in entries)
        {
            var pathBytes = Encoding.UTF8.GetBytes(entry.Relative);
            BinaryPrimitives.WriteInt64BigEndian(length, pathBytes.Length);
            hash.AppendData(length);
            hash.AppendData(pathBytes);
            var fileLength = new FileInfo(entry.FullPath).Length;
            BinaryPrimitives.WriteInt64BigEndian(length, fileLength);
            hash.AppendData(length);
            using var stream = File.OpenRead(entry.FullPath);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer.AsSpan(0, read));
        }
        return "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static string HashDirectory(string root)
        => HashFiles(root, Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
}
