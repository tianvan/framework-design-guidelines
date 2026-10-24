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
    /// <remarks>
    /// False whenever that is uncertain: an empty baseline (a new file), a syntax error in either source, a position
    /// outside any declaration, or no declaration with the same container, name, kind and parameter types, so a
    /// renamed type, a field turned into a property and a new overload are all new.
    /// </remarks>
    public static bool IsExisting(string currentSource, string baselineSource, int line, int column) =>
        IsExisting(DeclarationLocator.Parse(currentSource), DeclarationLocator.Parse(baselineSource), line, column);

    /// <inheritdoc cref="IsExisting(string, string, int, int)"/>
    public static bool IsExisting(SyntaxTree current, SyntaxTree baseline, int line, int column) =>
        DeclarationLocator.DeclarationAt(current, line, column) is DeclarationKey key && DeclarationLocator.Declares(baseline, key);
}
