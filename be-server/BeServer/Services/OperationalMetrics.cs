using System.Diagnostics;

namespace BeServer.Services;

public class OperationalMetrics
{
    private long _requestCount;
    private long _errorCount;
    private long _totalDurationMs;

    public void ObserveRequest(long durationMs, bool isError)
    {
        Interlocked.Increment(ref _requestCount);
        if (isError) Interlocked.Increment(ref _errorCount);
        Interlocked.Add(ref _totalDurationMs, durationMs);
    }

    public object Snapshot()
    {
        var count = Interlocked.Read(ref _requestCount);
        var total = Interlocked.Read(ref _totalDurationMs);
        return new
        {
            count,
            errors = Interlocked.Read(ref _errorCount),
            average_duration_ms = count == 0 ? 0 : Math.Round((double)total / count, 2),
        };
    }
}
