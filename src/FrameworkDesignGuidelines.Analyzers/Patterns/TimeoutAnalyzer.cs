using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using FrameworkDesignGuidelines.Analyzers.Usage;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Patterns;

/// <summary>
/// FDG9130 (section 9.10), timeouts as TimeSpan. It reports an externally visible parameter (of a method,
/// constructor, indexer or delegate) or property of an integer type whose name ends with the word Timeout, unless a
/// word of the name is a unit of time (<c>millisecondsTimeout</c>, <c>TimeoutMs</c>). Fields are not checked, and
/// overrides and interface implementations take their names and types from another member.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TimeoutAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> _units = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "Milliseconds",
        "Millisecond",
        "Ms",
        "Msec",
        "Microseconds",
        "Microsecond",
        "Seconds",
        "Second",
        "Secs",
        "Sec",
        "Minutes",
        "Minute",
        "Mins",
        "Hours",
        "Hour",
        "Days",
        "Day",
        "Ticks",
        "Tick");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG9130);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterSymbolAction(AnalyzeDelegate, SymbolKind.NamedType);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        if (property.IsImplicitlyDeclared
            || !NamingSymbols.IsExternallyVisible(property)
            || NamingSymbols.IsInherited(property))
        {
            return;
        }

        string owner = UsageSymbols.Display(property.ContainingType);
        if (!property.IsIndexer)
        {
            Report(context, property, property.Type, owner);
        }

        // The parameters of an indexer belong to the property, not to its accessors.
        foreach (IParameterSymbol parameter in property.Parameters)
        {
            Report(context, parameter, parameter.Type, owner + ".this[]");
        }
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method is not { MethodKind: MethodKind.Ordinary or MethodKind.Constructor, IsImplicitlyDeclared: false }
            || !NamingSymbols.IsExternallyVisible(method)
            || NamingSymbols.IsInherited(method))
        {
            return;
        }

        string owner = UsageSymbols.Display(method.ContainingType) + "."
            + (method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.Name);
        foreach (IParameterSymbol parameter in method.Parameters)
        {
            Report(context, parameter, parameter.Type, owner);
        }
    }

    /// <summary>
    /// The parameters of a delegate type, which belong to its implicitly declared Invoke method; the message names
    /// the delegate type.
    /// </summary>
    private static void AnalyzeDelegate(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type is not { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: { } invoke }
            || !NamingSymbols.IsExternallyVisible(type))
        {
            return;
        }

        foreach (IParameterSymbol parameter in invoke.Parameters)
        {
            Report(context, parameter, parameter.Type, UsageSymbols.Display(type));
        }
    }

    private static void Report(SymbolAnalysisContext context, ISymbol symbol, ITypeSymbol type, string owner)
    {
        if (!IsInteger(type)
            || !IsUnitlessTimeout(symbol.Name)
            || NamingSymbols.SourceLocation(symbol) is not { } location)
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(FdgDescriptors.FDG9130, location, symbol.Name, owner, UsageSymbols.Display(type)));
    }

    private static bool IsUnitlessTimeout(string name)
    {
        List<string> words = NamingSymbols.Words(name);
        return words.Count > 0
            && string.Equals(words[words.Count - 1], "Timeout", StringComparison.OrdinalIgnoreCase)
            && !words.Any(_units.Contains);
    }

    private static bool IsInteger(ITypeSymbol type)
    {
        return type.SpecialType is SpecialType.System_SByte
            or SpecialType.System_Byte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64;
    }
}
