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
/// A form matches a run of whole words (see <see cref="NamingSymbols.Words"/>) inside one part of a name between
/// digits or underscores, with the case of each word as the table writes it, except the first letter of a name's
/// first word, so <c>fileName</c> matches as the table's <c>metaData</c> does. The last word of a run may end with
/// a plural <c>s</c> or <c>es</c>, which the message keeps; an acronym plural such as <c>IDs</c> or <c>IDEs</c>,
/// which the splitter cuts before its last capital, is joined again first. <c>Don't</c> cannot be an identifier.
/// Footnote 1 makes the verb of <c>Logoff</c> and <c>Logon</c> two words and footnote 2 says <c>LogIn</c> is never
/// used, so <c>LogOut</c> and <c>LogIn</c> are reported in every name, and in a method name the message gives
/// <c>LogOff</c> and <c>LogOn</c>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CompoundWordAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The "Not" forms of Table 3-2, split into words, with the form the table uses instead and, for the verbs of
    /// footnote 1, the form a method name uses.
    /// </summary>
    private static readonly (string[] Words, string Pascal, string? Verb)[] _forms =
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
        Form("LogOut", "Logoff", "LogOff"),
        Form("LogIn", "Logon", "LogOn"),
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

    private static readonly string[] _pluralSuffixes = ["", "s", "es"];

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
    /// the name. <paramref name="isMethod"/> is true for a method name, where <c>LogOut</c> and <c>LogIn</c> are verbs.
    /// </summary>
    internal static IEnumerable<(string Found, string Instead)> Misspelled(string name, bool isMethod)
    {
        bool isFirstPart = true;
        foreach (string part in Parts(name))
        {
            List<string> words = JoinAcronymPlurals(NamingSymbols.Words(part));
            for (int start = 0; start < words.Count; start++)
            {
                bool isFirstWord = isFirstPart && start == 0;
                foreach ((string[] form, string pascal, string? verb) in _forms)
                {
                    if (Suffix(words, start, form, isFirstWord) is not { } suffix)
                    {
                        continue;
                    }

                    string found = string.Concat(words.Skip(start).Take(form.Length));
                    string instead = (isMethod && verb is not null ? verb : pascal) + suffix;
                    bool isLower = isFirstWord && char.IsLower(found[0]);
                    yield return (found, isLower ? char.ToLowerInvariant(instead[0]) + instead.Substring(1) : instead);
                }
            }

            isFirstPart = false;
        }
    }

    private static (string[] Words, string Pascal, string? Verb) Form(string form, string pascal, string? verb = null)
    {
        return (NamingSymbols.Words(form).ToArray(), pascal, verb);
    }

    /// <summary>The parts of <paramref name="name"/> between digits and underscores; a form never spans two.</summary>
    private static IEnumerable<string> Parts(string name)
    {
        int start = 0;
        for (int i = 0; i <= name.Length; i++)
        {
            if (i == name.Length || !char.IsLetter(name[i]))
            {
                if (i > start)
                {
                    yield return name.Substring(start, i - start);
                }

                start = i + 1;
            }
        }
    }

    /// <summary>
    /// Joins an all-capitals word with a following capital and <c>s</c>, which <see cref="NamingSymbols.Words"/> makes
    /// of an acronym plural: <c>I</c> and <c>Ds</c> become <c>IDs</c>, <c>ID</c> and <c>Es</c> become <c>IDEs</c>.
    /// </summary>
    private static List<string> JoinAcronymPlurals(List<string> words)
    {
        for (int i = words.Count - 2; i >= 0; i--)
        {
            string next = words[i + 1];
            if (next.Length == 2 && char.IsUpper(next[0]) && next[1] == 's' && words[i].All(char.IsUpper))
            {
                words[i] += next;
                words.RemoveAt(i + 1);
            }
        }

        return words;
    }

    /// <summary>
    /// The plural suffix the words at <paramref name="start"/> spell <paramref name="form"/> with (empty for none), or
    /// null if they do not spell it.
    /// </summary>
    private static string? Suffix(List<string> words, int start, string[] form, bool isFirstWord)
    {
        if (start + form.Length > words.Count)
        {
            return null;
        }

        for (int i = 0; i < form.Length - 1; i++)
        {
            if (!Same(words[start + i], form[i], isFirstWord && i == 0))
            {
                return null;
            }
        }

        int last = form.Length - 1;
        string word = words[start + last];
        string expected = form[last];
        foreach (string suffix in _pluralSuffixes)
        {
            if (word.Length == expected.Length + suffix.Length
                && word.EndsWith(suffix, StringComparison.Ordinal)
                && Same(word.Substring(0, expected.Length), expected, isFirstWord && last == 0))
            {
                return suffix;
            }
        }

        return null;
    }

    private static bool Same(string word, string expected, bool isFirstWord)
    {
        return isFirstWord
            ? word.Length == expected.Length
                && char.ToUpperInvariant(word[0]) == expected[0]
                && string.CompareOrdinal(word, 1, expected, 1, word.Length - 1) == 0
            : string.Equals(word, expected, StringComparison.Ordinal);
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
