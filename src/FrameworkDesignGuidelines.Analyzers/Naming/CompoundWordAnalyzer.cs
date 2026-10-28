using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FrameworkDesignGuidelines.Analyzers.Naming;

/// <summary>
/// FDG3006: the names that externally visible declarations choose do not use a form of the "Not" column of the
/// book's Table 3-2, such as <c>FileName</c>, <c>CallBack</c> or <c>ID</c>; the message gives the form of the
/// "Pascal" column.
/// </summary>
/// <remarks>
/// A form matches a run of whole words (see <see cref="NamingSymbols.Words"/>), with the case of each word as the
/// table writes it, except the first letter of a name's first word, so <c>fileName</c> matches as the table's
/// <c>metaData</c> does. <c>Don't</c> cannot be an identifier. Footnote 1 makes the verb of <c>Logoff</c> two words,
/// so <c>LogOut</c> is not reported in a method name; footnote 2 says <c>LogIn</c> is never used, so it is reported
/// in every name.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CompoundWordAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The "Not" forms of Table 3-2, split into words, with the form the table uses instead.</summary>
    private static readonly (string[] Words, string Form, string Pascal)[] _forms =
    [
        Form("Bitflag", "BitFlag"),
        Form("CallBack", "Callback"),
        Form("Cancelled", "Canceled"),
        Form("EMail", "Email"),
        Form("EndPoint", "Endpoint"),
        Form("FileName", "Filename"),
        Form("GridLine", "Gridline"),
        Form("HashTable", "Hashtable"),
        Form("ID", "Id"),
        Form("Indices", "Indexes"),
        Form("LogOut", "Logoff"),
        Form("LogIn", "Logon"),
        Form("MetaData", "Metadata"),
        Form("MultiPanel", "Multipanel"),
        Form("MultiView", "Multiview"),
        Form("NameSpace", "Namespace"),
        Form("OK", "Ok"),
        Form("PI", "Pi"),
        Form("PlaceHolder", "Placeholder"),
        Form("SignOn", "SignIn"),
        Form("SignOff", "SignOut"),
        Form("TimeStamp", "Timestamp"),
        Form("UserName", "Username"),
        Form("Whitespace", "WhiteSpace"),
        Form("Writeable", "Writable"),
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(FdgDescriptors.FDG3006);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeSymbol, NamingSymbols.DeclarationKinds);
    }

    /// <summary>
    /// The "Not" forms in <paramref name="name"/>, each with the form Table 3-2 uses instead, cased for its place in
    /// the name. <paramref name="isMethod"/> is true for a method name, where <c>LogOut</c> is a verb.
    /// </summary>
    internal static IEnumerable<(string Found, string Instead)> Misspelled(string name, bool isMethod)
    {
        List<string> words = NamingSymbols.Words(name);
        for (int start = 0; start < words.Count; start++)
        {
            foreach ((string[] form, string text, string pascal) in _forms)
            {
                if ((isMethod && text == "LogOut") || !Matches(words, start, form))
                {
                    continue;
                }

                string found = string.Concat(words.Skip(start).Take(form.Length));
                bool isLower = start == 0 && char.IsLower(found[0]);
                yield return (found, isLower ? char.ToLowerInvariant(pascal[0]) + pascal.Substring(1) : pascal);
            }
        }
    }

    private static (string[] Words, string Form, string Pascal) Form(string form, string pascal)
    {
        return (NamingSymbols.Words(form).ToArray(), form, pascal);
    }

    private static bool Matches(List<string> words, int start, string[] form)
    {
        if (start + form.Length > words.Count)
        {
            return false;
        }

        for (int i = 0; i < form.Length; i++)
        {
            string word = words[start + i];
            string expected = form[i];
            bool same = start + i == 0
                ? word.Length == expected.Length
                    && char.ToUpperInvariant(word[0]) == expected[0]
                    && string.CompareOrdinal(word, 1, expected, 1, word.Length - 1) == 0
                : string.Equals(word, expected, StringComparison.Ordinal);
            if (!same)
            {
                return false;
            }
        }

        return true;
    }

    private static void AnalyzeSymbol(SymbolAnalysisContext context)
    {
        foreach ((ISymbol symbol, bool _) in NamingSymbols.DeclaredNames(context.Symbol))
        {
            if (NamingSymbols.SourceLocation(symbol) is not { } location)
            {
                continue;
            }

            bool isMethod = symbol is IMethodSymbol;
            foreach ((string found, string instead) in Misspelled(symbol.Name, isMethod))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(FdgDescriptors.FDG3006, location, symbol.Name, found, instead));
            }
        }
    }
}
