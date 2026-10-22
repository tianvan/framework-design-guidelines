namespace Fdg.Cli;

/// <summary>An invalid command-line argument; the message is printed as is and the command exits with 2.</summary>
public sealed class CommandException : Exception
{
    public CommandException(string message)
        : base(message)
    {
    }
}
