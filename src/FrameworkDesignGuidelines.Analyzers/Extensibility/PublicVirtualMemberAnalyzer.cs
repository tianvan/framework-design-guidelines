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
/// record, nor members of a class that cannot be derived from outside its assembly (every instance constructor is
/// internal, private or private protected).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PublicVirtualMemberAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Names an indexer with its parameter types, <c>this[int, string]</c>.</summary>
    private static readonly SymbolDisplayFormat _indexerFormat = SymbolDisplayFormat.MinimallyQualifiedFormat
        .WithMemberOptions(SymbolDisplayMemberOptions.IncludeParameters)
        .WithParameterOptions(SymbolDisplayParameterOptions.IncludeType);

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
            || !CanBeDerivedOutsideItsAssembly(type)
            || !NamingSymbols.IsExternallyVisible(member)
            || NamingSymbols.SourceLocation(member) is not { } location)
        {
            return;
        }

        string name = member is IPropertySymbol { IsIndexer: true }
            ? member.ToDisplayString(_indexerFormat)
            : member.Name;
        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG6013, location, name));
    }

    /// <summary>
    /// True when another assembly can declare a subclass: some instance constructor is public, protected or
    /// protected internal (the implicit default constructor is public, or protected in an abstract class).
    /// </summary>
    private static bool CanBeDerivedOutsideItsAssembly(INamedTypeSymbol type)
    {
        return type.InstanceConstructors.Any(constructor => constructor.DeclaredAccessibility
            is Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal);
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
