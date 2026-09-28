using System.Collections.Concurrent;

namespace Mavrylo.Services;

public class AiAlertCooldown
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSentByOperation = new(StringComparer.OrdinalIgnoreCase);

    public bool TryClaim(string operationName, DateTimeOffset now)
    {
        while (true)
        {
            var lastSent = _lastSentByOperation.GetOrAdd(operationName, DateTimeOffset.MinValue);
            if (now - lastSent < Cooldown)
                return false;

            if (_lastSentByOperation.TryUpdate(operationName, now, lastSent))
                return true;
        }
    }
}
