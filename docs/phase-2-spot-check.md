# Phase 2 spot-check list

For each chapter, about ten mappings picked for the user to check against the book: the hardest calls, rules with
exceptions, breaking rules and rules with new analyzers.

How to use it: check each entry against the book and its rule page (`docs/rules/<ID>.md`), then tick it (`- [x]`). When
an entry is wrong, leave it unticked and write what is wrong on an indented line below it, so it can be fixed as a
mapping change in phase 3.

| Chapter | Rules | Entries |
| --- | ---: | ---: |
| 2 Framework Design Fundamentals | 32 | 10 |
| 4 Type Design Guidelines | 70 | 10 |
| 5 Member Design | 133 | 12 |
| 6 Designing for Extensibility | 22 | 10 |
| 7 Exceptions | 70 | 11 |
| 8 Usage Guidelines | 107 | 11 |
| 9 Common Design Patterns | 175 | 26 |
| D Breaking Changes | 63 | 11 |
| Total | 672 | 101 |

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

## Chapter 5: Member Design (5.1 to 5.3)

- [ ] FDG5003 AVOID being inconsistent in the ordering of parameters in overloaded members. Parameters with the same name should appear in the same position in all overloads.
  fdg FDG5003, partial; breaking. Chosen because the analyzer leaves out cases that rules elsewhere in the book fix (CancellationToken last in 9.2.2, the common exception constructors of 7.4) on top of the `params` and `out` cases of the body, and because reordering public parameters is breaking with no code fix.
- [ ] FDG5022 DO provide a protected virtual member that offers the same functionality as the explicitly implemented member if the functionality is meant to be specialized by derived classes.
  sdk CA1033, partial; breaking. Chosen because the text is conditional (only if derived classes are meant to specialize it), which CA1033 cannot see, and its fix adds a member or unseals the type.
- [ ] FDG5035 AVOID indexers with parameter types other than `System.Int32`, `System.Int64`, `System.String`, `System.Range`, `System.Index`, or an enum, except on dictionary-like types.
  sdk CA1043, partial; no autofix. Chosen for its exception (dictionary-like types), which CA1043 cannot detect, and because CA1043 accepts `Object` and `Int16`, which the text does not list.
- [ ] FDG5046 DO use the same name for constructor parameters and a property if the constructor parameters are used just to set the property.
  fdg FDG5046, partial; no autofix. Chosen because the new analyzer decides "used just to set the property" from the syntax (one use, `P = p` or `P = p ?? throw`), and because renaming a public parameter breaks callers that use named arguments.

## Chapter 5: Member Design (5.4 to 5.7)

- [ ] FDG5057 DO use `System.EventHandler<T>` instead of manually creating new delegates to be used as event handlers.
  sdk CA1003, partial; breaking. Chosen because the body exempts new events in a feature area that already uses custom handlers, which CA1003 cannot see, and because replacing the delegate type of a public event is breaking with no code fix.
- [ ] FDG5059 DO use a protected virtual method to raise each event. This is applicable only to nonstatic events on unsealed classes—not to structs, sealed classes, or static events.
  fdg FDG5059, partial; no autofix; library only. Chosen because the new analyzer finds the raising method by the "On" plus event name convention of the body, and leaves out override, abstract and explicitly implemented events on top of the exceptions in the text.
- [ ] FDG5073 DO throw an `ArgumentNullException` when the `this` parameter in an extension method is `null`.
  sdk CA1062, partial; no autofix. Chosen because CA1062 reports only a dereferenced parameter, and also reports every other parameter of public methods, which belongs to argument validation in 5.8.2; on the noise samples none of its reports was on an extension method.
- [ ] FDG5075 CONSIDER naming the type that holds extension methods for its functionality—for example, use “Routing” instead of “[ExtendedType] Extensions.”
  fdg FDG5075, partial; breaking. Chosen because the analyzer reports only the "[ExtendedType]Extensions" pattern and lets "[Feature][ExtendedType]Extensions" pass, a reading of the text that removed 9 of 13 reports on the samples.
- [ ] FDG5076 DO NOT put extension methods in the same namespace as the extended type unless the intention is to add methods to interfaces, for generic type restriction, or for dependency management.
  fdg FDG5076, partial; breaking. Chosen for its three exceptions: interfaces and generic restriction are read from symbols, and dependency management is approximated by leaving out every extended type from another assembly.

## Chapter 5: Member Design (5.8 to 5.9)

- [ ] FDG5111 DO NOT use `Enum.IsDefined` for enum range checks.
  fdg FDG5111, partial; no autofix. Chosen because the analyzer narrows to calls on an argument that guard a throw, reading the Rector annotation (`Enum.IsDefined` is fine for asking whether a value is a defined one) as an exception.
- [ ] FDG5113 AVOID using `out` or `ref` parameters except when implementing patterns that require them, such as the Try pattern (section 7.5.2).
  sdk CA1021, partial; breaking. Chosen because CA1045 was dropped after the noise run: its 3 reports were a property change setter helper, a pattern that requires ref; ref parameters of value types are now unchecked.
- [ ] FDG5131 DO NOT use tuples as method parameters.
  fdg FDG5131, full; breaking; library only. Chosen because collections of tuples are reported (the body calls them more justifiable, yet better served by descriptive types), while type parameters substituted with tuples and interface implementations are not.

## Chapter 6: Designing for Extensibility

- [ ] FDG6008 DO use the `Func<...>`, `Action<...>`, or `Expression<...>` types instead of custom delegates when possible, when defining APIs with callbacks.
  judgment; no autofix. Chosen because the analyzer candidate was rejected: the Toub exceptions can be read from symbols, but the Kotas annotation accepts a custom delegate whose arguments need names, which is meaning.
- [ ] FDG6010 DO understand that by calling a delegate, you are executing arbitrary code, and that could have security, correctness, and compatibility repercussions.
  judgment; no autofix; kept in the application profile. Chosen because delegates invoked under a lock (the Pepin annotation) are not reported, since the Duffy annotation says that this is sometimes unavoidable.
- [ ] FDG6011 DO NOT make members virtual unless you have a good reason to do so and you are aware of all the costs related to designing, testing, and maintaining virtual members.
  judgment; no autofix; kept in the application profile. Chosen because the cross-check moved it out of library only: the body's costs (virtual calls are not inlined, and members that can be overridden in unpredictable ways are costly to design and test) apply to any code, as for FDG5004 and FDG6007.
- [ ] FDG6013 DO prefer protected accessibility over public accessibility for virtual members. Public members should provide extensibility (if required) by calling into a protected virtual member.
  fdg FDG6013, full; breaking; library only. Chosen because the new analyzer reports abstract members too, as virtual members, and leaves out only overrides, compiler-declared members and a record's Equals(T).
- [ ] FDG6014 DO NOT provide abstractions unless they are tested by developing several concrete implementations and APIs consuming the abstractions.
  judgment; no autofix. Chosen because it stays in the application profile, like FDG4015, FDG4023 and FDG4024, while most of the chapter is library only.
- [ ] FDG6017 CONSIDER making base classes abstract even if they don’t contain any abstract members. This clearly communicates to the users that the class is designed solely to be inherited from.
  judgment; no autofix; library only. Chosen because the Barton annotation still asks for a reason to make a class abstract, so no analyzer could report a concrete base class.
- [ ] FDG6019 AVOID naming base classes with a “Base” suffix if the class is intended for use in public APIs.
  fdg FDG6019, partial; breaking; library only. Chosen because the new analyzer reads "intended for use in public APIs" as a use in the signature (constraints included) of an externally visible member or type of the same compilation, so a class used only as a base class is not reported, and counts only base classes (abstract, with a protected or virtual member that is not an override, or derived from), not compound nouns such as KnowledgeBase.
- [ ] FDG6020 DO NOT seal classes without having a good reason to do so.
  judgment; no autofix; library only. Chosen because the body's list of good reasons is open ("include the following"), and because CA1813 and CA1852, which ask for sealing, are not mapped.
- [ ] FDG6021 DO NOT declare protected or virtual members on sealed types.
  judgment; no autofix. Chosen because the C# compiler enforces it (CS0628, CS0549, CS0666), so it needs no analyzer and stays in the application profile.
- [ ] FDG6022 CONSIDER sealing members that you override.
  judgment; no autofix; library only. Chosen because overrides are not reported: the dispose pattern of section 9.4 has subclasses override Dispose(bool) again.

## Chapter 7: Exceptions

- [ ] FDG7002 DO report execution failures by throwing exceptions.
  judgment; no autofix. Chosen because the book shows the cross icon on a DO guideline (Ruling 4), and the notes say that the strength word is used.
- [ ] FDG7007 DO NOT have public members that can either throw or not throw based on some option.
  fdg FDG7007, partial; breaking; library only. Chosen because the new analyzer reads "some option" as a bool parameter whose first word is "throw" (throwOnError), so enum or settings options are not seen.
- [ ] FDG7008 DO NOT have public members that return exceptions as the return value or an `out` parameter.
  fdg FDG7008, partial; breaking; library only. Chosen because the analyzer unwraps Task<T> and ValueTask<T> returns but does not see properties or generic returns constrained to Exception, and private exception builder methods (FDG7009) stay allowed.
- [ ] FDG7021 DO ensure that each sentence of the message text ends with a period.
  fdg FDG7021, partial; no autofix; test profile none. Chosen because the new analyzer checks only the last sentence of a constant or interpolated message (which ends with text), and leaves messages that end with ? or ! to FDG7022.
- [ ] FDG7025 DO NOT swallow errors by catching nonspecific exceptions, such as `System.Exception`, `System.SystemException`, and so on, in framework code.
  sdk CA1031, partial; no autofix; library only. Chosen because CA1031 also reports a catch that transfers the exception to another thread (TaskCompletionSource.SetException), which the body allows.
- [ ] FDG7036 DO specify the inner exception when wrapping exceptions.
  fdg FDG7036, partial; no autofix. Chosen because the new analyzer counts a type as able to wrap only when it has an accessible constructor with an Exception parameter, an omitted optional inner exception argument is still reported, and an exception passed in an array or a sequence (AggregateException) counts as the inner exception; it had 0 reports in the noise samples.
- [ ] FDG7040 DO NOT throw or derive from `System.ApplicationException`.
  sdk CA2201 and CA1058, full; breaking. Chosen because changing the base type of a public exception is breaking, which makes CA1058 a breaking diagnostic.
- [ ] FDG7044 DO use `value` for the name of the implicit value parameter of property setters.
  fdg FDG7044, partial; no autofix. Chosen because CA2208 accepts the property name as paramName in a setter (probe with SDK 10.0.402), so a new analyzer was needed instead of the SDK rule; a paramName that is not a constant and throw helpers are not seen.
- [ ] FDG7057 DO end exception class names with the “Exception” suffix.
  sdk CA1710, full; breaking; test profile none. Chosen because renaming a public type is breaking (Ruling 24); fix round 1 set the chapter 3 CA1710 rules, FDG3056 included, to breaking as well (Ruling 30).
- [ ] FDG7058 DO make exceptions runtime serializable. An exception must be serializable to work correctly across application domain and remoting boundaries.
  sdk CA2237, partial; autofix yes. Chosen because CA2237 reports only a type that lists ISerializable itself, and only on .NET Framework, where the reason of the text still applies; an analyzer for every exception type was rejected, since .NET 8 made the serialization constructor and GetObjectData obsolete (SYSLIB0051).
- [ ] FDG7064 DO use the prefix “Try” and Boolean return type for methods implementing the Try pattern.
  fdg FDG7064, partial; breaking. Chosen because the analyzer was narrowed after noise to Try methods with an out parameter, after 4 of 8 first-run reports were best-effort or fluent methods without one; 3 true reports that return a nullable value or a tuple are no longer seen.

## Chapter 8: Usage Guidelines

- [ ] FDG8002 DO remember that arrays are mutable types—even arrays of immutable types.
  fdg FDG8002, partial; breaking; test profile none. Chosen because the new analyzer reports only readonly array fields, as in the Path.InvalidPathChars example of the body; a method that hands out an internal array is not seen, and array properties are left to CA1819 (FDG8003). Fix round 1 stopped reporting a field initialized with an empty array, which has no elements to change, and set the test profile to none (Ruling 32).
- [ ] FDG8011 AVOID overloading custom attribute constructors.
  fdg FDG8011, full; breaking. Chosen because the analyzer counts only the constructors an attribute application can call (neither private, protected nor private protected), and polly's polyfill copies of framework attributes are true reports that only a suppression can fix.
- [ ] FDG8015 DO NOT use `Hashtable` or `Dictionary<TKey,TValue>` in public APIs.
  fdg FDG8015, partial; breaking; library only. Chosen because types derived from Hashtable or Dictionary are not reported, since the body allows a custom type implementing IDictionary, uses as a type argument are not seen, and Task<T> and ValueTask<T> results are unwrapped.
- [ ] FDG8026 DO NOT return null values from collection properties or from methods returning collections. Return an empty collection or an empty array instead.
  fdg FDG8026, partial; no autofix. Chosen because fix round 1 narrowed the analyzer to types of the System.Collections namespaces (and types derived from them) after 3 of 15 first-run reports were document tree nodes that implement IList<T>.
- [ ] FDG8027 DO NOT return snapshot collections from properties. Properties should return live collections.
  judgment; no autofix. Chosen because an analyzer for ToArray or ToList in a getter was rejected: FDG8028 allows a snapshot for a volatile collection, and whether a collection is volatile is design.
- [ ] FDG8053 DO implement `IEquatable<T>` on value types.
  fdg FDG8053, partial; no autofix. Chosen because ref structs, enumerator structs and (after fix round 1) structs without instance fields are left out, while polly's 34 structs, whose files already suppress CA1815 by pragma, stay true by the letter.
- [ ] FDG8067 DO override `Equals` on value types.
  sdk CA1815, partial; no autofix; suggestion in every profile. Chosen because CA1815 also reports the missing equality operators, which FDG8105 asks for only if equality is meaningful, and gave that report to a struct with only const fields, whose values are all equal (1 false positive of 2); CA1815 cannot be narrowed to its Equals message, so the rule became a suggestion.
- [ ] FDG8091 DO use `System.Uri` to represent URI and URL data.
  sdk CA1054, CA1055 and CA1056, partial; breaking; test profile none. Chosen because changing a string parameter, return value or property to Uri changes the public signature, which makes the three diagnostics breaking (Ruling 24); they had 95 true reports in the internal samples.
- [ ] FDG8096 DO NOT use `XmlNode` or `XmlDocument` to represent XML data. Instead, favor using instances of `IXPathNavigable`, `XmlReader`, `XmlWriter`, or subtypes of `XNode`. `XmlNode` and `XmlDocument` are not designed for exposing in public APIs.
  fdg FDG8096, partial; breaking; library only. Chosen because, unlike FDG8014 and FDG8015, types derived from XmlNode are reported too (XmlDocument and XmlElement are named or implied by the text), and the message drops the nullable annotation.
- [ ] FDG8104 AVOID defining `operator==` with arguments of different types.
  fdg FDG8104, full; breaking. Chosen because the analyzer also reports the operators of the OK examples of FDG8103; their labels rate the reflexive and transitive properties, and FDG8104's own body asks of such a set "Why do this much work?", so the reports stand and a type that keeps the operators suppresses them.
- [ ] FDG8105 DO overload the equality operators on value types, if equality is meaningful.
  sdk CA2231, partial; autofix yes. Chosen because CA2231 reports only a struct that overrides Equals, which is how "if equality is meaningful" is read, and its code fix adds the operators without changing an existing signature.

## Chapter 9: Common Design Patterns (9.1 to 9.3)

- [ ] FDG9020 DO use the suffix “Async” when naming an asynchronous method.
  fdg FDG9020, partial; breaking. Chosen because static methods that only return completed tasks are left out after 4 of polly's 6 first-run reports were completed-ValueTask factories (section 9.2.2 gives the suffix to the method that initiates an operation), and the book's own "WriteAync" example is reported.
- [ ] FDG9022 DO accept a `CancellationToken` parameter, named “cancellation-Token,” in asynchronous methods.
  fdg FDG9022, partial; breaking. Chosen because the name check reads the hyphen as a line-end break, an overload that takes a token excuses one that does not, and polly's methods that receive the token in a context object stay true by the letter.
- [ ] FDG9023 DO provide a default value to the `CancellationToken` value.
  fdg FDG9023, partial; no autofix. Chosen because overloads, a required parameter after the token and protected ...Core template methods are exempt, while newtonsoft-json's protected virtual methods without a default are kept as true (FDG5011 excepts `CancellationToken` from its ban on defaults).
- [ ] FDG9024 CONSIDER keeping the `CancellationToken` as the last parameter, for better alignment with a synchronous version of the method.
  sdk CA1068, partial; breaking. Chosen because CA1068 also reports synchronous methods, which FDG9026 excludes, and does not report a token followed by optional parameters.
- [ ] FDG9037 DO NOT return tasks in the `Created` state.
  fdg FDG9037, partial; no autofix. Chosen because the book shows the check icon and the strength word is used, and a constructor call with a `CancellationToken` is exempt after both first-run reports were a `Task.FromCanceled` polyfill whose task starts Canceled.
- [ ] FDG9039 DO use `await task.ConfigureAwait(false)` when awaiting an Async operation, except in application models that depend on the synchronization context.
  sdk CA2007, partial; no autofix; library only. Chosen because CA2007 does not report ConfigureAwait(true) (probe with SDK 10.0.402) and the application exception is left to the profile.
- [ ] FDG9043 DO throw usage error exceptions directly from the Async method, to aid in debuggability.
  fdg FDG9043, partial; no autofix. Chosen because only ArgumentException and its derived types (and their ThrowIf helpers) count as usage errors, since other exception types also report execution errors, and a throw that comes after an await or an await foreach in the text is not reported, since it can depend on the result of an asynchronous operation (FDG9044); an await using awaits only at disposal, so it counts only after its statement ends. The order is the text order, not control flow: a throw in the else branch of an if whose other branch awaits, or inside the operand of an await, is not reported.
- [ ] FDG9056 DO NOT set default values of dependency properties explicitly in code; instead, set them in metadata.
  fdg FDG9056, partial; no autofix. Chosen because only constant-like values set in an instance constructor are reported, and the constructor of the FDG9058 example, which the book labels "Do not do this!", is reported as intended.

## Chapter 9: Common Design Patterns (9.4 to 9.8)

- [ ] FDG9065 DO implement the `IDisposable` interface by simply calling `Dispose(true)` followed by `GC.SuppressFinalize(this)`.
  sdk CA1063, CA1816, partial; no autofix. Chosen because CA1063 and CA1816 cover the two calls between them, the reports both make on the Close pattern of FDG9072 are called out as not violations, internal-b's reports are one public type that misses GC.SuppressFinalize, and the notes say that a block-bodied Dispose() that only calls Dispose(true) is reported by CA1816 alone, so an internal type in the application profile that omits GC.SuppressFinalize this way is reported by neither.
- [ ] FDG9067 DO NOT declare any overloads of the `Dispose` method other than `Dispose()` and `Dispose(bool)`.
  fdg FDG9067, full; breaking. Chosen because the only allowed shapes are Dispose() and Dispose(bool) passed by value, static or not, an extern (P/Invoke) Dispose overload is reported too, since DllImport's EntryPoint lets it take another name, overrides and interface implementations are reported where the member is declared, and CA1063 did not report a Dispose(int) overload (probe with SDK 10.0.402), so FDG9067 is full.
- [ ] FDG9069 AVOID throwing an exception from within `Dispose(bool)` except under critical situations where the containing process has been corrupted (e.g., leaks, inconsistent shared state).
  sdk CA1065, partial; no autofix. Chosen because CA1065 sees only Dispose(), not the Dispose(bool) the text names (probe with SDK 10.0.402), and the critical situations the text excepts, like the throw of the IndentationScope example, stay a judgment.
- [ ] FDG9073 DO NOT make public types finalizable.
  fdg FDG9073, partial; no autofix; library only. Chosen because it is library only, because the text is about public types, and an inherited finalizer such as that of a SafeHandle subclass, the replacement the text recommends, is not reported.
- [ ] FDG9082 DO implement `DisposeAsync` as only calling and awaiting `Dispose-AsyncCore()`, calling `Dispose(false)`, and calling `GC.SuppressFinalize(this)`, in that order—except on sealed types.
  fdg FDG9082, partial; no autofix. Chosen because the Dispose(false) call is expected only when the class or a base class declares Dispose(bool), sealed classes and structs are excepted as the text says, the book's SomeType example is not reported, with or without ConfigureAwait, and CA1816 is listed for FDG9065 only, so that its report on DisposeAsync is attributed to one rule.
- [ ] FDG9100 DO implement `IEnumerable<T>` on types implementing the Query Pattern.
  fdg FDG9100, partial; no autofix. Chosen because only instance query methods that take a delegate or an expression count, so a type with Union or Take alone is not a Query Pattern type, and implementing IQueryable<T> counts as IEnumerable<T>.
- [ ] FDG9112 DO mark generic type parameters on generic interfaces as contravariant when the type parameter is only ever used as an input parameter to methods on the interface.
  fdg FDG9112, partial; breaking; library only. Chosen because the positions follow the compiler, except that constraints of the type's own type parameters count as both, unused and statically used type parameters are not reported, the members of nested interfaces and delegates count with the same positions (CS1961), an interface that declares a class, struct or enum, directly or in a nested interface, is not reported (CS8427), and the book's ICalculator<in T> and IFilter<out T> examples are not reported.
- [ ] FDG9117 DO NOT declare a generic type parameter covariant on an interface that returns an array based on the type parameter.
  fdg FDG9117, partial; breaking. Chosen because an array whose element uses a covariant type parameter is reported in any output position of a returned type, and FDG9116 counts such an array as both, so it never asks for the covariance that FDG9117 forbids.

## Chapter 9: Common Design Patterns (9.9 to 9.12)

- [ ] FDG9124 AVOID making public members virtual.
  judgment; library only (application none). Chosen because the rule is not mapped to an analyzer of its own although it is machine-checkable: FDG6013 already reports a virtual or abstract method, property, indexer or event declared public in an externally visible class, and a rule may map only its own FDG diagnostic, so the notes point to FDG6013 instead.
- [ ] FDG9130 DO prefer using `TimeSpan` to represent timeout time.
  fdg FDG9130, partial; breaking. Chosen because the body accepts an integer timeout whose name gives its unit, so millisecondsTimeout and msTimeout are not reported while timeout and ReadTimeout are (timeoutMs and timeoutInSeconds do not end with Timeout and are never reported), and the second condition of the body (a commonly used value small enough to need no calculator) is not checked.
- [ ] FDG9138 DO use `ReadOnlySpan<T>` instead of `Span<T>` where possible.
  fdg FDG9138, partial; breaking. Chosen because the read-only uses are an explicit list (Length, IsEmpty, element reads, a conversion to ReadOnlySpan<T>, CopyTo, TryCopyTo, ToArray, ToString, ten MemoryExtensions methods and foreach without ref), anything else counts as a possible write, a member access on a struct element counts as a write, and virtual methods keep their Span<T> parameters.
- [ ] FDG9142 CONSIDER returning `System.Range` representing the bounds of a Span parameter, rather than a slice of the parameter.
  fdg FDG9142, partial; breaking. Chosen because it is a CONSIDER rule reported only when every return is the parameter, whole or sliced, the TrimEnd examples of FDG9139 and FDG9147 are reported too, and nothing is reported without System.Range (.NET Standard 2.0 with System.Memory).
- [ ] FDG9147 AVOID overloading a method across `Span<T>` and `ReadOnlySpan<T>`, or `Memory<T>` and `ReadOnlyMemory<T>`.
  fdg FDG9147, partial; breaking. Chosen because the exception of the body is read as each overload returning the type of its own differing parameter (the TrimEnd pair), the report is on the writable method, and overloads that also differ in other parameters are not compared.
- [ ] FDG9149 CONSIDER adding Span- or Memory-based alternatives to methods that accept arrays or strings.
  sdk CA1844, partial; no autofix; library only. Chosen because CA1844 sees only Stream subclasses that override the array-based ReadAsync or WriteAsync, reports them only when the target framework has the Memory-based overloads (net10.0, not net472, probe with SDK 10.0.402), and other array or string methods are not seen.
- [ ] FDG9157 DO position the output buffer parameter (“destination”) as the first method parameter after the source parameter(s).
  fdg FDG9157, partial; breaking. Chosen because only a parameter named destination before one named source is reported: an input value of another type may come before the destination (the TryWriteGuid call of FDG9169), so other parameters before it are not reported.
- [ ] FDG9163 DO report the number of values written by a Try-Write method via an `out` parameter.
  fdg FDG9163, partial; breaking. Chosen because a Try-Write method that writes a fixed number of values, the shape of BinaryPrimitives.TryWriteUInt64LittleEndian that the FDG9169 example calls, is reported too, since the text asks every Try-Write method for the out parameter, and CA1021 does not report the out parameter of a bool Try method (probe with SDK 10.0.402).
- [ ] FDG9173 DO report the number of output values successfully written by an `OperationStatus` method via an `out` parameter, declared after the out parameter that reports the number of input values that were successfully processed.
  fdg FDG9173, partial; breaking. Chosen because the out parameters are told apart only by the suffixes Written and Consumed, a single out parameter is taken to be the written count unless it ends with Consumed, and FDG9171 reports the missing consumed count of the same methods.
- [ ] FDG9174 DO include a Boolean parameter named “isFinalBlock,” if an `OperationStatus` method needs to treat the last input block in a special way. The parameter should be optional and have a default value of true.
  fdg FDG9174, partial; breaking. Chosen because whether a method needs to treat the last block in a special way is not checked, so only a parameter named isFinalBlock without the default true, or a bool named with Final or Last, is reported, and the book's ToBase64 example is not reported for FDG9174. That example does get FDG9159 on charsWritten (expected bytesWritten for its Span<byte> destination), which the notes of FDG9159 take as a book error (Ruling 37).

## Appendix D: Breaking Changes

Every appendix D rule is judgment, library only (application and test none), autofix no, and its notes end with the
PublicApiAnalyzers sentence. Strength is the heading's verdict icon; context is its break icons.

- [ ] FDGD001 NOT PERMITTED: Changing the Name of an Assembly
  judgment; library warning. Chosen because its heading uses the legend's small cross icon (cross1) and all four break icons, and the notes keep the book's way out (a final version that type-forwards).
- [ ] FDGD004 PERMITTED: Moving a Type via [`TypeForwardedTo`]
  judgment; library suggestion. Chosen for its condition: permitted only when there is a technical reason for the move.
- [ ] FDGD019 PERMITTED: Changing a `ref struct` to a (Non-`ref`) `struct`
  judgment; library suggestion; context null. Chosen because the heading has no break icon and its title holds parentheses that must survive the icon stripping.
- [ ] FDGD020 NOT PERMITTED: Masking Base Members with `new`
  judgment; library warning. Chosen for its two exceptions (static Create methods that specialize the return type; new added to a method that predates the base member).
- [ ] FDGD022 PERMITTED: Adding Members to an Unsealed Type
  judgment; library suggestion. Chosen for its exception (not when the new member knowingly conflicts with a derived type) and the CS0108 warnings-as-errors break.
- [ ] FDGD034 CONTEXTUAL: Removing or Renaming Private Fields on Serializable Types
  judgment; library suggestion. Chosen as a contextual verdict: the notes state when it is not permitted (a serializer that reads private state).
- [ ] FDGD036 CONTEXTUAL: Adding Alternative-Parameter Overloads for a Reference Type Parameter
  judgment; library suggestion. Chosen because the verdict depends on whether the existing member rejects null; its body is one paragraph.
- [ ] FDGD053 CONTEXTUAL: Changing a Parameter from Optional to Required
  judgment; library suggestion. Chosen for the hand-off to FDG5016 (move the default values to the new, longer overload).
- [ ] FDGD055 NOT PERMITTED: Changing the Value of a `const` Field
  judgment; library warning. Chosen for the cross-reference to FDG5066 and the before-release advice (static readonly instead of const).
- [ ] FDGD060 PERMITTED: Changing Usage Error Exceptions to Functioning Behavior
  judgment; library suggestion. Chosen because the book gives no condition, so its notes are only the fixed PublicApiAnalyzers sentence.
- [ ] FDGD061 CONTEXTUAL: Changing the Type of Values Returned from a Method
  judgment; library suggestion. Chosen because the notes split the verdict: a more derived value is permitted, a switch that breaks hard-casting callers is not.
