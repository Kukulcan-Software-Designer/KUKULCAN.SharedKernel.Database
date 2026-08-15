using KUKULCAN.SharedKernel.Abstractions;

namespace KUKULCAN.SharedKernel.Database.Client.Client;

public sealed class ConsoleDateTimeProvider : IClock
{
    private DateTimeOffset? _fixedTime;

    public DateTimeOffset UtcNow => _fixedTime ?? DateTimeOffset.UtcNow;

    public void FixTime(DateTimeOffset time) => _fixedTime = time;

    public void UseRealTime() => _fixedTime = null;
}
