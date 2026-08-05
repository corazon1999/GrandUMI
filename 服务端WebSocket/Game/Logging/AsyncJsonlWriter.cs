using System.Text.Json;
using System.Threading.Channels;

namespace GrandUMI.Game.Logging;

/// <summary>
/// 单线程有序 JSONL 写入器。游戏线程只负责把不可变快照放入队列，
/// 序列化、批量刷新和文件关闭均在后台完成，避免磁盘 I/O 阻塞对局结算。
/// </summary>
internal sealed class AsyncJsonlWriter
{
    private const int BatchDelayMs = 20;
    private const int MaxBatchSize = 256;

    private readonly Channel<Command> _queue = Channel.CreateUnbounded<Command>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
    });
    private readonly Dictionary<string, StreamWriter> _writers = new();
    private readonly JsonSerializerOptions? _jsonOptions;
    private readonly Task _worker;
    private int _stopped;

    public AsyncJsonlWriter(JsonSerializerOptions? jsonOptions = null)
    {
        _jsonOptions = jsonOptions;
        _worker = Task.Run(ProcessLoopAsync);
    }

    public void Open(string key, string path, bool append)
    {
        ThrowIfStopped();
        var completion = NewCompletion();
        Enqueue(new OpenCommand(key, path, append, completion));
        completion.Task.GetAwaiter().GetResult();
    }

    public void Append(string key, object entry)
    {
        if (Volatile.Read(ref _stopped) != 0) return;
        Enqueue(new AppendCommand(key, entry));
    }

    public void Close(string key)
    {
        if (Volatile.Read(ref _stopped) != 0) return;
        var completion = NewCompletion();
        Enqueue(new CloseCommand(key, completion));
        completion.Task.GetAwaiter().GetResult();
    }

    public void Shutdown()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        _queue.Writer.TryComplete();
        _worker.GetAwaiter().GetResult();
    }

    private void Enqueue(Command command)
    {
        if (!_queue.Writer.TryWrite(command))
            throw new InvalidOperationException("日志后台队列已停止");
    }

    private async Task ProcessLoopAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync())
            {
                var batch = new List<Command>(MaxBatchSize);
                if (!_queue.Reader.TryRead(out var first)) continue;
                batch.Add(first);

                // 只有普通追加才等待合并；打开/关闭文件不人为增加房间创建和清理延迟。
                if (first is AppendCommand)
                    await Task.Delay(BatchDelayMs);

                while (batch.Count < MaxBatchSize && _queue.Reader.TryRead(out var command))
                    batch.Add(command);

                ProcessBatch(batch);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[日志] 后台写入器异常: {ex.Message}");
        }
        finally
        {
            while (_queue.Reader.TryRead(out var command))
                ProcessBatch(new[] { command });

            foreach (var writer in _writers.Values)
            {
                try { writer.Flush(); writer.Dispose(); } catch { }
            }
            _writers.Clear();
        }
    }

    private void ProcessBatch(IReadOnlyList<Command> batch)
    {
        var touched = new HashSet<StreamWriter>();

        foreach (var command in batch)
        {
            switch (command)
            {
                case OpenCommand open:
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(open.Path)!);
                        if (_writers.Remove(open.Key, out var oldWriter))
                        {
                            oldWriter.Flush();
                            oldWriter.Dispose();
                        }
                        _writers[open.Key] = new StreamWriter(open.Path, append: open.Append);
                        open.Completion.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        open.Completion.TrySetException(ex);
                    }
                    break;

                case AppendCommand append:
                    if (!_writers.TryGetValue(append.Key, out var writer)) break;
                    try
                    {
                        writer.WriteLine(JsonSerializer.Serialize(append.Entry, _jsonOptions));
                        touched.Add(writer);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[日志] 写入 {append.Key} 失败: {ex.Message}");
                    }
                    break;

                case CloseCommand close:
                    try
                    {
                        if (_writers.Remove(close.Key, out var closeWriter))
                        {
                            closeWriter.Flush();
                            closeWriter.Dispose();
                            touched.Remove(closeWriter);
                        }
                        close.Completion.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        close.Completion.TrySetException(ex);
                    }
                    break;
            }
        }

        // 每批最多刷新一次，而不是每写一行都触发磁盘刷新。
        foreach (var writer in touched)
        {
            try { writer.Flush(); } catch { }
        }
    }

    private void ThrowIfStopped()
    {
        if (Volatile.Read(ref _stopped) != 0)
            throw new InvalidOperationException("日志后台队列已停止");
    }

    private static TaskCompletionSource NewCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private abstract record Command;
    private sealed record OpenCommand(string Key, string Path, bool Append, TaskCompletionSource Completion) : Command;
    private sealed record AppendCommand(string Key, object Entry) : Command;
    private sealed record CloseCommand(string Key, TaskCompletionSource Completion) : Command;
}
