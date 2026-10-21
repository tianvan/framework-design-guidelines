namespace Fdg.Generator.Book;

public sealed class BookFormatException : Exception
{
    public BookFormatException(string message)
        : base(message)
    {
    }
}
