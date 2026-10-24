using Fdg.Generator.Book;

namespace Fdg.Generator.Tests;

public class NormalizerTests
{
    [Theory]
    [InlineData("3.4.1 Namespaces and Type Name Conf licts", "3.4.1 Namespaces and Type Name Conflicts")]
    public void Ligatures_rejoins_known_split_words(string input, string expected)
    {
        Assert.Equal(expected, Normalizer.Ligatures(input));
    }

    [Theory]
    [InlineData("Krzysztof in")]
    [InlineData("turn it off the")]
    [InlineData("stuff like")]
    [InlineData("diff view")]
    public void Ligatures_leaves_real_words_alone(string input)
    {
        Assert.Equal(input, Normalizer.Ligatures(input));
    }

    [Fact]
    public void Collapse_and_Code_rejoin_split_ligatures()
    {
        Assert.Equal("Type Name Conflicts", Normalizer.Collapse("Type  Name\nConf licts"));
        Assert.Equal("// Conflicts", Normalizer.Code("// Conf licts"));
    }
}
