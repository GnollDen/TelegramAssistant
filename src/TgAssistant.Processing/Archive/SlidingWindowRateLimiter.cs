namespace TgAssistant.Processing.Archive;

internal class SlidingWindowRateLimiter
{
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly Queue<DateTime> _requests = new();
    private readonly object _lock = new();

    public SlidingWindowRateLimiter(int limit, TimeSpan window)
    {
        _limit = Math.Max(1, limit);
        _window = window;
    }

    public async Task WaitAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            TimeSpan? delay = null;
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                while (_requests.Count > 0 && now - _requests.Peek() >= _window)
                {
                    _requests.Dequeue();
                }

                if (_requests.Count < _limit)
                {
                    _requests.Enqueue(now);
                    return;
                }

                var oldest = _requests.Peek();
                delay = _window - (now - oldest);
            }

            if (delay is { } toWait && toWait > TimeSpan.Zero)
            {
                await Task.Delay(toWait, ct);
            }
            else
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
            }
        }
    }
}

