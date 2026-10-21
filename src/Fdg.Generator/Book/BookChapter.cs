using Fdg.Generator.Rules;

namespace Fdg.Generator.Book;

/// <param name="Rules">The guidelines in book order.</param>
/// <param name="IconCount">The number of check/cross icons found in the chapter.</param>
public sealed record BookChapter(IReadOnlyList<Rule> Rules, int IconCount);
