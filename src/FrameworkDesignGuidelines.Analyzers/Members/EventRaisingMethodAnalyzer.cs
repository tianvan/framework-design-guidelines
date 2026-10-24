using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Members;

/// <summary>
/// FDG5059: an externally visible instance event of an unsealed class has a protected virtual method, named "On" and
/// the event name, that raises it. FDG5060: a protected method named that way takes one parameter.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EventRaisingMethodAnalyzer : DiagnosticAnalyzer
{
    private const string Prefix = "On";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG5059, FdgDescriptors.FDG5060);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeEvent, SymbolKind.Event);
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeEvent(SymbolAnalysisContext context)
    {
        var @event = (IEventSymbol)context.Symbol;
        if (@event.IsStatic || @event.IsOverride || @event.IsAbstract
            || !@event.ExplicitInterfaceImplementations.IsEmpty
            || @event.ContainingType is not { TypeKind: TypeKind.Class, IsSealed: false, IsStatic: false } type
            || !NamingSymbols.IsExternallyVisible(@event)
            || NamingSymbols.SourceLocation(@event) is not { } location)
        {
            return;
        }

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(Prefix + @event.Name).OfType<IMethodSymbol>().Any(IsRaisingMethod))
            {
                return;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG5059, location, @event.Name));
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.MethodKind != MethodKind.Ordinary || method.IsOverride || method.Parameters.Length == 1
            || !IsProtected(method) || !NamingSymbols.IsExternallyVisible(method)
            || !method.Name.StartsWith(Prefix, StringComparison.Ordinal)
            || NamingSymbols.SourceLocation(method) is not { } location)
        {
            return;
        }

        string eventName = method.Name.Substring(Prefix.Length);
        for (INamedTypeSymbol? current = method.ContainingType; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(eventName).OfType<IEventSymbol>().Any())
            {
                context.ReportDiagnostic(Diagnostic.Create(FdgDescriptors.FDG5060, location, method.Name));
                return;
            }
        }
    }

    private static bool IsRaisingMethod(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Ordinary && IsProtected(method)
            && (method.IsVirtual || method.IsAbstract || method.IsOverride);
    }

    private static bool IsProtected(IMethodSymbol method)
    {
        return method.DeclaredAccessibility is Accessibility.Protected or Accessibility.ProtectedOrInternal;
    }
}
