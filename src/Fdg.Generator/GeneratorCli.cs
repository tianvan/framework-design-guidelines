using Fdg.Generator.Commands;

namespace Fdg.Generator;

public static class GeneratorCli
{
    private const string Usage =
        "Usage:\n" +
        "  fdg-gen extract --book <OEBPS dir> --chapter <token> [--rules <dir>]\n" +
        "  fdg-gen verify-source --book <OEBPS dir> [--rules <dir>]";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0)
        {
            error.WriteLine(Usage);
            return 2;
        }

        string[] rest = args[1..];
        switch (args[0])
        {
            case "extract":
                return ExtractCommand.Run(rest, output, error);
            case "verify-source":
                return VerifySourceCommand.Run(rest, output, error);
            default:
                error.WriteLine($"Unknown command '{args[0]}'.");
                error.WriteLine(Usage);
                return 2;
        }
    }
}
