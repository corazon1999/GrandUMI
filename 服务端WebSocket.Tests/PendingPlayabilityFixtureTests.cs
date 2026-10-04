using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace GrandUMI.Tests;

public sealed class PendingPlayabilityFixtureTests
{
    [Fact]
    public async Task 独立卡表中的Pending仍被所有格式及最终建房入口拒绝()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(configuredRoot));
        var root = Path.Combine(Path.GetFullPath(configuredRoot!),
            "pending-playability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await RunFixture(CreateFixture(Path.Combine(root, "main-card"), pendingLeader: false));
            await RunFixture(CreateFixture(Path.Combine(root, "leader"), pendingLeader: true));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task RunFixture(
        (string Root, string ValidDeck, string PendingDeck, string PendingNumber) fixture)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(FixtureAssemblyPath())!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(FixtureAssemblyPath());
        startInfo.ArgumentList.Add("pending-playability");
        startInfo.ArgumentList.Add(fixture.Root);
        startInfo.ArgumentList.Add(fixture.ValidDeck);
        startInfo.ArgumentList.Add(fixture.PendingDeck);
        startInfo.ArgumentList.Add(fixture.PendingNumber);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 pending 可用状态隔离进程。 ");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.True(process.ExitCode == 0,
            $"{fixture.PendingNumber} pending 安全夹具失败（exit={process.ExitCode}）："
            + $"\nstdout={await stdout}\nstderr={await stderr}");
    }

    private static (string Root, string ValidDeck, string PendingDeck, string PendingNumber) CreateFixture(
        string root,
        bool pendingLeader)
    {
        Directory.CreateDirectory(root);
        var utf8 = new UTF8Encoding(false);
        const string setFile = "TS01.json";
        const string schemaFile = "_schema.v1.json";
        const string playabilityFile = "_playability.v1.json";
        File.WriteAllText(Path.Combine(root, schemaFile), "{}", utf8);

        var cards = new List<object>
        {
            new
            {
                number = "TS01-001", name = "测试领袖", color = "红", type = "领航", property = "打",
                power = "5000", cost = "5", keyWords = "测试", counter = "", effectTags = Array.Empty<string>(),
                abilities = Array.Empty<string>(), trigger = "", rarity = "L", subscript = 4,
            },
            new
            {
                number = "TS01-016", name = "可用测试领袖", color = "红", type = "领航", property = "打",
                power = "5000", cost = "5", keyWords = "测试", counter = "", effectTags = Array.Empty<string>(),
                abilities = Array.Empty<string>(), trigger = "", rarity = "L", subscript = 4,
            },
        };
        cards.AddRange(Enumerable.Range(2, 14).Select(index => (object)new
        {
            number = $"TS01-{index:000}", name = $"测试角色{index}", color = "红", type = "角色", property = "打",
            power = "1000", cost = "1", keyWords = "测试", counter = "反击+1000", effectTags = Array.Empty<string>(),
            abilities = Array.Empty<string>(), trigger = "", rarity = "C", subscript = 4,
        }));
        var setText = JsonSerializer.Serialize(cards);
        File.WriteAllText(Path.Combine(root, setFile), setText, utf8);

        var pendingNumber = pendingLeader ? "TS01-001" : "TS01-015";
        var playabilityText = JsonSerializer.Serialize(new
        {
            schemaVersion = "grandumi.card-playability.v1",
            pendingReason = "effect-implementation-pending",
            cards = new[] { new { number = pendingNumber, state = "pending" } },
        });
        File.WriteAllText(Path.Combine(root, playabilityFile), playabilityText, utf8);

        string schemaHash = HashFile(Path.Combine(root, schemaFile));
        string setHash = HashFile(Path.Combine(root, setFile));
        string playabilityHash = HashFile(Path.Combine(root, playabilityFile));
        string contentHash = HashBytes(Encoding.UTF8.GetBytes($"{setFile}\0{setHash}\0{cards.Count}\n"));
        var manifest = new
        {
            schemaVersion = "grandumi.card-content-manifest.v1",
            schema = new { path = schemaFile, sha256 = schemaHash },
            totalCards = cards.Count,
            contentSha256 = contentHash,
            files = new[] { new { path = setFile, sha256 = setHash, cardCount = cards.Count } },
            playability = new { path = playabilityFile, sha256 = playabilityHash, pendingCardCount = 1 },
        };
        File.WriteAllText(Path.Combine(root, "_manifest.v1.json"), JsonSerializer.Serialize(manifest), utf8);

        var main = Enumerable.Range(2, 13)
            .SelectMany(index => Enumerable.Repeat($"TS01-{index:000}", 4))
            .Take(50)
            .ToList();
        var validLines = new[] { "TS01-016" }.Concat(main).ToArray();
        var pendingLines = validLines.ToArray();
        if (pendingLeader) pendingLines[0] = "TS01-001";
        else pendingLines[^1] = "TS01-015";
        var validDeck = Path.Combine(root, "valid.deck");
        var pendingDeck = Path.Combine(root, "pending.deck");
        File.WriteAllText(validDeck, string.Join('\n', validLines), utf8);
        File.WriteAllText(pendingDeck, string.Join('\n', pendingLines), utf8);
        return (root, validDeck, pendingDeck, pendingNumber);
    }

    private static string FixtureAssemblyPath()
    {
        const string assembly = "GrandUMI.ProcessWorkerFixture.dll";
        var currentOutput = new DirectoryInfo(AppContext.BaseDirectory);
        var artifactsCandidate = currentOutput.Parent?.Parent is { } artifactsBin
            ? Path.Combine(artifactsBin.FullName, "GrandUMI.ProcessWorkerFixture", currentOutput.Name, assembly)
            : string.Empty;
        if (File.Exists(artifactsCandidate)) return artifactsCandidate;

        var configuration = currentOutput.Parent?.Name
            ?? throw new DirectoryNotFoundException("无法识别测试构建配置目录。 ");
        var repositoryCandidate = RepoPath(
            "服务端WebSocket.ProcessWorkerFixture", "bin", configuration, "net10.0", assembly);
        Assert.True(File.Exists(repositoryCandidate), $"缺少进程夹具：{repositoryCandidate}");
        return repositoryCandidate;
    }

    private static string RepoPath(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            // 测试输出目录也会携带一份“卡牌数据”，不能单凭该目录判定仓库根。
            // 用两个实际项目文件固定边界，避免在干净克隆的 bin/Debug/net10.0 下
            // 把输出目录误当仓库，继而在其下重复拼接进程夹具路径。
            if (File.Exists(Path.Combine(current.FullName,
                    "服务端WebSocket.Tests", "GrandUMIServer.Tests.csproj"))
                && File.Exists(Path.Combine(current.FullName,
                    "服务端WebSocket.ProcessWorkerFixture", "GrandUMI.ProcessWorkerFixture.csproj")))
                return Path.Combine(new[] { current.FullName }.Concat(parts).ToArray());
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("未找到仓库根目录。 ");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashBytes(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
