using Microsoft.CodeAnalysis;

namespace Fdg.Generator.Sdk;

/// <summary>A diagnostic that one of the installed SDK's C# analyzers can report.</summary>
public sealed record SdkDiagnostic(string Id, string Title, DiagnosticSeverity DefaultSeverity, bool HasCodeFix);
