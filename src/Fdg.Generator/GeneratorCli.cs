using Fdg.Generator.Commands;

namespace Fdg.Generator;

public static class GeneratorCli
{
    private const string Usage =
        "Usage:\n" +
        "  fdg-gen extract --book <OEBPS dir> --chapter <token> [--rules <dir>] [--update]\n" +
        "  fdg-gen verify-source --book <OEBPS dir> [--rules <dir>]\n" +
        "  fdg-gen check [--book <OEBPS dir>] [--rules <dir>] [--sdk <SDK dir>]\n" +
        "  fdg-gen generate [--rules <dir>] [--out <repository root>] [--version <version>]";

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
            case "check":
                return CheckCommand.Run(rest, output, error);
            case "generate":
                return GenerateCommand.Run(rest, output, error);
            case "sdk-catalog":
                return SdkCatalogCommand.Run(rest, output, error);
            default:
                error.WriteLine($"Unknown command '{args[0]}'.");
                error.WriteLine(Usage);
                return 2;
        }
    }
}
