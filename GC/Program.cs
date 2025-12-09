using System.Diagnostics;
using System.Runtime.InteropServices;

const int allocationSizeBytes = 64 * 1024;
const int allocationCount = 30000;

var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
var runAll = mode is "all" or "both";

if (mode is "help" or "--help" or "-h")
{
    PrintHelp();
    return;
}

var scenarios = new List<(string Name, Action Action)>
{
    ("Unoptimized (finalizer heavy)", () => RunUnoptimized(allocationSizeBytes, allocationCount)),
    ("Optimized (deterministic dispose)", () => RunOptimized(allocationSizeBytes, allocationCount))
};

if (!runAll)
{
    scenarios = scenarios
        .Where(s => s.Name.Contains(mode, StringComparison.OrdinalIgnoreCase))
        .ToList();

    if (scenarios.Count == 0)
    {
        Console.WriteLine($"Unknown mode '{mode}'.");
        PrintHelp();
        return;
    }
}

foreach (var scenario in scenarios)
{
    RunScenario(scenario.Name, scenario.Action);
}

return;

static void RunScenario(string name, Action scenario)
{
    Console.WriteLine($"\n=== {name} ===");
    var stopwatch = Stopwatch.StartNew();

    var startDispose = UnmanagedBlock.BytesReleasedViaDispose;
    var startFinalizer = UnmanagedBlock.BytesReleasedViaFinalizer;
    var startCollections = new[]
    {
        GC.CollectionCount(0),
        GC.CollectionCount(1),
        GC.CollectionCount(2)
    };

    PrintMemory("Before allocations");
    scenario();
    PrintMemory("After allocations (before GC)");

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    stopwatch.Stop();
    PrintMemory("After forced cleanup");

    var disposeDelta = UnmanagedBlock.BytesReleasedViaDispose - startDispose;
    var finalizerDelta = UnmanagedBlock.BytesReleasedViaFinalizer - startFinalizer;

    Console.WriteLine($"Released via dispose:   {FormatBytes(disposeDelta)}");
    Console.WriteLine($"Released via finalizer: {FormatBytes(finalizerDelta)}");
    Console.WriteLine(
        $"GC collections (Δ): gen0={GC.CollectionCount(0) - startCollections[0]}, " +
        $"gen1={GC.CollectionCount(1) - startCollections[1]}, " +
        $"gen2={GC.CollectionCount(2) - startCollections[2]}");
    Console.WriteLine($"Elapsed: {stopwatch.Elapsed.TotalSeconds:F2}s");
}

static void RunUnoptimized(int blockSize, int count)
{
    var blocks = new List<UnmanagedBlock>(count);

    for (var i = 0; i < count; i++)
    {
        blocks.Add(new UnmanagedBlock(blockSize));

        if ((i + 1) % 500 == 0)
        {
            PrintMemory($"Allocated {i + 1} blocks");
        }
    }

    blocks.Clear();
}

static void RunOptimized(int blockSize, int count)
{
    for (var i = 0; i < count; i++)
    {
        using var block = new UnmanagedBlock(blockSize);
        block.Touch((byte)(i & 0xFF));

        if ((i + 1) % 500 == 0)
        {
            PrintMemory($"Allocated and disposed {i + 1} blocks");
        }
    }
}

static void PrintMemory(string label)
{
    var process = Process.GetCurrentProcess();
    var workingSet = FormatBytes(process.WorkingSet64);
    var managed = FormatBytes(GC.GetTotalMemory(forceFullCollection: false));

    Console.WriteLine($"{label}: working set={workingSet}, managed={managed}");
}

static string FormatBytes(long bytes)
{
    const double scale = 1024d;
    var mb = bytes / (scale * scale);
    return $"{mb:F1} MB";
}

static void PrintHelp()
{
    Console.WriteLine("GC demo modes:");
    Console.WriteLine("  all/both     Run both scenarios (default)");
    Console.WriteLine("  unoptimized  Allocate unmanaged memory and rely on finalizers");
    Console.WriteLine("  optimized    Dispose unmanaged memory immediately");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run -- unoptimized");
    Console.WriteLine("  dotnet run -- optimized");
}

internal sealed class UnmanagedBlock : IDisposable
{
    private static long _bytesReleasedViaDispose;
    private static long _bytesReleasedViaFinalizer;

    public static long BytesReleasedViaDispose => Interlocked.Read(ref _bytesReleasedViaDispose);
    public static long BytesReleasedViaFinalizer => Interlocked.Read(ref _bytesReleasedViaFinalizer);

    private IntPtr _buffer;
    private readonly int _size;
    private bool _disposed;

    public UnmanagedBlock(int size)
    {
        _size = size;
        _buffer = Marshal.AllocHGlobal(size);
        GC.AddMemoryPressure(size);

        var temp = new byte[Math.Min(32, size)];
        Random.Shared.NextBytes(temp);
        Marshal.Copy(temp, 0, _buffer, temp.Length);
    }

    public void Touch(byte value)
    {
        if (_buffer == IntPtr.Zero || _disposed)
        {
            return;
        }

        var temp = new byte[Math.Min(32, _size)];
        Array.Fill(temp, value);
        Marshal.Copy(temp, 0, _buffer, temp.Length);
    }

    public void Dispose()
    {
        Release(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Release(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            GC.RemoveMemoryPressure(_size);
            _buffer = IntPtr.Zero;

            if (disposing)
            {
                Interlocked.Add(ref _bytesReleasedViaDispose, _size);
            }
            else
            {
                Interlocked.Add(ref _bytesReleasedViaFinalizer, _size);
            }
        }

        _disposed = true;
    }

    ~UnmanagedBlock()
    {
        Release(disposing: false);
    }
}
