using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5005: two overloads declared in the same type do not differ only in the <c>ref</c>, <c>out</c>, <c>in</c> or
/// <c>ref readonly</c> modifiers of their parameters. The parameter with the modifier is reported, unless its method
/// overrides or implements another member.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RefKindOverloadAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5005);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        foreach (ImmutableArray<IMethodSymbol> family in Overloads.Families((INamedTypeSymbol)context.Symbol))
        {
            var reported = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
            for (int i = 0; i < family.Length; i++)
            {
                for (int j = i + 1; j < family.Length; j++)
                {
                    if (!DifferOnlyInModifiers(family[i], family[j]))
                    {
                        continue;
                    }

                    for (int k = 0; k < family[i].Parameters.Length; k++)
                    {
                        IParameterSymbol first = family[i].Parameters[k];
                        IParameterSymbol second = family[j].Parameters[k];
                        if (first.RefKind == second.RefKind)
                        {
                            continue;
                        }

                        IParameterSymbol modified = second.RefKind != RefKind.None ? second : first;
                        var method = (IMethodSymbol)modified.ContainingSymbol;
                        if (!NamingSymbols.IsInherited(method) && reported.Add(modified)
                            && NamingSymbols.SourceLocation(modified) is { } location)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                FdgDescriptors.FDG5005,
                                location,
                                Overloads.DisplayName(method),
                                Modifier(modified.RefKind),
                                modified.Name));
                        }
                    }
                }
            }
        }
    }

    private static bool DifferOnlyInModifiers(IMethodSymbol first, IMethodSymbol second)
    {
        if (first.Parameters.Length != second.Parameters.Length)
        {
            return false;
        }

        bool differ = false;
        for (int k = 0; k < first.Parameters.Length; k++)
        {
            if (!Overloads.SameType(first.Parameters[k].Type, second.Parameters[k].Type))
            {
                return false;
            }

            differ |= first.Parameters[k].RefKind != second.Parameters[k].RefKind;
        }

        return differ;
    }

    private static string Modifier(RefKind refKind)
    {
        return refKind switch
        {
            RefKind.Ref => "ref",
            RefKind.Out => "out",
            RefKind.In => "in",
            RefKind.RefReadOnlyParameter => "ref readonly",
            _ => refKind.ToString(),
        };
    }
}
