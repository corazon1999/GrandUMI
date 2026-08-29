using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using GrandUMI.Training;

namespace GrandUMI.ProcessWorkerFixture;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false, true);
        if (args.Length >= 1 && string.Equals(args[0], "child-loop", StringComparison.Ordinal))
        {
            if (args.Length != 2) return 2;
            await File.WriteAllTextAsync(args[1], Environment.ProcessId.ToString());
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return 0;
        }
        if (args.Length < 2) return 2;
        var scenario = args[0];
        if (string.Equals(scenario, "nonzero", StringComparison.Ordinal)) return 7;

        var archive = ReplayArtifactArchive.Verify(args[1]);
        var descriptor = ReplayArtifactArchive.CreateTestDescriptor(archive.Manifest);
        var workerId = ArtifactReplayProcessProtocol.WorkerId(descriptor.EngineArtifactId);
        var hello = ArtifactReplayProcessProtocol.CreateHello(
            descriptor.EngineArtifactId,
            ReplayArtifactIdentity.Fingerprint(descriptor),
            workerId,
            archive.Manifest.RuntimeIdentity.ManifestHash);
        var stdout = Console.OpenStandardOutput();
        await ArtifactReplayProcessProtocol.WriteAsync(
            stdout,
            ArtifactReplayProcessProtocol.Serialize(hello),
            CancellationToken.None);

        var stdin = Console.OpenStandardInput();
        var client = await ArtifactReplayProcessProtocol.ReadAsync<ArtifactReplayProcessClientFrame>(
            stdin,
            ProcessArtifactReplayWorker.DefaultMaximumRequestBytes,
            CancellationToken.None);
        var trailing = new byte[1];
        if (await stdin.ReadAsync(trailing) != 0) return 3;

        switch (scenario)
        {
            case "stderr-noise":
                Console.Error.WriteLine("fixture stderr 噪声不会污染 stdout 协议");
                await WriteReadyAsync(stdout, hello);
                return 0;
            case "extra-frame":
                await WriteReadyAsync(stdout, hello);
                await WriteReadyAsync(stdout, hello);
                return 0;
            case "overlong":
                await WritePrefixAsync(stdout, 64 * 1024 * 1024);
                return 0;
            case "truncated":
                await WritePrefixAsync(stdout, 100);
                await stdout.WriteAsync("{}"u8.ToArray());
                return 0;
            case "invalid-utf8":
                await WritePrefixAsync(stdout, 1);
                await stdout.WriteAsync(new byte[] { 0xff });
                return 0;
            case "invalid-json":
                await WritePrefixAsync(stdout, 1);
                await stdout.WriteAsync("{"u8.ToArray());
                return 0;
            case "tampered-result":
                if (client.Request is null) return 4;
                var failure = new ArtifactReplayWorkerFailure(
                    ReplayQuarantineCodes.WorkerFailure,
                    "fixture",
                    "fixture tampered response",
                    SourceSeq: null,
                    ActionIndex: null);
                var response = new ArtifactReplayWorkerResponse(
                    ArtifactReplayWorkerDispatcher.ResponseSchema,
                    client.Request.RequestHash,
                    client.Request.ArtifactFingerprint,
                    workerId,
                    Verified: null,
                    failure,
                    StableHash: "sha256:" + new string('f', 64));
                var server = new ArtifactReplayProcessServerFrame(
                    ArtifactReplayProcessProtocol.Schema,
                    ArtifactReplayProcessProtocol.ResultKind,
                    response,
                    Probe: null);
                await ArtifactReplayProcessProtocol.WriteAsync(
                    stdout,
                    ArtifactReplayProcessProtocol.Serialize(server),
                    CancellationToken.None);
                return 0;
            case "stderr-overflow":
                Console.Error.Write(new string('x', 16 * 1024));
                Console.Error.Flush();
                await Task.Delay(Timeout.InfiniteTimeSpan);
                return 0;
            case "hang-child":
                if (args.Length != 3) return 5;
                var assemblyPath = typeof(Program).Assembly.Location;
                var child = Process.Start(new ProcessStartInfo
                {
                    FileName = "dotnet",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    ArgumentList =
                    {
                        assemblyPath,
                        "child-loop",
                        args[2],
                    },
                });
                if (child is null) return 6;
                while (!File.Exists(args[2])) await Task.Delay(10);
                await Task.Delay(Timeout.InfiniteTimeSpan);
                return 0;
            default:
                return 2;
        }
    }

    private static Task WriteReadyAsync(Stream stdout, ArtifactReplayProcessHello hello)
    {
        var server = new ArtifactReplayProcessServerFrame(
            ArtifactReplayProcessProtocol.Schema,
            ArtifactReplayProcessProtocol.ReadyKind,
            Response: null,
            ArtifactReplayProcessProtocol.CreateProbe(hello));
        return ArtifactReplayProcessProtocol.WriteAsync(
            stdout,
            ArtifactReplayProcessProtocol.Serialize(server),
            CancellationToken.None);
    }

    private static async Task WritePrefixAsync(Stream stdout, int length)
    {
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(prefix, length);
        await stdout.WriteAsync(prefix);
        await stdout.FlushAsync();
    }
}
