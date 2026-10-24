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
