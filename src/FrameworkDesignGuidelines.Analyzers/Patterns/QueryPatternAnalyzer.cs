using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using FrameworkDesignGuidelines.Analyzers.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Patterns;

/// <summary>
/// FDG9100, FDG9103 and FDG9106 (section 9.6.5), the Query Pattern. A query method is an ordinary method that returns a
/// value, is named after a method of Figure 9-1 that takes a delegate (Where, Select, SelectMany, OrderBy, ThenBy,
/// SkipWhile, Join, or OrderByDescending and ThenByDescending), and has a parameter, other than the this parameter of
/// an extension method, of a delegate type or of <c>Expression&lt;TDelegate&gt;</c>. Overrides and interface
/// implementations take their name from another member and are not query methods here. FDG9100 reports a class,
/// struct or interface of any accessibility that declares an instance query method and does not implement
/// <c>IEnumerable&lt;T&gt;</c>. FDG9103 reports an instance or extension query method named OrderBy or
/// OrderByDescending, of any accessibility, that returns the type it is applied to (by original definition) or
/// <c>IEnumerable&lt;T&gt;</c>, unless that type is already an ordered sequence type (see <see cref="IsOrdered"/>).
/// FDG9106 reports an externally visible extension query method declared in a namespace
/// whose last segment is not Linq, the global namespace included.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QueryPatternAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> _queryNames = ImmutableHashSet.Create(
        "Where",
        "Select",
        "SelectMany",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "ThenByDescending",
        "SkipWhile",
        "Join");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG9100, FdgDescriptors.FDG9103, FdgDescriptors.FDG9106);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            INamedTypeSymbol? enumerable = start.Compilation.GetTypeByMetadataName(
                "System.Collections.Generic.IEnumerable`1");
            INamedTypeSymbol? expression = start.Compilation.GetTypeByMetadataName(
                "System.Linq.Expressions.Expression`1");
            if (enumerable is null)
            {
                return;
            }

            start.RegisterSymbolAction(
                symbolContext => AnalyzeType(symbolContext, enumerable, expression), SymbolKind.NamedType);
            INamedTypeSymbol?[] ordered =
            [
                start.Compilation.GetTypeByMetadataName("System.Linq.IOrderedEnumerable`1"),
                start.Compilation.GetTypeByMetadataName("System.Linq.IOrderedQueryable`1"),
            ];
            start.RegisterSymbolAction(
                symbolContext => AnalyzeMethod(symbolContext, enumerable, ordered, expression), SymbolKind.Method);
        });
    }

    private static void AnalyzeType(
        SymbolAnalysisContext context, INamedTypeSymbol enumerable, INamedTypeSymbol? expression)
    {
        if (context.Symbol is not INamedTypeSymbol
            {
                TypeKind: TypeKind.Class or TypeKind.Struct or TypeKind.Interface, IsImplicitlyDeclared: false,
            } type
            || UsageSymbols.Implements(type, enumerable)
            || NamingSymbols.SourceLocation(type) is not { } location)
        {
            return;
        }

        string? name = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => !method.IsStatic && IsQueryMethod(method, expression))
            .Select(method => method.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault();
        if (name is not null)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG9100, location, UsageSymbols.Display(type), name));
        }
    }

    private static void AnalyzeMethod(
        SymbolAnalysisContext context,
        INamedTypeSymbol enumerable,
        INamedTypeSymbol?[] ordered,
        INamedTypeSymbol? expression)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!IsQueryMethod(method, expression) || NamingSymbols.SourceLocation(method) is not { } location)
        {
            return;
        }

        string name = UsageSymbols.Display(method.ContainingType) + "." + method.Name;
        ITypeSymbol? source = method.IsExtensionMethod ? method.Parameters[0].Type
            : method.IsStatic ? null
            : method.ContainingType;
        if (source is not null
            && method.Name is "OrderBy" or "OrderByDescending"
            && !IsOrdered(source, method, ordered, expression)
            && (SymbolEqualityComparer.Default.Equals(method.ReturnType.OriginalDefinition, source.OriginalDefinition)
                || UsageSymbols.Is(method.ReturnType, enumerable)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                FdgDescriptors.FDG9103, location, name, UsageSymbols.Display(method.ReturnType)));
        }

        if (method.IsExtensionMethod
            && NamingSymbols.IsExternallyVisible(method)
            && method.ContainingNamespace is { } @namespace
            && (@namespace.IsGlobalNamespace || @namespace.Name != "Linq"))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG9106, location, name, @namespace.ToDisplayString()));
        }
    }

    /// <summary>
    /// True when <paramref name="source"/> is already an ordered sequence type: it implements
    /// <c>IOrderedEnumerable&lt;T&gt;</c> or <c>IOrderedQueryable&lt;T&gt;</c>, or a ThenBy or ThenByDescending query
    /// method applies to it, as an instance method of the type or a base type, or as an extension method of the class
    /// that declares <paramref name="method"/> whose this parameter has the same original definition.
    /// </summary>
    private static bool IsOrdered(
        ITypeSymbol source, IMethodSymbol method, INamedTypeSymbol?[] ordered, INamedTypeSymbol? expression)
    {
        if (ordered.Any(definition => UsageSymbols.Implements(source, definition)))
        {
            return true;
        }

        for (ITypeSymbol? current = source; current is not null; current = current.BaseType)
        {
            if (current.GetMembers().OfType<IMethodSymbol>().Any(candidate =>
                    !candidate.IsStatic && IsThenBy(candidate, expression)))
            {
                return true;
            }
        }

        return method.ContainingType.GetMembers().OfType<IMethodSymbol>().Any(candidate =>
            candidate.IsExtensionMethod
            && IsThenBy(candidate, expression)
            && SymbolEqualityComparer.Default.Equals(
                candidate.Parameters[0].Type.OriginalDefinition, source.OriginalDefinition));
    }

    private static bool IsThenBy(IMethodSymbol method, INamedTypeSymbol? expression)
    {
        return method.Name is "ThenBy" or "ThenByDescending" && IsQueryMethod(method, expression);
    }

    /// <summary>
    /// True for an ordinary method that returns a value, has the name of a query method that takes a delegate, takes
    /// a delegate or an expression of one, and is not an override or an interface implementation.
    /// </summary>
    private static bool IsQueryMethod(IMethodSymbol method, INamedTypeSymbol? expression)
    {
        return method is { MethodKind: MethodKind.Ordinary, ReturnsVoid: false, IsImplicitlyDeclared: false }
            && _queryNames.Contains(method.Name)
            && method.Parameters.Skip(method.IsExtensionMethod ? 1 : 0).Any(parameter =>
                parameter.Type.TypeKind == TypeKind.Delegate || UsageSymbols.Is(parameter.Type, expression))
            && !NamingSymbols.IsInherited(method);
    }
}
