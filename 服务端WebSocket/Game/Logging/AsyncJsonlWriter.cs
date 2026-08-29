using System.Text.Json;
using System.Threading.Channels;
using GrandUMI.Diagnostics;

namespace GrandUMI.Game.Logging;

/// <summary>
/// 单线程有序 JSONL 写入器。游戏线程只负责把不可变快照放入队列，
/// 序列化、批量刷新和文件关闭均在后台完成，避免磁盘 I/O 阻塞对局结算。
/// </summary>
internal sealed class AsyncJsonlWriter
{
    private const int BatchDelayMs = 20;
    private const int MaxBatchSize = 256;
    private const int DefaultCapacity = 16_384;

    private readonly Channel<Command> _queue;
    private readonly Dictionary<string, StreamWriter> _writers = new();
    private readonly JsonSerializerOptions? _jsonOptions;
    private readonly Task _worker;
    private int _stopped;
    private int _queueDepth;
    private long _droppedEntries;

    public AsyncJsonlWriter(JsonSerializerOptions? jsonOptions = null, int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, MaxBatchSize);
        _jsonOptions = jsonOptions;
        _queue = Channel.CreateBounded<Command>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _worker = Task.Run(ProcessLoopAsync);
    }

    public int QueueDepth => Math.Max(0, Volatile.Read(ref _queueDepth));
    public long DroppedEntries => Interlocked.Read(ref _droppedEntries);

    public void Open(string key, string path, bool append)
    {
        ThrowIfStopped();
        EnqueueRequired(new OpenCommand(key, path, append));
        // 打开命令与后续 Append 由同一 Channel 保序；房间创建线程无需等待磁盘真正打开。
        // 否则数百房间同时创建时会占满线程池，连健康检查也无法及时执行。
    }

    public bool Append(string key, object entry)
    {
        if (Volatile.Read(ref _stopped) != 0) return false;
        var depth = Interlocked.Increment(ref _queueDepth);
        if (_queue.Writer.TryWrite(new AppendCommand(key, entry)))
        {
            LatencyDiagnostics.RecordMetric("JSONL 日志队列深度", depth, "条");
            return true;
        }

        Interlocked.Decrement(ref _queueDepth);
        Interlocked.Increment(ref _droppedEntries);
        LatencyDiagnostics.RecordMetric("JSONL 日志丢弃", 1, "条");
        return false;
    }

    /// <summary>关键审计行不因队列容量暂满而丢弃；暂满时等待单写队列接收。</summary>
    public void AppendRequired(string key, object entry)
        => EnqueueRequired(new AppendCommand(key, entry));

    public void Close(string key)
        => CloseDeferred(key).GetAwaiter().GetResult();

    /// <summary>有序排入关闭命令并返回完成任务，供高并发房间清理异步收尾。</summary>
    public Task CloseDeferred(string key)
    {
        if (Volatile.Read(ref _stopped) != 0) return Task.CompletedTask;
        var completion = NewCompletion();
        EnqueueRequired(new CloseCommand(key, completion));
        return completion.Task;
    }

    /// <summary>关闭文件后在同一写入线程删除，保证删除不会早于此前追加，也会被 Shutdown 排空。</summary>
    public Task DeleteDeferred(string key, string path)
    {
        if (Volatile.Read(ref _stopped) != 0) return Task.CompletedTask;
        var completion = NewCompletion();
        EnqueueRequired(new DeleteCommand(key, path, completion));
        return completion.Task;
    }

    public void Shutdown()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        _queue.Writer.TryComplete();
        _worker.GetAwaiter().GetResult();
    }

    private void EnqueueRequired(Command command)
    {
        ThrowIfStopped();
        Interlocked.Increment(ref _queueDepth);
        try
        {
            _queue.Writer.WriteAsync(command).AsTask().GetAwaiter().GetResult();
        }
        catch
        {
            Interlocked.Decrement(ref _queueDepth);
            throw;
        }
    }

    private async Task ProcessLoopAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync())
            {
                var batch = new List<Command>(MaxBatchSize);
                if (!_queue.Reader.TryRead(out var first)) continue;
                Interlocked.Decrement(ref _queueDepth);
                batch.Add(first);

                // 只有普通追加才等待合并；打开/关闭文件不人为增加房间创建和清理延迟。
                if (first is AppendCommand)
                    await Task.Delay(BatchDelayMs);

                while (batch.Count < MaxBatchSize && _queue.Reader.TryRead(out var command))
                {
                    Interlocked.Decrement(ref _queueDepth);
                    batch.Add(command);
                }

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
            {
                Interlocked.Decrement(ref _queueDepth);
                ProcessBatch(new[] { command });
            }

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
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[日志] 打开 {open.Key} 失败：{ex.Message}");
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

                case DeleteCommand delete:
                    try
                    {
                        if (_writers.Remove(delete.Key, out var deleteWriter))
                        {
                            deleteWriter.Flush();
                            deleteWriter.Dispose();
                            touched.Remove(deleteWriter);
                        }
                        File.Delete(delete.Path);
                        delete.Completion.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        delete.Completion.TrySetException(ex);
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
    private sealed record OpenCommand(string Key, string Path, bool Append) : Command;
    private sealed record AppendCommand(string Key, object Entry) : Command;
    private sealed record CloseCommand(string Key, TaskCompletionSource Completion) : Command;
    private sealed record DeleteCommand(string Key, string Path, TaskCompletionSource Completion) : Command;
}
