using System.Globalization;

namespace Fdg.Cli.Hooks;

/// <summary>
/// Stop hook settings from <c>FDG_HOOK_MAX_BLOCKS</c>, <c>FDG_HOOK_TIMEOUT_SECONDS</c> and <c>FDG_HOOK_BUILD</c>
/// (<c>0</c> turns the build step off). Missing or invalid values fall back to the defaults.
/// </summary>
/// <param name="MaxBlocks">How many times one turn may be blocked before the issues are only reported.</param>
/// <param name="Timeout">For the whole Stop check: formatting, building and reporting.</param>
public sealed record HookSettings(int MaxBlocks, TimeSpan Timeout, bool Build)
{
    public static HookSettings Default { get; } = new(2, TimeSpan.FromSeconds(60), true);

    public static HookSettings FromEnvironment(Func<string, string?> environment)
    {
        int maxBlocks = int.TryParse(environment("FDG_HOOK_MAX_BLOCKS"), NumberStyles.None, CultureInfo.InvariantCulture, out int blocks)
            ? blocks
            : Default.MaxBlocks;
        TimeSpan timeout = int.TryParse(environment("FDG_HOOK_TIMEOUT_SECONDS"), NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : Default.Timeout;
        bool build = environment("FDG_HOOK_BUILD")?.Trim() != "0";
        return new HookSettings(maxBlocks, timeout, build);
    }
}
