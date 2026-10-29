using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Fdg.Cli.Hooks;

/// <summary>
/// Tells whether a diagnostic is about an API that already existed when the turn started. Changing such an API to fix
/// the diagnostic would break its callers, so the Stop hook does not ask for it.
/// </summary>
public static class ExistingApi
{
    /// <summary>
    /// Whether the declaration at the 1-based line and column of <paramref name="currentSource"/> is declared by
    /// <paramref name="baselineSource"/>, the file's content at the start of the turn.
    /// </summary>
    /// <param name="compareBaseList">
    /// Whether a type whose base list differs from the baseline's is new: a diagnostic on the type may be about the
    /// base types the turn added. See <see cref="ComparesBaseList"/>.
    /// </param>
    /// <remarks>
    /// False whenever that is uncertain: an empty baseline (a new file), a syntax error in either source, a position
    /// outside the API surface (inside a body or initializer, on an attribute), or no declaration with the same
    /// container, name, kind and parameter types, so a renamed type, a field turned into a property and a new overload
    /// are all new. A parameter or type parameter is existing only if the baseline declaration has one of that name.
    /// The base list of a type is compared within the file: the base types of all its declarations in the file, in
    /// any order.
    /// </remarks>
    public static bool IsExisting(string currentSource, string baselineSource, int line, int column, bool compareBaseList = false) =>
        IsExisting(DeclarationLocator.Parse(currentSource), DeclarationLocator.Parse(baselineSource), line, column, compareBaseList);

    /// <inheritdoc cref="IsExisting(string, string, int, int, bool)"/>
    public static bool IsExisting(SyntaxTree current, SyntaxTree baseline, int line, int column, bool compareBaseList = false) =>
        IsExisting(current, DeclarationLocator.Baseline(baseline), line, column, compareBaseList);

    /// <param name="baseline">
    /// <see cref="DeclarationLocator.Baseline"/> of the baseline, computed once per file; null when it could not be
    /// read or parsed.
    /// </param>
    /// <inheritdoc cref="IsExisting(string, string, int, int, bool)"/>
    public static bool IsExisting(SyntaxTree current, BaselineDeclarations? baseline, int line, int column, bool compareBaseList)
    {
        if (baseline is null ||
            DeclarationLocator.DeclarationAt(current, line, column) is not DeclarationKey key ||
            !baseline.Keys.Contains(key))
        {
            return false;
        }

        // Only the key of a type itself (no Part) has a base list.
        return !compareBaseList ||
            !baseline.BaseLists.TryGetValue(key, out string? baseList) ||
            DeclarationLocator.BaseListOf(current, key) == baseList;
    }

    /// <summary>
    /// Whether a diagnostic of <paramref name="ruleId"/> on an existing type is new when the turn changed the type's
    /// base list. False for the naming rules (IDE1006, CA1700 to CA1727, FDG3xxx): renaming the type to fix them would
    /// break its callers, whatever the turn added to it.
    /// </summary>
    public static bool ComparesBaseList(string ruleId) =>
        !(string.Equals(ruleId, DeclarationLocator.NamingDiagnosticId, StringComparison.OrdinalIgnoreCase) ||
            IsNumbered(ruleId, "CA", 1700, 1727) ||
            IsNumbered(ruleId, "FDG", 3000, 3999));

    /// <summary>Whether <paramref name="ruleId"/> is <paramref name="prefix"/> followed by four digits in the range.</summary>
    private static bool IsNumbered(string ruleId, string prefix, int first, int last) =>
        ruleId.Length == prefix.Length + 4 &&
        ruleId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(ruleId.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int number) &&
        number >= first &&
        number <= last;
}
