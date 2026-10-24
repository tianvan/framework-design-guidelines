using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Extensibility;

/// <summary>
/// FDG6013: virtual members are protected, and public members provide extensibility by calling them. A virtual or
/// abstract method, property, indexer or event declared public in an externally visible class is reported.
/// Overrides take their accessibility from the overridden member and are not reported, nor are members that the
/// compiler declares, nor a record's <c>Equals(T)</c>, which C# requires to be public and virtual in an unsealed
/// record.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublicVirtualMemberAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG6013);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeMember, SymbolKind.Method, SymbolKind.Property, SymbolKind.Event);
    }

    private static void AnalyzeMember(SymbolAnalysisContext context)
    {
        ISymbol member = context.Symbol;
        if (member.ContainingType is not { TypeKind: TypeKind.Class } type
            || member.DeclaredAccessibility != Accessibility.Public
            || !(member.IsVirtual || member.IsAbstract)
            || member.IsOverride
            || member.IsImplicitlyDeclared
            || member is IMethodSymbol { MethodKind: not MethodKind.Ordinary }
            || IsRecordEquals(member, type)
            || !NamingSymbols.IsExternallyVisible(member)
            || NamingSymbols.SourceLocation(member) is not { } location)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG6013, location, member.Name));
    }

    /// <summary>The <c>Equals(T)</c> of a record T, which must be public and virtual unless T is sealed.</summary>
    private static bool IsRecordEquals(ISymbol member, INamedTypeSymbol type)
    {
        return type.IsRecord
            && member is IMethodSymbol { Name: nameof(Equals), ReturnType.SpecialType: SpecialType.System_Boolean }
                method
            && method.Parameters.Length == 1
            && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, type);
    }
}
