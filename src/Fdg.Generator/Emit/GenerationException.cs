namespace Fdg.Generator.Emit;

/// <summary>The rule data cannot be turned into consistent output, such as two rules setting one option differently.</summary>
public sealed class GenerationException : Exception
{
    public GenerationException(string message)
        : base(message)
    {
    }
}
