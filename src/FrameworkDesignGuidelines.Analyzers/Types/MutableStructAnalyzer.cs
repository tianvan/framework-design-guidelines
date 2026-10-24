using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Types;

/// <summary>
/// FDG4027: an externally visible struct that is not <c>readonly</c> exposes no instance field that is not
/// <c>readonly</c> and no instance property or indexer with a visible <c>set</c> accessor (<c>init</c> is allowed).
/// Methods that change the instance are not detected. Ref structs are left to FDG4031.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MutableStructAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG4027);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Struct || type.IsReadOnly || type.IsRefLikeType
            || !NamingSymbols.IsExternallyVisible(type))
        {
            return;
        }

        foreach (ISymbol member in type.GetMembers())
        {
            if (!member.IsStatic && !member.IsImplicitlyDeclared && IsMutator(member)
                && NamingSymbols.SourceLocation(member) is { } location)
            {
                string name = member is IPropertySymbol { IsIndexer: true } ? "this[]" : member.Name;
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG4027, location, type.Name, name));
            }
        }
    }

    private static bool IsMutator(ISymbol member)
    {
        return member switch
        {
            IFieldSymbol field => !field.IsReadOnly && !field.IsConst && NamingSymbols.IsExternallyVisible(field),
            IPropertySymbol { SetMethod: { IsInitOnly: false, IsReadOnly: false } setter } =>
                NamingSymbols.IsExternallyVisible(setter),
            _ => false,
        };
    }
}
