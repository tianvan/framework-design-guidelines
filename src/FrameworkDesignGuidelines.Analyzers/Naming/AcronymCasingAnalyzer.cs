using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Naming;

/// <summary>
/// FDG3003 and FDG3004: the acronyms that chapter 3 itself names as acronyms (IO and UI; HTML, XML, RGB and ARGB) are
/// cased as the book shows, <c>IO</c> and <c>Html</c>, in the names that externally visible declarations choose.
/// </summary>
/// <remarks>
/// The first word of a camel-cased name (a parameter) is left to FDG3005. A word is an acronym only when it is the
/// whole word, so <c>Ratio</c> or <c>Xmlns</c> is not reported.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AcronymCasingAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] _twoCharacterAcronyms = ["IO", "UI"];

    private static readonly string[] _longerAcronyms = ["Html", "Xml", "Rgb", "Argb"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG3003, FdgDescriptors.FDG3004);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeSymbol, NamingSymbols.DeclarationKinds);
    }

    /// <summary>
    /// The acronyms in <paramref name="name"/> that are cased against the book, each with the rule it breaks and the
    /// casing the book uses.
    /// </summary>
    internal static IEnumerable<(DiagnosticDescriptor Rule, string Acronym)> Miscased(string name, bool isCamelCase)
    {
        List<string> words = NamingSymbols.Words(name);
        for (int i = isCamelCase ? 1 : 0; i < words.Count; i++)
        {
            string word = words[i];
            foreach (string acronym in _twoCharacterAcronyms)
            {
                if (IsMiscased(word, acronym))
                {
                    yield return (FdgDescriptors.FDG3003, acronym);
                }
            }

            foreach (string acronym in _longerAcronyms)
            {
                if (IsMiscased(word, acronym))
                {
                    yield return (FdgDescriptors.FDG3004, acronym);
                }
            }
        }
    }

    private static bool IsMiscased(string word, string acronym)
    {
        return string.Equals(word, acronym, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(word, acronym, StringComparison.Ordinal);
    }

    /// <summary>
    /// The name without the <c>I</c> of an interface (FDG3042) or the <c>T</c> of a type parameter (FDG3046), which
    /// would otherwise run into an acronym that follows it, as in <c>IHTMLWriter</c>.
    /// </summary>
    private static string WithoutPrefix(ISymbol symbol)
    {
        char prefix = symbol switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Interface } => 'I',
            ITypeParameterSymbol => 'T',
            _ => '\0',
        };
        string name = symbol.Name;
        return name.Length > 1 && name[0] == prefix && char.IsUpper(name[1]) ? name.Substring(1) : name;
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        foreach ((ISymbol symbol, bool isCamelCase) in NamingSymbols.DeclaredNames(context.Symbol))
        {
            if (NamingSymbols.SourceLocation(symbol) is not { } location)
            {
                continue;
            }

            foreach ((DiagnosticDescriptor rule, string acronym) in Miscased(WithoutPrefix(symbol), isCamelCase))
            {
                context.ReportDiagnostic(Diagnostic.Create(rule, location, symbol.Name, acronym));
            }
        }
    }
}
