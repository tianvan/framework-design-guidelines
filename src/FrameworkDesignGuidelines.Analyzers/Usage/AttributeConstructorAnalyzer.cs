using System.Collections.Immutable;
using FrameworkDesignGuidelines.Analyzers.Exceptions;
using FrameworkDesignGuidelines.Analyzers.Naming;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Usage;

/// <summary>
/// FDG8011 (section 8.2): a non-abstract class derived from <c>System.Attribute</c> that declares more than one
/// constructor an attribute application can call, that is one that is neither private, protected nor private
/// protected.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AttributeConstructorAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG8011);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            INamedTypeSymbol? attribute = start.Compilation.GetTypeByMetadataName("System.Attribute");
            start.RegisterSymbolAction(
                symbolContext =>
                {
                    if (symbolContext.Symbol is INamedTypeSymbol { TypeKind: TypeKind.Class, IsAbstract: false } type
                        && ExceptionSymbols.DerivesFrom(type, attribute)
                        && type.InstanceConstructors.Count(constructor => !constructor.IsImplicitlyDeclared
                            && constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal
                                or Accessibility.ProtectedOrInternal) > 1
                        && NamingSymbols.SourceLocation(type) is { } location)
                    {
                        symbolContext.ReportDiagnostic(
                            Diagnostic.Create(FdgDescriptors.FDG8011, location, UsageSymbols.Display(type)));
                    }
                },
                SymbolKind.NamedType);
        });
    }
}
