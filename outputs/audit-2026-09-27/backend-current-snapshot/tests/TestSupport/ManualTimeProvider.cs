namespace Mavrylo.Tests.TestSupport;

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

    public void Set(DateTimeOffset utcNow) => _utcNow = utcNow;
}
