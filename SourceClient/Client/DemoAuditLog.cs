using KUKULCAN.SharedKernel.Database.Abstractions;

namespace KUKULCAN.SharedKernel.Database.Client.Client;

/// <summary>Demonstrates database-level immutable entity enforcement.</summary>
public sealed class DemoAuditLog : IImmutable
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Action { get; init; } = string.Empty;
    public string PerformedBy { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public DateTimeOffset PerformedAt { get; init; } = DateTimeOffset.UtcNow;
}