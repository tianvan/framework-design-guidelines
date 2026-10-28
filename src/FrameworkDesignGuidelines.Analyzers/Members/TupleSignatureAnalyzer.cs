using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// The tuple rules of section 5.9, over the signatures of the externally visible members that declare their own:
/// FDG5128, no unnamed tuple (<c>System.Tuple</c>, or a value tuple with an element without a name); FDG5129,
/// tuple element names start with an uppercase letter; FDG5130, no tuple has more than three elements; FDG5131, no
/// parameter type is or contains a tuple, except the <c>this</c> parameter of an extension method over a tuple;
/// FDG5132, no extension method extends a tuple. Tuples are found in type arguments and array element types too.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TupleSignatureAnalyzer : DiagnosticAnalyzer
{
    private const int MaxElements = 3;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            FdgDescriptors.FDG5128,
            FdgDescriptors.FDG5129,
            FdgDescriptors.FDG5130,
            FdgDescriptors.FDG5131,
            FdgDescriptors.FDG5132);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(
            AnalyzeMember,
            SymbolKind.Method,
            SymbolKind.Property,
            SymbolKind.Field,
            SymbolKind.Event,
            SymbolKind.NamedType);
    }

    private static void AnalyzeMember(SymbolAnalysisContext context)
    {
        ISymbol member = Signatures.Member(context.Symbol);
        if (!Signatures.IsDeclared(member) || NamingSymbols.SourceLocation(member) is not { } location)
        {
            return;
        }

        string name = Signatures.Name(member);
        ImmutableArray<IParameterSymbol> parameters = Signatures.Parameters(member);
        INamedTypeSymbol[] tuples = parameters.Select(parameter => parameter.Type)
            .Prepend(ValueType(member))
            .SelectMany(Tuples)
            .ToArray();

        if (tuples.FirstOrDefault(IsUnnamed) is { } unnamed)
        {
            context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG5128, location, Display(unnamed), name));
        }

        foreach (IFieldSymbol element in tuples.Where(tuple => tuple.IsTupleType)
            .SelectMany(tuple => tuple.TupleElements)
            .Where(element => element.IsExplicitlyNamedTupleElement && !char.IsUpper(element.Name[0])))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                FdgDescriptors.FDG5129, NamingSymbols.SourceLocation(element) ?? location, element.Name, name));
        }

        if (tuples.FirstOrDefault(tuple => ElementCount(tuple) > MaxElements) is { } large)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG5130, location, ElementCount(large), name));
        }

        bool overTuple = member is IMethodSymbol { IsExtensionMethod: true } && IsTuple(parameters[0].Type);
        if (overTuple)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG5132, location, name, Display(parameters[0].Type)));
        }

        foreach (IParameterSymbol parameter in parameters.Skip(overTuple ? 1 : 0))
        {
            if (Tuples(parameter.Type).Any() && NamingSymbols.SourceLocation(parameter) is { } parameterLocation)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(FdgDescriptors.FDG5131, parameterLocation, parameter.Name, name));
            }
        }
    }

    /// <summary>The type of a field, property or event, or the return type of a method.</summary>
    private static ITypeSymbol ValueType(ISymbol member)
    {
        return member switch
        {
            IMethodSymbol method => method.ReturnType,
            IPropertySymbol property => property.Type,
            IEventSymbol @event => @event.Type,
            _ => ((IFieldSymbol)member).Type,
        };
    }

    /// <summary>
    /// The tuples in <paramref name="type"/>: the type itself, its type arguments, its array element type and the
    /// element types of its tuples, at any depth, in that order.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> Tuples(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                foreach (INamedTypeSymbol tuple in Tuples(array.ElementType))
                {
                    yield return tuple;
                }

                break;
            case INamedTypeSymbol named:
                if (IsTuple(named))
                {
                    yield return named;
                }

                IEnumerable<ITypeSymbol> parts = named.IsTupleType
                    ? named.TupleElements.Select(element => element.Type)
                    : named.TypeArguments;
                foreach (INamedTypeSymbol tuple in parts.SelectMany(Tuples))
                {
                    yield return tuple;
                }

                break;
        }
    }

    /// <summary>True for a value tuple, named or not, or a <c>System.Tuple</c> class.</summary>
    private static bool IsTuple(ITypeSymbol type)
    {
        return type is INamedTypeSymbol named && (named.IsTupleType || IsSystemTuple(named));
    }

    private static bool IsSystemTuple(INamedTypeSymbol type)
    {
        return type is
        {
            Name: "Tuple",
            IsGenericType: true,
            ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true },
        };
    }

    private static bool IsUnnamed(INamedTypeSymbol tuple)
    {
        return !tuple.IsTupleType || tuple.TupleElements.Any(element => !element.IsExplicitlyNamedTupleElement);
    }

    /// <summary>
    /// The number of elements of a tuple; a <c>System.Tuple</c> with eight type arguments continues in its last one.
    /// </summary>
    private static int ElementCount(INamedTypeSymbol tuple)
    {
        if (tuple.IsTupleType)
        {
            return tuple.TupleElements.Length;
        }

        ImmutableArray<ITypeSymbol> arguments = tuple.TypeArguments;
        return arguments.Length == 8 && arguments[7] is INamedTypeSymbol rest && IsSystemTuple(rest)
            ? 7 + ElementCount(rest)
            : arguments.Length;
    }

    private static string Display(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }
}
