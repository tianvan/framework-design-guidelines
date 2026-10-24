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

A public name cannot change later without breaking callers, so choose names carefully before you write the member.
In `test` projects the Chapter 3 rules are off (test names such as `Method_WhenX_DoesY` are fine); Appendix A applies.

## Naming (Chapter 3)

- **FDG3014** DO NOT use abbreviations or contractions as part of identifier names.
- **FDG3008** DO choose easily readable identifier names.
- **FDG3009** DO favor readability over brevity. The property name `CanScrollHorizontally` is better than `ScrollableX` (an obscure reference to the *X*-axis).
- **FDG3015** DO NOT use any acronyms that are not widely accepted, and even if they are, only when necessary.
- **FDG3011** DO NOT use Hungarian notation.
- **FDG3035** DO NOT give types names that would conflict with any type in the core namespaces.
- **FDG3038** DO name classes and structs with nouns or noun phrases, using PascalCasing.
- **FDG3039** DO name interfaces with adjective phrases, or occasionally with nouns or noun phrases.
- **FDG3043** DO ensure that the names differ only by the “I” prefix on the interface name when you are defining a class–interface pair where the class is a standard implementation of the interface.
- **FDG3041** CONSIDER ending the name of derived classes with the name of the base class.
- **FDG3044** DO name generic type parameters with descriptive names unless a single-letter name is completely self-explanatory and a descriptive name would not add value.
- **FDG3060** DO use a singular type name for an enumeration unless its values are bit fields.
- **FDG3061** DO use a plural type name for an enumeration with bit fields as values, also called flags enum.
- **FDG3065** DO give methods names that are verbs or verb phrases.
- **FDG3066** DO name properties using a noun, noun phrase, or adjective.
- **FDG3069** DO name Boolean properties with an affirmative phrase (`CanSeek` instead of `CantSeek`). Optionally, you can also prefix Boolean properties with “Is,” “Can,” or “Has,” but only where it adds value.
- **FDG3070** CONSIDER giving a property the same name as its type.
- **FDG3071** DO name events with a verb or a verb phrase.
- **FDG3072** DO give events names with a concept of before and after, using the present and past tenses, such as `Closing` and `Closed`.
- **FDG3078** DO name fields using a noun, noun phrase, or adjective.
- **FDG3081** DO use descriptive parameter names.
- **FDG3082** CONSIDER using names based on a parameter’s meaning rather than the parameter’s type.

Compound-word casing that analyzers cannot see (Table 3-2 in the book):

- **FDG3006** DO NOT capitalize each word in so-called closed-form compound words.

## Coding style that tools cannot check (Appendix A.1.6)

- **FDGA050** DO use `if`…`throw` instead of assignment–expression–throw for argument validation.
- **FDGA041** DO use language keywords (`string`, `int`, `double`, …) instead of BCL type names (`String`, `Int32`, `Double`, …), for both variable declarations and method invocation.
- **FDGA040** DO NOT use `this.` unless absolutely necessary.
- **FDGA038** DO add the optional trailing comma after the last enum member.
- **FDGA039** DO add the optional trailing comma after a property assignment from an object initializer, or addition via a collection initializer, when the initializer spans multiple lines.
- **FDGA045** CONSIDER using expression-bodied members when the implementation of a property or method is unlikely to change. [C# 6+] (alt: Use a block body. Expression-bodied accessors and constructors, and throw expressions, need C# 7.0.)

## Comments, files and blocks (Appendix A)

- **FDGA006** DO NOT use the braceless variant of the `using` (dispose) statement.
- **FDGA059** DO NOT use comments unless they describe something not obvious to someone other than the developer who wrote the code.
- **FDGA062** AVOID placing comments at the end of a line even when the comment is very short.
- **FDGA064** DO NOT have more than one public type in a source file, unless they differ only in the number of generic parameters or one is nested in the other.
- **FDGA065** DO name the source file with the name of the public type it contains.
- **FDGA024** DO add a blank line before control flow statements.

Appendix A is the .NET BCL team's style; the book calls it a set of suggestions, not requirements. Follow it unless
the repository's own `.editorconfig` says otherwise.

## When to read a reference

| When you are about to | Read |
|---|---|
| Name an assembly, a namespace or a resource, add a new version of an existing API, name operator overload parameters, or name anything the rules above do not cover | [references/ch03-naming.md](references/ch03-naming.md) |
| Write comments, organize a source file, order members or sort using directives | [references/appA-coding-style.md](references/appA-coding-style.md) |

Other references:

- [references/ch02-fundamentals.md](references/ch02-fundamentals.md): design an API's main scenarios, entry points, layers and documentation
- [references/ch04-types-4-1-to-4-4.md](references/ch04-types-4-1-to-4-4.md): choose between a class, struct, interface or abstract class, and organize types into namespaces
- [references/ch04-types-4-5-on.md](references/ch04-types-4-5-on.md): design static classes, interfaces, structs, enums, nested types, assembly metadata and strongly typed strings
- [references/ch05-members-general.md](references/ch05-members-general.md): design member overloads, explicit interface implementations, and choose between a property and a method
- [references/ch05-members-properties-constructors.md](references/ch05-members-properties-constructors.md): design properties, indexers, property change events, constructors and static constructors
- [references/ch05-members-events-fields.md](references/ch05-members-events-fields.md): design events, event handlers and fields
- [references/ch05-members-extensions-operators.md](references/ch05-members-extensions-operators.md): design extension methods and operator overloads, including conversion and inequality operators
- [references/ch05-members-parameters.md](references/ch05-members-parameters.md): design parameters (enums versus booleans, argument validation, ref, out, params, pointers) and tuples in member signatures

Each reference lists, by section, every rule of its chapter that tools do not enforce. When a rule's wording is not
enough, follow the `examples:` link at the end of its line to the book's explanation and code.

## Language version

The project's C# language version and target frameworks are given in the session context; take them as the truth.
If they are missing, read `langVersion` and `targetFramework` from the project's `obj/fdg/marker.json`, which also
resolves the default version when the project sets none (C# 7.3 for .NET Framework and .NET Standard 2.0); without a
marker, read `LangVersion` and `TargetFramework(s)` from the project file. A rule tagged `[C# N+]` needs C# N or
later: when the project's version is lower, write the `(alt: ...)` form instead. Never raise the language version
and never add polyfills to follow a rule.
