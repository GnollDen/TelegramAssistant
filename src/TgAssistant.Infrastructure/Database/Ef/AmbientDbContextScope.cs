using System.Threading;

namespace TgAssistant.Infrastructure.Database.Ef;

public static class AmbientDbContextScope
{
    private static readonly AsyncLocal<TgAssistantDbContext?> CurrentHolder = new();

    public static TgAssistantDbContext? Current => CurrentHolder.Value;

    public static IDisposable Enter(TgAssistantDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        var previous = CurrentHolder.Value;
        CurrentHolder.Value = dbContext;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly TgAssistantDbContext? _previous;
        private bool _disposed;

        public Scope(TgAssistantDbContext? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CurrentHolder.Value = _previous;
            _disposed = true;
        }
    }
}
