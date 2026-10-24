# Phase 2 spot-check list

For each chapter, about ten mappings picked for the user to check against the book: the hardest calls, rules with
exceptions, breaking rules and rules with new analyzers. Tick an entry when it has been checked.

## Chapter 2: Framework Design Fundamentals

- [ ] FDG2003 DO understand and design for the broad variety of programming languages.
  judgment; library only (application none). Chosen because CA1014 (CLSCompliant) is deliberately not mapped and the F# implicit-conversion example is left to the chapter 5 operator rules (CA2225).
- [ ] FDG2007 DO design APIs by first writing code samples for the main scenarios and then defining the object model to support the code samples.
  judgment; library and application warning. Chosen because a process rule is kept in the application profile (layer contracts), and the body spans two code blocks.
- [ ] FDG2011 DO organize usability studies to test APIs in main scenarios.
  judgment; library and application warning. Chosen because the cross-check moved it from library only to application: the book allows informal studies (2.2.1.1), which suit an application's layer contracts.
- [ ] FDG2013 DO provide simple overloads of constructors and methods. A simple overload has a very small number of parameters, and all parameters are primitives.
  judgment; library and application warning. Chosen because it looks machine-checkable but was not made an analyzer candidate (which members need a simple overload depends on the main scenarios). It has no body in the book.
- [ ] FDG2016 DO NOT require that users perform any extensive initialization before they can start programming basic scenarios.
  judgment; library and application warning. Chosen for its exception: if some initialization is necessary, the exception for skipping it must explain what to do (the hand-off to FDG2018 and FDG2026).
- [ ] FDG2017 DO provide good defaults for all properties and parameters (using convenience overloads), if possible.
  judgment; library and application warning. Chosen for its exception: defaults that lead the user astray (security holes, poor performance) must not be provided.
- [ ] FDG2018 DO communicate incorrect usage of APIs using exceptions.
  judgment. Chosen because CA2208 (argument exceptions without a message) is left to the chapter 7 rules instead of being mapped here.
- [ ] FDG2020 DO provide great documentation with all APIs.
  judgment; library and application warning. Chosen because the compiler warning CS1591 is related but is not an SDK analyzer diagnostic, so nothing is mapped.
- [ ] FDG2027 DO provide strongly typed APIs if at all possible.
  judgment. Chosen because it was not made an analyzer candidate: the body allows property bags when required, so object or string parameters cannot be reported without guessing intent.
- [ ] FDG2031 AVOID mixing low-level and high-level APIs in a single namespace if the low-level APIs are very complex (i.e., they contain many types).
  judgment; library and application warning. Chosen because the rule is conditional: mixing layers in one namespace is fine for simple features (2.2.4.2, the System.Text example), and only complex low-level APIs make it a violation.

## Chapter 4: Type Design Guidelines

- [ ] FDG4013 DO NOT define public or protected internal constructors in abstract types.
  fdg FDG4013 plus sdk CA1012, full; breaking. Chosen because a new analyzer covers the protected internal half (footnote 2) that CA1012 misses, and changing a constructor's accessibility changes the public API.
- [ ] FDG4019 DO declare static classes as sealed, abstract, and add a private instance constructor if your programming language does not have built-in support for static classes.
  sdk CA1052, partial; breaking. Chosen because the text is about languages without static classes, and in C# it is read as "declare the class static"; CA1052's fix changes the kind of the type.
- [ ] FDG4022 AVOID using marker interfaces (interfaces with no members).
  sdk CA1040, partial; suggestion in library and application. Chosen for its exception (a marker checked at compile time) and because noise lowered it from warning: the one report on the samples was such a marker.
- [ ] FDG4027 DO NOT define mutable value types.
  fdg FDG4027, partial; no autofix. Chosen for the new analyzer's scope: settable properties and writable fields of public structs are reported, methods that change the instance are not, and ref structs are left to FDG4031.
- [ ] FDG4032 DO implement `IEquatable<T>` on value types.
  fdg FDG4032, partial. Chosen because a new analyzer was written instead of mapping CA1815, which also asks for the equality operators, and because it reported 37 public structs on the samples (polly's argument structs that are never compared).
- [ ] FDG4038 AVOID publicly exposing enums with only one value.
  fdg FDG4038, full; library only. Chosen because the Morrison and Barton annotations argue for one-value handles, which the text does not make an exception.
- [ ] FDG4040 DO provide a value of zero on simple enums.
  sdk CA1008, partial; breaking. Chosen because CA1008 is shared with the flag enum rules FDG4048 and FDG4049, whose fix renames a value, so its fix is never applied automatically.
- [ ] FDG4044 DO apply the `System.FlagsAttribute` to flag enums. Do not apply this attribute to simple enums.
  sdk CA1027 and CA2217, partial; breaking; suggestion. Chosen because both diagnostics guess the kind of enum from its values (one CA1027 false positive on the samples), and adding or removing the attribute changes Enum.ToString for callers.
- [ ] FDG4053 AVOID publicly exposed nested types. The only exception to this guideline is if variables of the nested type need to be declared only in rare scenarios such as subclassing or other advanced customization scenarios.
  sdk CA1034, partial; library only. Chosen for its exception (rarely declared types, struct enumerators), which CA1034 cannot detect.
- [ ] FDG4070 AVOID creating overloads across `System.String` and a strongly typed string, unless the `System.String` overload was released in a previous version.
  judgment. Chosen because the book shows the check icon here and the strength word (AVOID) is used; its exception needs the shipped API.
