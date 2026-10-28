using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using FrameworkDesignGuidelines.Analyzers.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Patterns;

/// <summary>
/// FDG9047 and FDG9048 (section 9.2.10), the asynchronous forms of FDG8016 and FDG8017. FDG9047 reports a type that
/// is or implements <c>IAsyncEnumerator&lt;T&gt;</c> used as the type of a parameter, a return value (or the result
/// type of a returned <c>Task&lt;T&gt;</c> or <c>ValueTask&lt;T&gt;</c>), a property or indexer, or a field of an
/// externally visible member, except as the return type of a method named GetAsyncEnumerator. Uses as a type argument
/// are not reported, and neither are overrides and interface implementations, which take their signature from another
/// member. FDG9048 reports an externally visible class or struct that implements both
/// <c>IAsyncEnumerable&lt;T&gt;</c> and <c>IAsyncEnumerator&lt;T&gt;</c> when its base class does not already.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AsyncEnumeratorAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG9047, FdgDescriptors.FDG9048);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            var types = new AsyncTypes(start.Compilation);
            if (types.AsyncEnumerator is null)
            {
                return;
            }

            start.RegisterSymbolAction(
                symbolContext => AnalyzeMember(symbolContext, types),
                SymbolKind.Method,
                SymbolKind.Property,
                SymbolKind.Field);
            start.RegisterSymbolAction(symbolContext => AnalyzeType(symbolContext, types), SymbolKind.NamedType);
        });
    }

    private static void AnalyzeMember(SymbolAnalysisContext context, AsyncTypes types)
    {
        ISymbol member = context.Symbol;
        if (member.IsImplicitlyDeclared
            || NamingSymbols.IsInherited(member)
            || !NamingSymbols.IsExternallyVisible(member))
        {
            return;
        }

        string name;
        ImmutableArray<IParameterSymbol> parameters;
        ITypeSymbol? type;
        switch (member)
        {
            case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor:
                name = constructor.ContainingType.Name;
                parameters = constructor.Parameters;
                type = null;
                break;
            case IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion,
                } method:
                name = method.Name;
                parameters = method.Parameters;
                type = method.Name == "GetAsyncEnumerator" ? null : Result(method.ReturnType, types);
                break;
            case IPropertySymbol property:
                name = property.IsIndexer ? "this" : property.Name;
                parameters = property.Parameters;
                type = NamingSymbols.IsPositionalProperty(property) ? null : property.Type;
                break;
            case IFieldSymbol field:
                name = field.Name;
                parameters = ImmutableArray<IParameterSymbol>.Empty;
                type = field.Type;
                break;
            default:
                return;
        }

        foreach (IParameterSymbol parameter in parameters)
        {
            if (NamingSymbols.SourceLocation(parameter) is { } location)
            {
                Check(context, types, parameter.Type, location, name);
            }
        }

        if (type is not null && NamingSymbols.SourceLocation(member) is { } memberLocation)
        {
            Check(context, types, type, memberLocation, name);
        }
    }

    private static void Check(
        SymbolAnalysisContext context, AsyncTypes types, ITypeSymbol type, Location location, string member)
    {
        if (type is INamedTypeSymbol && UsageSymbols.Implements(type, types.AsyncEnumerator))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG9047, location, UsageSymbols.Display(type), member));
        }
    }

    private static void AnalyzeType(SymbolAnalysisContext context, AsyncTypes types)
    {
        if (context.Symbol is INamedTypeSymbol
            {
                TypeKind: TypeKind.Class or TypeKind.Struct, IsImplicitlyDeclared: false,
            } type
            && NamingSymbols.IsExternallyVisible(type)
            && IsCollectionAndEnumerator(type, types)
            && !IsCollectionAndEnumerator(type.BaseType, types)
            && NamingSymbols.SourceLocation(type) is { } location)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(FdgDescriptors.FDG9048, location, UsageSymbols.Display(type)));
        }
    }

    private static bool IsCollectionAndEnumerator(INamedTypeSymbol? type, AsyncTypes types)
    {
        return UsageSymbols.Implements(type, types.AsyncEnumerable)
            && UsageSymbols.Implements(type, types.AsyncEnumerator);
    }

    /// <summary>
    /// The result type of a <c>Task&lt;T&gt;</c> or <c>ValueTask&lt;T&gt;</c>, otherwise the type itself.
    /// </summary>
    private static ITypeSymbol Result(ITypeSymbol type, AsyncTypes types)
    {
        return type is INamedTypeSymbol { TypeArguments: { Length: 1 } arguments } named
            && (UsageSymbols.Is(named, types.TaskOfT) || UsageSymbols.Is(named, types.ValueTaskOfT))
            ? arguments[0]
            : type;
    }
}
