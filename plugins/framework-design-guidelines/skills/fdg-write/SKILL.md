---
name: fdg-write
description: >-
  Framework Design Guidelines (3rd ed.) for C# and .NET. Use this skill whenever writing or modifying C# code: adding
  or changing a class, interface, struct, enum, member, method, property, event, field or parameter, naming anything,
  designing a public API or a library, or throwing an exception. It gives the book's rules that analyzers cannot
  check, quoted verbatim with their FDG ids, and tells you which reference file to read for the rest.
paths: "**/*.cs"
---

# Writing C# by the Framework Design Guidelines

Scope: these rules apply to SDK-style projects that set `FrameworkDesignGuidelinesProfile` (library, application or
test). The session context names them; without it, look for the property in the project file or a
`Directory.Build.props` above it, or for the project's `obj/fdg/marker.json`. In any other project, follow the
surrounding code's existing style instead, and you may suggest `/fdg-setup` to the user once.

Inside that scope, apply these rules while you write or change C# code, not afterwards. There, analyzers,
`dotnet format` and the end-of-turn check fix layout and check casing, modifiers and the other mechanical rules; the
rules here are the ones only you can apply. Each rule is quoted from the book with its id. DO is a rule to always
follow, CONSIDER one to generally follow, AVOID one to generally not follow, and DO NOT one to almost never break.
PERMITTED, NOT PERMITTED and CONTEXTUAL are appendix D's verdicts on changing an already shipped public API, with the
kinds of break (runtime, compilation, recompile, reflection) the change can cause.

A public name cannot change later without breaking callers, so choose names carefully before you write the member.

## When to use a reference

| When you are about to | Read |
|---|---|
| Plan a new public API or feature area: its scenarios and code samples, its entry points, its layers, or its documentation | [references/ch02-fundamentals.md](references/ch02-fundamentals.md) |
| Name an assembly, a namespace or a resource, add a new version of an existing API, name operator overload parameters, or name anything not covered by the top rules | [references/ch03-naming.md](references/ch03-naming.md) |
| Design a type: choose between a class, struct, interface or abstract class, or organize types into namespaces; design a static class, an interface, a struct, an enum, a nested type or a strongly typed string; or set assembly metadata | [references/ch04-types-4-1-to-4-4.md](references/ch04-types-4-1-to-4-4.md), [references/ch04-types-4-5-on.md](references/ch04-types-4-5-on.md) |
| Design members in general: member overloads, explicit interface implementations, or choose between a property and a method | [references/ch05-members-general.md](references/ch05-members-general.md) |
| Design a property, an indexer, a property change event, a constructor or a static constructor | [references/ch05-members-properties-constructors.md](references/ch05-members-properties-constructors.md) |
| Design an event, an event handler or a field | [references/ch05-members-events-fields.md](references/ch05-members-events-fields.md) |
| Design an extension method or an operator overload | [references/ch05-members-extensions-operators.md](references/ch05-members-extensions-operators.md) |
| Design a parameter: enum vs boolean, argument validation, ref, out, params, pointers, or use a tuple in a member signature | [references/ch05-members-parameters.md](references/ch05-members-parameters.md) |
| Design for extensibility: choose an extensibility mechanism (unsealed classes, protected members, events, callbacks, virtual members, abstractions, base classes) or decide when to seal | [references/ch06-extensibility.md](references/ch06-extensibility.md) |
| Throw, choose, catch, wrap or design an exception, write an exception message, or use the Tester-Doer or Try patterns | [references/ch07-exceptions.md](references/ch07-exceptions.md) |
| Use arrays, design a custom attribute, or choose, return or implement a collection in a public API | [references/ch08-usage-arrays-attributes-collections.md](references/ch08-usage-arrays-attributes-collections.md) |
| Use `DateTime`, `DateTimeOffset`, `ICloneable`, `IComparable<T>`, `IEquatable<T>`, `Nullable<T>`, `Uri` or `System.Xml` types; override `Equals`, `GetHashCode` or `ToString`; design a serializable type; or overload an equality operator | [references/ch08-usage-types.md](references/ch08-usage-types.md) |
| Design an aggregate component, a factored type, an asynchronous method (Task-Based Async Pattern, `IAsyncEnumerable<T>`), or a dependency property | [references/ch09-patterns-aggregate-async.md](references/ch09-patterns-aggregate-async.md) |
| Implement the Dispose Pattern, a finalizer, a scoped operation or `IAsyncDisposable` | [references/ch09-patterns-dispose.md](references/ch09-patterns-dispose.md) |
| Design a factory or support LINQ | [references/ch09-patterns-factories-linq.md](references/ch09-patterns-factories-linq.md) |
| Apply the Optional Feature Pattern, covariance or contravariance | [references/ch09-patterns-optional-variance.md](references/ch09-patterns-optional-variance.md) |
| Apply the Template Method Pattern, design a timeout or a XAML readable type | [references/ch09-patterns-template-timeouts-xaml.md](references/ch09-patterns-template-timeouts-xaml.md) |
| Design an operation on buffers, a data transformation or a fixed-size write | [references/ch09-patterns-buffers.md](references/ch09-patterns-buffers.md) |
| Write a value to a buffer with the Try-Write Pattern, or report partial writes with `OperationStatus` | [references/ch09-patterns-buffers-try-write.md](references/ch09-patterns-buffers-try-write.md) |
| Change an already shipped public API | [references/appd-breaking-changes-d-1-to-d-8.md](references/appd-breaking-changes-d-1-to-d-8.md), [references/appd-breaking-changes-d-9-on.md](references/appd-breaking-changes-d-9-on.md) |
| Organize a source file, order members, sort using directives, or write comments | [references/appA-coding-style.md](references/appA-coding-style.md) |

Each reference lists, by section, every rule of its chapter that tools do not enforce. When a rule's wording is not
enough, follow the `examples:` link at the end of its line to the book's explanation and code.

## Top rules

The rules most often needed while writing C#, most important first. For a decision they do not cover, read the
reference for its topic in the table above.

In `test` projects only rules from Chapter 7 (exceptions), Chapter 8 (usage guidelines) and Appendix A (coding
style) apply. The naming, type, member, extensibility and pattern rules are off there, so test names such as
`Method_WhenX_DoesY` are fine. In `application` projects the rules about a library's public surface are off; the
reference lines, and the top rules below, mark them "(library only)".

Appendix A is the .NET BCL team's style; the book calls it a set of suggestions, not requirements. Follow it unless
the repository's own `.editorconfig` says otherwise.

- **FDG3014** DO NOT use abbreviations or contractions as part of identifier names.
- **FDG3009** DO favor readability over brevity. The property name `CanScrollHorizontally` is better than `ScrollableX` (an obscure reference to the *X*-axis).
- **FDG3008** DO choose easily readable identifier names.
- **FDG7001** DO NOT return error codes.
- **FDG7042** DO throw `ArgumentException` or one of its subtypes if bad arguments are passed to a member. Prefer the most derived exception type, if applicable.
- **FDG7041** DO throw an `InvalidOperationException` if the object is in an inappropriate state.
- **FDG5106** DO NOT use Boolean parameters unless you are absolutely sure there will never be a need for more than two values.
- **FDG3065** DO give methods names that are verbs or verb phrases.
- **FDG3069** DO name Boolean properties with an affirmative phrase (`CanSeek` instead of `CantSeek`). Optionally, you can also prefix Boolean properties with “Is,” “Can,” or “Has,” but only where it adds value.
- **FDG3081** DO use descriptive parameter names.
- **FDG7004** DO NOT use exceptions for the normal flow of control, if possible. Prefer Tester–Doer or the Try pattern to throwing exceptions in common cases.
- **FDG7029** DO NOT overcatch. Exceptions should often be allowed to propagate up the call stack.
- **FDG7012** DO NOT create new exception types to communicate usage errors. Throw one of the existing base library exceptions instead. See section 7.3 for detailed usage guidelines for the most common standard exception types.
- **FDG4034** DO use an enum to strongly type parameters, properties, and return values that represent sets of values.
- **FDG5100** DO use the least derived parameter type that provides the functionality required by the member.
- **FDG8001** DO prefer using collections over arrays in public APIs. Section 8.3.3 provides details about how to choose between collections and arrays. (library only)
- **FDG8013** DO NOT use weakly typed collections in public APIs. (library only)
- **FDG9018** DO implement new asynchronous API using the Task-Based Async Pattern.
- **FDG9044** DO throw execution error exceptions via the Task-like value returned from an Async method.
- **FDG6011** DO NOT make members virtual unless you have a good reason to do so and you are aware of all the costs related to designing, testing, and maintaining virtual members.
- **FDG6003** DO treat protected members in unsealed classes as public for the purpose of security, documentation, and compatibility analysis. (library only)
- **FDG4025** DO NOT add members to an interface that has previously shipped. (library only)
- **FDG5048** DO minimal work in the constructor.
- **FDG5026** DO create get-only properties if the caller should not be able to change the value of the property.
- **FDG5030** DO preserve the previous value if a property setter throws an exception.
- **FDG3038** DO name classes and structs with nouns or noun phrases, using PascalCasing.
- **FDG4001** DO ensure that each type is a well-defined set of related members, not just a random collection of unrelated functionality.
- **FDG4017** DO NOT treat static classes as a miscellaneous bucket.
- **FDG4036** DO NOT use an enum for open sets (such as the operating system version, names of your friends, etc.).
- **FDG4052** DO NOT use public nested types as a logical grouping construct; use namespaces for this purpose. (library only)
- **FDG4030** DO ensure that a state where all instance data is set to zero, false, or null (as appropriate) is valid.
- **FDG5110** DO validate enum parameters.
- **FDG5029** DO allow properties to be set in any order, even if this results in a temporary invalid state of the object.
- **FDG5002** DO NOT arbitrarily vary parameter names in overloads. If a parameter in one overload represents the same input as a parameter in another overload, the parameters should have the same name.
- **FDG5006** DO NOT have overloads with parameters at the same position and similar types, yet with different semantics.
- **FDG5127** DO prefer descriptive types over tuples, `System.Tuple<T1, ...>`, and `System.ValueTuple<T1, ...>`; they improve discoverability and can better evolve over time. (library only)
- **FDG5069** AVOID frivolously defining extension methods, especially on types outside your library or framework.
- **FDG5090** DO NOT provide an implicit conversion operator if the conversion is potentially lossy.
- **FDG6014** DO NOT provide abstractions unless they are tested by developing several concrete implementations and APIs consuming the abstractions.
- **FDG6008** DO use the `Func<...>`, `Action<...>`, or `Expression<...>` types instead of custom delegates when possible, when defining APIs with callbacks.
- **FDG8022** DO use `ReadOnlyCollection<T>`, a subclass of `ReadOnlyCollection<T>`, or in rare cases `IEnumerable<T>` for properties or return values representing read-only collections.
- **FDG8027** DO NOT return snapshot collections from properties. Properties should return live collections.
- **FDG8044** DO use `DateTimeOffset` whenever you are referring to an exact point in time. For example, use it to calculate “now,” transaction times, file change times, logging event times, and so on. If the time zone is not known, use it with UTC. These usages are much more common than the scenarios where `DateTime` is preferred, so this should be considered the default.
- **FDG8072** DO ensure that if the `Object.Equals` method returns true for any two objects, `GetHashCode` returns the same value for these objects.
- **FDG8084** DO ensure that `ToString` has no observable side effects.
- **FDG9026** DO NOT return Tasks for long-running synchronous methods.
- **FDG9025** CONSIDER accepting a `CancellationToken` in long-running or blocking synchronous methods.
- **FDG9070** DO throw an `ObjectDisposedException` from any member that cannot be used after the object has been disposed of.
- **FDG9068** DO allow the `Dispose(bool)` method to be called more than once. The method might choose to do nothing after the first call.
- **FDG9084** DO prefer constructors to factories, because they are generally more usable, consistent, and convenient than specialized construction mechanisms.
- **FDG9105** DO defer execution of query operator implementations. The expected behavior of most of the Query Pattern members is that they simply construct a new object which, upon enumeration, produces the elements of the set that match the query.
- **FDG7030** DO use try-finally and avoid using try-catch for cleanup code. In well-written exception code, try-finally is far more common than try-catch.
- **FDG7023** DO NOT disclose security-sensitive information in exception messages.
- **FDG7065** DO pick one kind of reason for why your Try method can return `false`, and throw an exception for any other failure reason.
- **FDG2027** DO provide strongly typed APIs if at all possible.
- **FDGA050** DO use `if`…`throw` instead of assignment–expression–throw for argument validation.
- **FDGA041** DO use language keywords (`string`, `int`, `double`, …) instead of BCL type names (`String`, `Int32`, `Double`, …), for both variable declarations and method invocation.
- **FDGA059** DO NOT use comments unless they describe something not obvious to someone other than the developer who wrote the code.
- **FDGA064** DO NOT have more than one public type in a source file, unless they differ only in the number of generic parameters or one is nested in the other.
- **FDGA040** DO NOT use `this.` unless absolutely necessary.

## Language version

The project's C# language version and target frameworks are given in the session context; take them as the truth.
If they are missing, read `langVersion` and `targetFramework` from the project's `obj/fdg/marker.json`, which also
resolves the default version when the project sets none (C# 7.3 for .NET Framework and .NET Standard 2.0); without a
marker, read `LangVersion` and `TargetFramework(s)` from the project file. A rule tagged `[C# N+]` needs C# N or
later: when the project's version is lower, write the `(alt: ...)` form instead. Never raise the language version
and never add polyfills to follow a rule.
