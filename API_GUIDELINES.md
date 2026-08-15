# API Design Guidelines

## Purpose

This document defines the official API design guidelines for **KUKULCAN.SharedKernel**.

Its purpose is to ensure that every public API exposed by the framework follows a consistent architectural style, remains maintainable over time and provides a predictable developer experience.

These guidelines apply to:

- Existing framework modules.
- Future framework modules.
- External libraries extending the Shared Kernel.
- Contributions submitted through Pull Requests.

Whenever a conflict exists between convenience and architectural consistency, **architectural consistency always takes precedence**.

---

# Design Principles

Every public API should follow the principles below.

## Simplicity

Public APIs should be easy to understand.

Complexity should remain inside the implementation rather than being exposed to consumers.

Prefer:

```csharp
Result<Customer>
```

instead of

```csharp
Tuple<bool, Customer?, string?>
```

---

## Explicitness

APIs should explicitly communicate their behavior.

Avoid hidden side effects.

Prefer:

```csharp
Result<Customer>
```

instead of

```csharp
Customer?
```

when failure is possible.

---

## Predictability

Methods performing similar operations should expose similar signatures.

Naming, return types and behavior should remain consistent throughout the framework.

---

## Consistency

Once a pattern has been adopted, it should be reused everywhere.

Avoid introducing multiple ways to solve the same problem.

---

# Naming Conventions

## Types

Use PascalCase.

Good examples:

```csharp
SemanticVersion

CustomerId

ValidationResult

SupportedCulture
```

Avoid abbreviations.

---

## Interfaces

Interfaces always begin with:

```text
I
```

Examples:

```csharp
IClock

ISpecification<T>

ICurrencyFormatter
```

---

## Methods

Methods should describe behavior.

Prefer:

```csharp
TryParse()

Validate()

Create()

Combine()
```

Avoid:

```csharp
Do()

Execute()

HandleStuff()
```

---

## Properties

Properties should represent state.

Methods should represent behavior.

---

# Result Pattern

Business operations should return **Result** whenever failure is expected.

Good:

```csharp
Result<Customer>
```

Bad:

```csharp
Customer?

bool

Tuple<bool, Customer>
```

---

## Success

```csharp
return Result.Success();
```

---

## Failure

```csharp
return Result.Failure(
    CommonErrors.NotFound(
        nameof(Customer),
        id));
```

---

# Maybe Pattern

Use **Maybe<T>** whenever an object may legitimately not exist.

Good:

```csharp
Maybe<Customer>
```

Avoid:

```csharp
Customer?
```

when null has business meaning.

---

# Exceptions

Exceptions should represent programming errors.

Do NOT use exceptions for:

- validation;
- business rules;
- expected failures.

Instead use:

- Result
- ValidationResult
- Maybe

Good:

```csharp
Result<Customer>
```

Bad:

```csharp
throw new InvalidOperationException();
```

for expected business conditions.

---

# Strongly Typed Identifiers

Primitive identifiers should never be exposed.

Prefer:

```csharp
CustomerId
```

instead of

```csharp
Guid
```

Prefer:

```csharp
OrderId
```

instead of

```csharp
int
```

---

# Value Objects

Value Objects should:

- be immutable;
- implement structural equality;
- never expose public setters.

Example:

```csharp
public sealed class Email
    : ValueObject
{
}
```

---

# Entities

Entities represent identity.

Identity should never change.

Entity behavior should be encapsulated.

---

# Aggregate Roots

Aggregate Roots protect consistency boundaries.

External code should not modify child entities directly.

---

# Specifications

Business rules that are reusable should become Specifications.

Avoid duplicating predicates.

Good:

```csharp
ActiveCustomerSpecification
```

instead of repeating:

```csharp
customer => customer.IsActive
```

throughout the codebase.

---

# Nullability

Nullable Reference Types are mandatory.

Avoid using the null-forgiving operator.

Prefer explicit validation.

---

# Immutability

Whenever possible:

- records;
- readonly collections;
- immutable state.

Mutability should be minimized.

---

# XML Documentation

Every public type must contain XML documentation.

Every public member should include:

- summary;
- parameter descriptions;
- return value;
- exception documentation when applicable.

---

# Public API Surface

Public APIs are contracts.

Every public type increases maintenance cost.

Prefer:

```csharp
internal
```

unless the type is intended for framework consumers.

---

# Dependencies

Public APIs should avoid exposing external library types.

Prefer framework abstractions.

---

# Versioning

Breaking changes are reserved for future major releases.

Minor releases should remain backward compatible.

Patch releases should never introduce API changes.

---

# Examples

## Good API

```csharp
public Result<Customer> Create(CustomerRegistration registration)
```

---

## Poor API

```csharp
public Customer? Create(
    string name,
    string email,
    bool throwOnError)
```

---

# Binary Compatibility

Public signatures should remain stable.

Avoid:

- renaming public members;
- removing public members;
- changing parameter order;
- changing generic constraints.

---

# Extension Methods

Extension methods should only exist when they provide significant usability improvements.

Avoid creating extension methods merely to reduce typing.

---

# Thread Safety

Unless explicitly documented otherwise, public APIs should be safe to use concurrently.

Shared mutable state should be avoided.

---

# Testing

Every public API should have corresponding unit tests.

Whenever possible, examples shown in the documentation should also compile as tests.

---

# Final Guideline

Every new public API should answer the following questions before being accepted:

- Is it necessary?
- Is it simple?
- Is it consistent?
- Is it discoverable?
- Is it testable?
- Is it maintainable?
- Will it still make sense in ten years?

If the answer to any of these questions is **no**, the API should be reconsidered before implementation.
