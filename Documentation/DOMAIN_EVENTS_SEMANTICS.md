# Domain Event Dispatch Semantics

`KUKULCAN.SharedKernel.Database` uses an **at-least-once dispatch** contract for domain events.

## Guarantees

- Events are never removed from their aggregate before successful dispatch.
- Pending events are acknowledged individually after successful dispatch.
- If event `B` fails after event `A` succeeds, `A` is not dispatched again on the next retry within the same `DbContext`; `B` remains pending.
- Explicit transactions dispatch events only after the database transaction has committed successfully.
- A dispatcher failure after commit does not roll the database transaction back.

## Consequence

The infrastructure does **not** provide durable exactly-once delivery. A dispatcher and downstream consumer must be idempotent across process crashes or other recovery boundaries. Durable exactly-once recovery requires an application-level outbox or equivalent persistent acknowledgement state.
