using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// The parameter list rules of sections 5.8, 5.8.1 and 5.8.3, over the externally visible members that declare their
/// own signature: FDG5103, no out parameter comes before a parameter that is neither out nor a params array;
/// FDG5105, a member has fewer than two bool parameters other than out ones, unless it is a constructor whose bool
/// parameters are named after bool properties of its type; FDG5114, no reference type is passed by ref, in or ref
/// readonly, except ref parameters of a member with two or more ref parameters of the same type (the swap shape);
/// FDG5115, no value type is passed by in or ref readonly.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ParameterPassingAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            FdgDescriptors.FDG5103, FdgDescriptors.FDG5105, FdgDescriptors.FDG5114, FdgDescriptors.FDG5115);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeMember, SymbolKind.Method, SymbolKind.Property, SymbolKind.NamedType);
    }

    private static void AnalyzeMember(SymbolAnalysisContext context)
    {
        ISymbol member = Signatures.Member(context.Symbol);
        if (!Signatures.IsDeclared(member))
        {
            return;
        }

        ImmutableArray<IParameterSymbol> parameters = Signatures.Parameters(member);
        string name = Signatures.Name(member);
        AnalyzeOutOrder(context, parameters, name);
        AnalyzeBooleans(context, member, parameters, name);
        AnalyzeByReference(context, parameters, name);
    }

    private static void AnalyzeOutOrder(
        SymbolAnalysisContext context, ImmutableArray<IParameterSymbol> parameters, string name)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].RefKind == RefKind.Out
                && parameters.Skip(i + 1).Any(next => next.RefKind != RefKind.Out && !next.IsParams)
                && NamingSymbols.SourceLocation(parameters[i]) is { } location)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(FdgDescriptors.FDG5103, location, parameters[i].Name, name));
            }
        }
    }

    private static void AnalyzeBooleans(
        SymbolAnalysisContext context, ISymbol member, ImmutableArray<IParameterSymbol> parameters, string name)
    {
        IParameterSymbol[] booleans = parameters
            .Where(parameter => parameter.RefKind != RefKind.Out
                && parameter.Type.SpecialType == SpecialType.System_Boolean)
            .ToArray();
        if (booleans.Length < 2
            || (member is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
                && booleans.All(parameter => InitializesProperty(constructor.ContainingType, parameter)))
            || NamingSymbols.SourceLocation(member) is not { } location)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG5105, location, booleans.Length, name));
    }

    private static void AnalyzeByReference(
        SymbolAnalysisContext context, ImmutableArray<IParameterSymbol> parameters, string name)
    {
        foreach (IParameterSymbol parameter in parameters)
        {
            bool readOnly = parameter.RefKind is RefKind.In or RefKind.RefReadOnlyParameter;
            DiagnosticDescriptor? descriptor = parameter.Type switch
            {
                { IsReferenceType: true } when readOnly
                    || (parameter.RefKind == RefKind.Ref && !IsSwapped(parameters, parameter)) =>
                    FdgDescriptors.FDG5114,
                { IsValueType: true } when readOnly => FdgDescriptors.FDG5115,
                _ => null,
            };
            if (descriptor is not null && NamingSymbols.SourceLocation(parameter) is { } location)
            {
                context.ReportDiagnostic(Diagnostic.Create(descriptor, location, parameter.Name, name));
            }
        }
    }

    /// <summary>
    /// True when another ref parameter has the same type as the ref <paramref name="parameter"/>: the shape of a
    /// member that swaps references, the exception of the text.
    /// </summary>
    private static bool IsSwapped(ImmutableArray<IParameterSymbol> parameters, IParameterSymbol parameter)
    {
        return parameters.Any(other => !SymbolEqualityComparer.Default.Equals(other, parameter)
            && other.RefKind == RefKind.Ref
            && Overloads.SameType(other.Type, parameter.Type));
    }

    /// <summary>
    /// True when <paramref name="type"/> or one of its base types has a bool property with the name of
    /// <paramref name="parameter"/>, ignoring case: a constructor parameter that initializes a Boolean property.
    /// </summary>
    private static bool InitializesProperty(INamedTypeSymbol type, IParameterSymbol parameter)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers().OfType<IPropertySymbol>().Any(property =>
                    property.Type.SpecialType == SpecialType.System_Boolean
                    && string.Equals(property.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
