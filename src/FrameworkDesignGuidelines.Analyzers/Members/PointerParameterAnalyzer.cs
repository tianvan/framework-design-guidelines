using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// The pointer and varargs rules of sections 5.8, 5.8.4 and 5.8.5, over the externally visible members that declare
/// their own signature: FDG5102, no parameter is a multidimensional array, or a pointer, a function pointer or an
/// array of either unless an overload of the same name has no pointer parameters; FDG5123, no method or constructor
/// uses <c>__arglist</c>; FDG5124, a method or constructor with a pointer parameter has an overload of the same name
/// in its type (another constructor, for a constructor) without one.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PointerParameterAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5102, FdgDescriptors.FDG5123, FdgDescriptors.FDG5124);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeMember, SymbolKind.Method, SymbolKind.Property, SymbolKind.NamedType);
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeMember(SymbolAnalysisContext context)
    {
        ISymbol member = Signatures.Member(context.Symbol);
        if (!Signatures.IsDeclared(member))
        {
            return;
        }

        bool hasAlternative =
            member is IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.Constructor } overloaded
            && HasAlternative(overloaded);
        foreach (IParameterSymbol parameter in Signatures.Parameters(member))
        {
            if (((HasPointer(parameter.Type) && !hasAlternative) || HasMultidimensionalArray(parameter.Type))
                && NamingSymbols.SourceLocation(parameter) is { } location)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FdgDescriptors.FDG5102, location, parameter.Name, Signatures.Name(member)));
            }
        }

        if (member is IMethodSymbol { IsVararg: true } method
            && NamingSymbols.SourceLocation(method) is { } varargLocation)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG5123, varargLocation, Signatures.Name(method)));
        }
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        foreach (OverloadFamily family in Overloads.Families((INamedTypeSymbol)context.Symbol))
        {
            if (family.Methods.Any(method => !HasPointerParameter(method)))
            {
                continue;
            }

            for (int i = 0; i < family.Length; i++)
            {
                if (!family.IsInherited(i) && NamingSymbols.SourceLocation(family[i]) is { } location)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(FdgDescriptors.FDG5124, location, Overloads.DisplayName(family[i])));
                }
            }
        }
    }

    /// <summary>
    /// True when the overload family of <paramref name="method"/>, as FDG5124 sees it, has a member without pointer
    /// parameters: the alternative that the 5.8.5 introduction asks for when interoperability requires pointers.
    /// </summary>
    private static bool HasAlternative(IMethodSymbol method)
    {
        return Overloads.Families(method.ContainingType)
            .FirstOrDefault(family => family.Methods.Contains(method, SymbolEqualityComparer.Default))
            ?.Methods.Any(overload => !HasPointerParameter(overload)) == true;
    }

    private static bool HasPointerParameter(IMethodSymbol method)
    {
        return method.Parameters.Any(parameter => HasPointer(parameter.Type));
    }

    /// <summary>True for a pointer or function pointer type, or an array of them at any depth.</summary>
    private static bool HasPointer(ITypeSymbol type)
    {
        return type switch
        {
            IPointerTypeSymbol or IFunctionPointerTypeSymbol => true,
            IArrayTypeSymbol array => HasPointer(array.ElementType),
            _ => false,
        };
    }

    /// <summary>True for a multidimensional array, or a jagged array with one at any level.</summary>
    private static bool HasMultidimensionalArray(ITypeSymbol type)
    {
        return type is IArrayTypeSymbol array && (array.Rank > 1 || HasMultidimensionalArray(array.ElementType));
    }
}
