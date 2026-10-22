using System.Text;
using Fdg.Cli;

// Paths in the output may contain CJK characters.
Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    return await FdgCli.RunAsync(args, Console.Out, Console.Error, cancellation.Token);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    return 130;
}
