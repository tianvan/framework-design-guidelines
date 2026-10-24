namespace Fdg.Generator.Rules;

/// <summary>A book field of a rule whose value differs between two versions of the rule.</summary>
/// <param name="Field">The YAML name of the field.</param>
/// <param name="Expected">The value in the expected rule; null for <c>body</c>, which has no one-line form.</param>
/// <param name="Actual">The value in the actual rule; null for <c>body</c>.</param>
public sealed record BookFieldDifference(string Field, object? Expected, object? Actual);

/// <summary>The fields of a rule that only the extractor writes: everything but the mapping.</summary>
public static class BookFields
{
    public static IReadOnlyList<BookFieldDifference> Compare(Rule expected, Rule actual)
    {
        var differences = new List<BookFieldDifference>();
        Add(differences, "id", expected.Id, actual.Id);
        Add(differences, "strength", expected.Strength, actual.Strength);
        Add(differences, "text", expected.Text, actual.Text);
        Add(differences, "context", expected.Context, actual.Context);
        Add(differences, "section", expected.Section, actual.Section);
        Add(differences, "anchor", expected.Anchor, actual.Anchor);
        if (!expected.Body.SequenceEqual(actual.Body))
        {
            differences.Add(new BookFieldDifference("body", null, null));
        }

        return differences;
    }

    private static void Add<T>(List<BookFieldDifference> differences, string field, T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            differences.Add(new BookFieldDifference(field, expected, actual));
        }
    }
}
