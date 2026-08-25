using KUKULCAN.SharedKernel.DomainEvents.Abstractions;
using Spectre.Console;

namespace KUKULCAN.SharedKernel.Database.Client.Client;

/// <summary>Console dispatcher used by the reference client to observe domain-event dispatch.</summary>
public sealed class ConsoleDomainEventDispatcher : IDomainEventDispatcher
{
    /// <summary>Gets the number of events dispatched since process start.</summary>
    public int DispatchCount { get; private set; }

    /// <summary>Gets the last dispatched event, if any.</summary>
    public IDomainEvent? LastEvent { get; private set; }

    /// <inheritdoc />
    public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        cancellationToken.ThrowIfCancellationRequested();

        DispatchCount++;
        LastEvent = domainEvent;

        AnsiConsole.MarkupLine($"  [green]▶ DomainEvent dispatched:[/] [yellow]{domainEvent.GetType().Name}[/]");
        foreach (var property in domainEvent.GetType().GetProperties())
        {
            object? value = property.GetValue(domainEvent);
            AnsiConsole.MarkupLine($"    [grey]{property.Name}:[/] {value?.ToString()?.EscapeMarkup()}");
        }

        return Task.CompletedTask;
    }
}
