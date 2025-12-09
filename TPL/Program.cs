using System.Collections.Concurrent;
using System.Diagnostics;

Console.WriteLine("=== TPL, async/await, and threading demo ===");

using var appCts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    appCts.Cancel();
    Console.WriteLine("Cancellation requested by user (Ctrl+C).");
};

await ThreadDemo.RunThreadVsAsync(appCts.Token);

var processedFiles = await FileProcessingDemo.RunAsync(appCts.Token);

await PluginDemo.RunAsync(processedFiles, appCts.Token);

PerformanceDemo.Measure(processedFiles.Values);

Console.WriteLine("=== Demo complete ===");

public static class ThreadDemo
{
    public static async Task RunThreadVsAsync(CancellationToken token)
    {
        Console.WriteLine();
        Console.WriteLine("== Threads vs async/await ==");
        var counters = new SharedCounters();
        var threads = new[]
        {
            new Thread(() => counters.IncrementWithSynchronization(1_000)),
            new Thread(() => counters.IncrementWithSynchronization(1_000))
        };

        foreach (var thread in threads)
        {
            thread.Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        Console.WriteLine($"Threaded unsafe counter (race): {counters.UnsafeCount}");
        Console.WriteLine($"Threaded safe counter via lock: {counters.LockCount}");
        Console.WriteLine($"Threaded safe counter via Monitor: {counters.MonitorCount}");
        Console.WriteLine($"Threaded safe counter via Mutex: {counters.MutexCount}");

        var asyncCounter = 0;
        var asyncGate = new SemaphoreSlim(1, 1);
        var asyncTasks = Enumerable.Range(0, 4).Select(async i =>
        {
            for (var j = 0; j < 500; j++)
            {
                token.ThrowIfCancellationRequested();
                await asyncGate.WaitAsync(token);
                try
                {
                    asyncCounter++;
                }
                finally
                {
                    asyncGate.Release();
                }

                await Task.Delay(1, token);
            }

            Console.WriteLine($"Async worker {i} completed on thread {Environment.CurrentManagedThreadId}");
        });

        await Task.WhenAll(asyncTasks);
        Console.WriteLine($"Async counter (SemaphoreSlim for synchronization): {asyncCounter}");
    }

    private sealed class SharedCounters
    {
        private readonly Lock _lock = new();
        private readonly object _monitor = new();
        private readonly Mutex _mutex = new();

        public int UnsafeCount { get; private set; }
        public int LockCount { get; private set; }
        public int MonitorCount { get; private set; }
        public int MutexCount { get; private set; }

        public void IncrementWithSynchronization(int iterations)
        {
            for (var i = 0; i < iterations; i++)
            {
                UnsafeCount++;

                lock (_lock)
                {
                    LockCount++;
                }

                var taken = false;
                Monitor.Enter(_monitor, ref taken);
                try
                {
                    MonitorCount++;
                }
                finally
                {
                    if (taken)
                    {
                        Monitor.Exit(_monitor);
                    }
                }

                _mutex.WaitOne();
                try
                {
                    MutexCount++;
                }
                finally
                {
                    _mutex.ReleaseMutex();
                }
            }
        }
    }
}

public static class FileProcessingDemo
{
    private static readonly string[] _fileNames =
    [
        "alpha.txt",
        "beta.txt",
        "gamma.txt",
        "delta.txt",
        "fail.txt",
        "epsilon.txt"
    ];

    public static async Task<Dictionary<string, string>> RunAsync(CancellationToken externalToken)
    {
        Console.WriteLine();
        Console.WriteLine("== Async/Await file processing with TPL ==");
        ThreadPool.GetAvailableThreads(out var workers, out var iocp);
        Console.WriteLine($"ThreadPool available threads: worker={workers}, io={iocp}");

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        linkedCts.CancelAfter(TimeSpan.FromMilliseconds(700));

        using var gate = new SemaphoreSlim(4);
        var processed = new ConcurrentDictionary<string, string>();

        var tasks = _fileNames.Select(name => ProcessFileAsync(name, gate, processed, linkedCts.Token)).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("File processing canceled.");
        }
        catch (AggregateException ex)
        {
            foreach (var inner in ex.Flatten().InnerExceptions)
            {
                Console.WriteLine($"Handled exception: {inner.GetType().Name} - {inner.Message}");
            }
        }

        Console.WriteLine($"Processed {processed.Count} of {_fileNames.Length} files.");
        return new Dictionary<string, string>(processed);
    }

    private static async Task ProcessFileAsync(
        string fileName,
        SemaphoreSlim gate,
        ConcurrentDictionary<string, string> processed,
        CancellationToken token)
    {
        await gate.WaitAsync(token);
        var sw = Stopwatch.StartNew();
        try
        {
            Console.WriteLine($"[{fileName}] queued on thread {Environment.CurrentManagedThreadId}");
            var content = await SimulateFileReadAsync(fileName, token);
            var transformed = await ProcessContentOnThreadPoolAsync(content, token);
            processed[fileName] = transformed;
            sw.Stop();
            Console.WriteLine($"[{fileName}] processed in {sw.ElapsedMilliseconds} ms");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"[{fileName}] canceled.");
           // throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{fileName}] failed: {ex.Message}");
            //throw;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<string> SimulateFileReadAsync(string fileName, CancellationToken token)
    {
        await Task.Delay(120, token);
        if (fileName.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Simulated read failure.");
        }

        var repeated = string.Join(' ', Enumerable.Repeat(fileName.Replace(".txt", ""), 200));
        return $"{fileName} content {repeated}";
    }

    private static async Task<string> ProcessContentOnThreadPoolAsync(string content, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var reversed = string.Join(' ', words.Select(w => new string(w.Reverse().ToArray())));
            return reversed;
        }, token);
    }
}

public static class PluginDemo
{
    public static async Task RunAsync(Dictionary<string, string> files, CancellationToken token)
    {
        Console.WriteLine();
        Console.WriteLine("== Plugin system (inheritance + composition) ==");
        var content = files.Values.FirstOrDefault() ?? "fallback content for plugin pipeline";

        var plugins = new List<IPlugin>
        {
            new UppercasePlugin(new UppercaseTransformer()),
            new ReversePlugin(new ReverseTransformer()),
            new KeywordAnnotatorPlugin(new KeywordAnnotator(["alpha", "delta"]))
        };

        var host = new PluginHost(plugins);
        var outputs = await host.RunAsync(content, token);
        foreach (var (name, value) in outputs)
        {
            Console.WriteLine($"[{name}] {value[..Math.Min(value.Length, 80)]}...");
        }
    }
}

public interface IPlugin
{
    string Name { get; }
    Task<string> ProcessAsync(string content, CancellationToken token);
}

public abstract class PluginBase(string name, IContentTransformer transformer) : IPlugin
{
    public string Name { get; } = name;

    public virtual Task<string> ProcessAsync(string content, CancellationToken token) =>
        transformer.TransformAsync(content, token);
}

public interface IContentTransformer
{
    Task<string> TransformAsync(string content, CancellationToken token);
}

public sealed class UppercasePlugin(IContentTransformer transformer) : PluginBase("UppercasePlugin", transformer);

public sealed class ReversePlugin(IContentTransformer transformer) : PluginBase("ReversePlugin", transformer);

public sealed class KeywordAnnotatorPlugin(IContentTransformer transformer) : PluginBase("KeywordAnnotator", transformer);

public sealed class UppercaseTransformer : IContentTransformer
{
    public Task<string> TransformAsync(string content, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(content.ToUpperInvariant());
    }
}

public sealed class ReverseTransformer : IContentTransformer
{
    public Task<string> TransformAsync(string content, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var reversed = new string(content.Reverse().ToArray());
        return Task.FromResult(reversed);
    }
}

public sealed class KeywordAnnotator(IEnumerable<string> keywords) : IContentTransformer
{
    private readonly IReadOnlyCollection<string> _keywords = keywords.ToArray();

    public Task<string> TransformAsync(string content, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var found = _keywords.Where(k => content.Contains(k, StringComparison.OrdinalIgnoreCase)).ToArray();
        var tag = found.Length == 0 ? "no-keywords" : string.Join('-', found);
        return Task.FromResult($"[{tag}] {content}");
    }
}

public sealed class PluginHost(IEnumerable<IPlugin> plugins)
{
    private readonly IReadOnlyList<IPlugin> _plugins = plugins.ToArray();

    public async Task<Dictionary<string, string>> RunAsync(string content, CancellationToken token)
    {
        var results = new Dictionary<string, string>();
        foreach (var plugin in _plugins)
        {
            var output = await plugin.ProcessAsync(content, token);
            results[plugin.Name] = output;
        }

        return results;
    }
}

public static class PerformanceDemo
{
    public static void Measure(IEnumerable<string> contents)
    {
        Console.WriteLine();
        Console.WriteLine("== PLINQ performance ==");
        var corpus = contents.DefaultIfEmpty("placeholder text for performance measurement").ToList();

        var watch = Stopwatch.StartNew();
        var sequential = corpus.SelectMany(TextToWords).Count();
        watch.Stop();
        var sequentialMs = watch.ElapsedMilliseconds;

        watch.Restart();
        var parallel = corpus.AsParallel()
            .WithDegreeOfParallelism(Math.Min(Environment.ProcessorCount, 4))
            .SelectMany(TextToWords)
            .Count();
        watch.Stop();
        var parallelMs = watch.ElapsedMilliseconds;

        Console.WriteLine($"Sequential word count: {sequential} in {sequentialMs} ms");
        Console.WriteLine($"PLINQ word count: {parallel} in {parallelMs} ms (delta {sequentialMs - parallelMs} ms)");
    }

    private static IEnumerable<string> TextToWords(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
