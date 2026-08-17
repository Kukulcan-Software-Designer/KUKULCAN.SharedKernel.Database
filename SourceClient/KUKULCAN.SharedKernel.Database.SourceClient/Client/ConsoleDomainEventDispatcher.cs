using KUKULCAN.SharedKernel.DomainEvents.Abstractions;
using Spectre.Console;

namespace KUKULCAN.SharedKernel.Database.Client.Client;

public sealed class ConsoleDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        cancellationToken.ThrowIfCancellationRequested();

        AnsiConsole.MarkupLine($"  [green]▶ DomainEvent dispatched:[/] [yellow]{domainEvent.GetType().Name}[/]");
        foreach (var property in domainEvent.GetType().GetProperties())
        {
            object? value = property.GetValue(domainEvent);
            AnsiConsole.MarkupLine($"    [grey]{property.Name}:[/] {value?.ToString()?.EscapeMarkup()}");
        }

        return Task.CompletedTask;
    }
}
