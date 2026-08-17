using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using GrandUMI.Effects.Dsl;
using GrandUMI.Game;

namespace GrandUMI.Effects.Rules;

/// <summary>
/// 一局游戏固定使用的不可变卡效规则集。激活新规则集只影响之后创建的对局，
/// 已经持有旧实例的对局会继续使用旧规则，直到自然结束。
/// </summary>
public sealed class CardRuleset
{
    private readonly IReadOnlyDictionary<string, IScriptedEffect> _scriptedEffects;
    private readonly IReadOnlyDictionary<string, JsonElement> _dslDefinitions;

    internal CardRuleset(
        string id,
        string? baseRulesetId,
        string description,
        IReadOnlyDictionary<string, IScriptedEffect> scriptedEffects,
        IReadOnlyDictionary<string, JsonElement> dslDefinitions,
        IReadOnlyCollection<string> changedCards,
        IReadOnlyCollection<AssemblyLoadContext>? loadContexts = null)
    {
        Id = id;
        BaseRulesetId = baseRulesetId;
        Description = description;
        _scriptedEffects = scriptedEffects;
        _dslDefinitions = dslDefinitions;
        ChangedCards = changedCards.Order(StringComparer.Ordinal).ToArray();
        LoadContexts = loadContexts?.ToArray() ?? [];
    }

    public string Id { get; }
    public string? BaseRulesetId { get; }
    public string Description { get; }
    public IReadOnlyList<string> ChangedCards { get; }

    // 规则集存活期间保留插件加载上下文，避免程序集在仍有对局引用时被回收。
    internal IReadOnlyList<AssemblyLoadContext> LoadContexts { get; }

    public IScriptedEffect? TryGetScriptedEffect(string cardNumber)
        => _scriptedEffects.TryGetValue(cardNumber, out var effect) ? effect : null;

    internal bool TryGetDslDefinition(string cardNumber, out JsonElement definition)
        => _dslDefinitions.TryGetValue(cardNumber, out definition);

    public bool HasOncePerTurnEffect(string cardNumber)
        => _dslDefinitions.TryGetValue(cardNumber, out var definition)
           && DslInterpreter.ContainsOncePerTurnDefinition(definition);

    internal Dictionary<string, IScriptedEffect> CloneScriptedEffects()
        => new Dictionary<string, IScriptedEffect>(_scriptedEffects, StringComparer.OrdinalIgnoreCase);

    internal Dictionary<string, JsonElement> CloneDslDefinitions()
        => _dslDefinitions.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase);
}

public sealed record RulesetActivationResult(
    string PreviousRulesetId,
    string CurrentRulesetId,
    string Description,
    IReadOnlyList<string> ChangedCards);

public sealed record RulesetUpdateNotice(
    string PreviousRulesetId,
    string CurrentRulesetId,
    string Description,
    IReadOnlyList<string> ChangedCards);

/// <summary>
/// 规则集注册与激活入口。规则包位于持久化数据目录的 Rulesets/&lt;id&gt;/ 下；
/// 激活指针通过原子替换更新，因此并发建房只会完整取得旧版或新版，不会看到半加载状态。
/// </summary>
public static class CardRulesetManager
{
    private const string ActiveRulesetFileName = "active-ruleset";
    private static readonly object Gate = new();
    private static readonly Dictionary<string, CardRuleset> Rulesets = new(StringComparer.OrdinalIgnoreCase);
    private static CardRuleset? _builtIn;
    private static CardRuleset? _current;
    private static string? _packageRoot;

    public static CardRuleset BuiltIn
    {
        get
        {
            lock (Gate) return EnsureBootstrapLocked();
        }
    }

    public static CardRuleset Current
    {
        get
        {
            var current = Volatile.Read(ref _current);
            if (current is not null) return current;
            lock (Gate) return _current ??= EnsureBootstrapLocked();
        }
    }

    public static CardRuleset For(GameState state)
        => state.Ruleset ?? Current;

    /// <summary>加载当前发布产物自带的完整 DSL 与手写脚本，作为所有热更新包的基线。</summary>
    public static void InitializeBuiltIn(
        IReadOnlyDictionary<string, JsonElement> definitions,
        string rulesetId,
        string description = "当前服务端内置规则")
    {
        ValidateRulesetId(rulesetId);
        lock (Gate)
        {
            if (_builtIn is not null && _builtIn.Id != "builtin-uninitialized")
                return;

            var scripted = ScriptedEffectRegistry.ScanAssembly(typeof(ScriptedEffectRegistry).Assembly);
            var clonedDefinitions = definitions.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone(),
                StringComparer.OrdinalIgnoreCase);
            var builtIn = new CardRuleset(
                rulesetId,
                baseRulesetId: null,
                description,
                scripted,
                clonedDefinitions,
                changedCards: []);
            Rulesets.Remove("builtin-uninitialized");
            Rulesets[rulesetId] = builtIn;
            _builtIn = builtIn;
            _current = builtIn;
            Console.WriteLine($"[规则集] 已加载内置规则 {rulesetId}：{scripted.Count} 个手写卡效，{clonedDefinitions.Count} 个 DSL 定义");
        }
    }

    /// <summary>扫描持久化规则包，并恢复上次激活的版本。单个无效包不会影响内置规则启动。</summary>
    public static void InitializePackages(string packageRoot)
    {
        var root = Path.GetFullPath(packageRoot);
        Directory.CreateDirectory(root);
        lock (Gate)
        {
            EnsureBootstrapLocked();
            _packageRoot = root;
            LoadAllPackagesLocked(root);

            var activePath = Path.Combine(root, ActiveRulesetFileName);
            if (!File.Exists(activePath)) return;
            var activeId = File.ReadAllText(activePath).Trim();
            if (Rulesets.TryGetValue(activeId, out var active))
            {
                _current = active;
                Console.WriteLine($"[规则集] 已恢复激活版本 {active.Id}");
            }
            else
            {
                Console.Error.WriteLine($"[规则集] 激活记录指向不可用版本 {activeId}，继续使用内置规则 {_builtIn!.Id}");
            }
        }
    }

    public static IReadOnlyList<object> Snapshot()
    {
        lock (Gate)
        {
            var currentId = Current.Id;
            return Rulesets.Values
                .OrderBy(ruleset => ruleset.Id, StringComparer.Ordinal)
                .Select(ruleset => (object)new
                {
                    id = ruleset.Id,
                    baseRulesetId = ruleset.BaseRulesetId,
                    ruleset.Description,
                    changedCards = ruleset.ChangedCards,
                    active = string.Equals(ruleset.Id, currentId, StringComparison.OrdinalIgnoreCase),
                })
                .ToArray();
        }
    }

    /// <summary>重新扫描磁盘上的规则包。已加载规则集保持不可变，新增完整包会被加入可激活列表。</summary>
    public static void RefreshPackages()
    {
        lock (Gate)
        {
            if (_packageRoot is null)
                throw new InvalidOperationException("规则包目录尚未初始化");
            LoadAllPackagesLocked(_packageRoot);
        }
    }

    public static CardRuleset GetRequired(string rulesetId)
    {
        lock (Gate)
        {
            if (Rulesets.TryGetValue(rulesetId, out var ruleset)) return ruleset;
            throw new InvalidOperationException($"对局要求的规则版本不可用：{rulesetId}");
        }
    }

    public static RulesetActivationResult Activate(string rulesetId)
    {
        ValidateRulesetId(rulesetId);
        lock (Gate)
        {
            if (!Rulesets.TryGetValue(rulesetId, out var target))
                throw new InvalidOperationException($"规则版本不存在或加载失败：{rulesetId}");

            var previous = Current;
            PersistActiveRulesetLocked(target.Id);
            Volatile.Write(ref _current, target);
            var notice = BuildUpdateNoticeLocked(previous.Id, target.Id);
            Console.WriteLine($"[规则集] 已激活 {previous.Id} -> {target.Id}");
            return new RulesetActivationResult(previous.Id, target.Id, notice.Description, notice.ChangedCards);
        }
    }

    public static RulesetUpdateNotice? BuildUpdateNotice(string previousRulesetId)
    {
        lock (Gate)
        {
            var current = Current;
            if (string.Equals(previousRulesetId, current.Id, StringComparison.OrdinalIgnoreCase)) return null;
            return BuildUpdateNoticeLocked(previousRulesetId, current.Id);
        }
    }

    private static RulesetUpdateNotice BuildUpdateNoticeLocked(string previousRulesetId, string currentRulesetId)
    {
        if (!Rulesets.TryGetValue(currentRulesetId, out var current))
            return new RulesetUpdateNotice(previousRulesetId, currentRulesetId, "卡牌效果规则已更新", []);

        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cursor = current;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (visited.Add(cursor.Id))
        {
            foreach (var card in cursor.ChangedCards) changed.Add(card);
            if (string.Equals(cursor.BaseRulesetId, previousRulesetId, StringComparison.OrdinalIgnoreCase)) break;
            if (cursor.BaseRulesetId is null || !Rulesets.TryGetValue(cursor.BaseRulesetId, out var parent))
            {
                changed.Clear();
                break;
            }

            cursor = parent;
        }

        return new RulesetUpdateNotice(
            previousRulesetId,
            currentRulesetId,
            string.IsNullOrWhiteSpace(current.Description) ? "卡牌效果规则已更新" : current.Description,
            changed.Order(StringComparer.Ordinal).ToArray());
    }

    private static CardRuleset EnsureBootstrapLocked()
    {
        if (_builtIn is not null) return _builtIn;
        var scripted = ScriptedEffectRegistry.ScanAssembly(typeof(ScriptedEffectRegistry).Assembly);
        _builtIn = new CardRuleset(
            "builtin-uninitialized",
            baseRulesetId: null,
            "尚未加载 DSL 的内置规则",
            scripted,
            new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
            changedCards: []);
        Rulesets[_builtIn.Id] = _builtIn;
        _current = _builtIn;
        return _builtIn;
    }

    private static void LoadAllPackagesLocked(string root)
    {
        var pending = Directory.GetDirectories(root)
            .Where(directory => File.Exists(Path.Combine(directory, "manifest.json")))
            .Order(StringComparer.Ordinal)
            .ToList();

        bool madeProgress;
        do
        {
            madeProgress = false;
            foreach (var directory in pending.ToArray())
            {
                try
                {
                    var manifest = ReadManifest(directory);
                    if (Rulesets.ContainsKey(manifest.Id))
                    {
                        pending.Remove(directory);
                        continue;
                    }
                    if (!Rulesets.TryGetValue(manifest.BaseRulesetId, out var baseRuleset)) continue;
                    Rulesets[manifest.Id] = LoadPackage(directory, manifest, baseRuleset);
                    pending.Remove(directory);
                    madeProgress = true;
                    Console.WriteLine($"[规则集] 已发现热更新包 {manifest.Id}（基于 {manifest.BaseRulesetId}）");
                }
                catch (Exception ex)
                {
                    pending.Remove(directory);
                    Console.Error.WriteLine($"[规则集] 跳过无效规则包 {directory}：{ex.Message}");
                }
            }
        } while (madeProgress && pending.Count > 0);

        foreach (var directory in pending)
            Console.Error.WriteLine($"[规则集] 跳过规则包 {directory}：基础版本不存在");
    }

    private static CardRuleset LoadPackage(string directory, RulesetManifest manifest, CardRuleset baseRuleset)
    {
        ValidateRulesetId(manifest.Id);
        ValidateRulesetId(manifest.BaseRulesetId);
        var scripted = new Dictionary<string, IScriptedEffect>(
            baseRuleset.CloneScriptedEffects(),
            StringComparer.OrdinalIgnoreCase);
        var definitions = new Dictionary<string, JsonElement>(
            baseRuleset.CloneDslDefinitions(),
            StringComparer.OrdinalIgnoreCase);
        var changedCards = new HashSet<string>(manifest.ChangedCards ?? [], StringComparer.OrdinalIgnoreCase);
        var overriddenCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contexts = new List<AssemblyLoadContext>();

        if (!string.IsNullOrWhiteSpace(manifest.DefinitionsDirectory))
        {
            var definitionsDirectory = ResolveInside(directory, manifest.DefinitionsDirectory);
            var overrides = DslInterpreter.ReadDefinitionsDirectory(definitionsDirectory, strict: true);
            foreach (var pair in overrides)
            {
                definitions[pair.Key] = pair.Value.Clone();
                changedCards.Add(pair.Key);
                overriddenCards.Add(pair.Key);
            }
        }

        foreach (var relativeAssembly in manifest.Assemblies ?? [])
        {
            var assemblyPath = ResolveInside(directory, relativeAssembly);
            if (!File.Exists(assemblyPath)) throw new FileNotFoundException("规则程序集不存在", assemblyPath);
            var context = new RulesetPluginLoadContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            var overrides = ScriptedEffectRegistry.ScanAssembly(assembly, rejectDuplicates: true);
            if (overrides.Count == 0)
                throw new InvalidOperationException($"规则程序集没有 IScriptedEffect 实现：{relativeAssembly}");
            foreach (var pair in overrides)
            {
                scripted[pair.Key] = pair.Value;
                changedCards.Add(pair.Key);
                overriddenCards.Add(pair.Key);
            }
            contexts.Add(context);
        }

        if (overriddenCards.Count == 0)
            throw new InvalidOperationException("规则包没有实际覆盖任何 DSL 定义或手写卡效");

        return new CardRuleset(
            manifest.Id,
            manifest.BaseRulesetId,
            manifest.Description ?? "卡牌效果规则已更新",
            scripted,
            definitions,
            changedCards,
            contexts);
    }

    private static RulesetManifest ReadManifest(string directory)
    {
        var json = File.ReadAllText(Path.Combine(directory, "manifest.json"));
        return JsonSerializer.Deserialize<RulesetManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("manifest.json 内容为空");
    }

    private static string ResolveInside(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new InvalidOperationException("规则包路径必须是相对路径");
        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        if (!resolved.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("规则包路径越过了包目录");
        return resolved;
    }

    private static void PersistActiveRulesetLocked(string rulesetId)
    {
        if (_packageRoot is null) return;
        var target = Path.Combine(_packageRoot, ActiveRulesetFileName);
        var temporary = target + ".next";
        File.WriteAllText(temporary, rulesetId + Environment.NewLine);
        File.Move(temporary, target, overwrite: true);
    }

    private static void ValidateRulesetId(string rulesetId)
    {
        if (string.IsNullOrWhiteSpace(rulesetId)
            || rulesetId.Length > 80
            || rulesetId.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-')))
            throw new InvalidOperationException($"规则版本 ID 无效：{rulesetId}");
    }

    private sealed class RulesetPluginLoadContext(string mainAssemblyPath) : AssemblyLoadContext(isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var serverAssembly = typeof(IScriptedEffect).Assembly;
            if (string.Equals(assemblyName.Name, serverAssembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
                return serverAssembly;
            var dependencyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return dependencyPath is null ? null : LoadFromAssemblyPath(dependencyPath);
        }
    }

    private sealed record RulesetManifest(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("baseRulesetId")] string BaseRulesetId,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("definitionsDirectory")] string? DefinitionsDirectory,
        [property: JsonPropertyName("assemblies")] string[]? Assemblies,
        [property: JsonPropertyName("changedCards")] string[]? ChangedCards);
}
