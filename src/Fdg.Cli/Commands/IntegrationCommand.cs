using System.Text;
using System.Text.Json.Nodes;
using Fdg.Cli.Hooks;
using Fdg.Cli.Integration;
using Fdg.Cli.IO;
using Fdg.Cli.Projects;

namespace Fdg.Cli.Commands;

internal static class IntegrationCommand
{
    private static readonly string[] ProjectPathspecs = [":(glob,icase)**/*.csproj"];

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args, TextReader input, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        try
        {
            bool install = args[0] == "init";
            (string? host, bool preview, bool yes) = ParseOptions(args);
            if (host is null && !yes && !Console.IsInputRedirected)
            {
                output.Write("Which host (codex, claude, both)? ");
                host = input.ReadLine()?.Trim().ToLowerInvariant();
            }

            AgentHost[] selected = host == "both" ? [.. AgentHost.All] :
                AgentHost.Find(host) is { } match ? [match] :
                throw new CommandException("Select a host with --host codex, --host claude or --host both.");

            GitRepository git = await GitRepository.OpenAsync(Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false)
                ?? throw new CommandException("Run this command inside the Git worktree you want to configure.");
            string[] projects = install
                ? (await git.ListFilesAsync(ProjectPathspecs, cancellationToken).ConfigureAwait(false))
                    .Where(path => IsCandidate(git.TopLevel, path)).Order(StringComparer.Ordinal).ToArray()
                : [];
            if (install && projects.Length == 0)
            {
                throw new CommandException("No SDK-style C# project was found inside this worktree.");
            }

            string state = Path.Combine(git.GitDirectory, "fdg-install");
            string journal = Path.Combine(state, "transaction.json");
            string lockPath = Path.Combine(state, "lock");
            if (!WorktreePath.IsSafe(git.GitDirectory, journal) || !WorktreePath.IsSafe(git.GitDirectory, lockPath))
            {
                throw new CommandException("The FDG installation state must be inside this worktree's Git directory.");
            }

            InstallChanges? changes = null;
            if (!File.Exists(journal))
            {
                changes = Plan(git.TopLevel, selected, install);
                Describe(changes, selected, install, projects, output);
                if (preview)
                {
                    await changes.PreviewDiffAsync(output, cancellationToken).ConfigureAwait(false);
                    return 0;
                }

                if (changes.Count == 0 || !Confirm(yes, input, output, "Apply these changes? [y/N] "))
                {
                    return 0;
                }
            }
            else if (preview || !Confirm(yes, input, output, "Recover the interrupted FDG installation? [y/N] "))
            {
                throw new CommandException("An FDG installation was interrupted. Rerun init or uninstall with --yes to recover it, then preview again.");
            }

            Directory.CreateDirectory(state);
            using FileStream lease = await FileLease.AcquireAsync(lockPath, cancellationToken).ConfigureAwait(false);
            if (changes is null)
            {
                InstallChanges.Recover(git.TopLevel, journal);
                changes = Plan(git.TopLevel, selected, install);
                Describe(changes, selected, install, projects, output);
                if (changes.Count == 0 || !Confirm(yes, input, output, "Apply these changes? [y/N] "))
                {
                    return 0;
                }
            }
            else if (File.Exists(journal))
            {
                throw new CommandException("Another FDG installation was interrupted. Run this command again to recover it.");
            }

            if (install)
            {
                await ToolCache.PrepareAsync(git.TopLevel, FdgCli.Version, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            changes.Apply(journal);
            output.WriteLine(install
                ? "FDG is configured. Reopen the repository in the selected host and review its project hook approvals."
                : "FDG integration removed for the selected host.");
            if (install && selected.Contains(AgentHost.Codex) && File.Exists(Path.Combine(git.GitDirectory, "commondir")))
            {
                output.WriteLine("In a linked worktree, Codex 0.169 reads project hook declarations from the primary checkout. Merge the hook configuration into that checkout to enable discovery there.");
            }

            return 0;
        }
        catch (CommandException e)
        {
            error.WriteLine(e.Message);
            return 2;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            error.WriteLine("FDG integration failed: " + e.Message);
            return 1;
        }
    }

    private static InstallChanges Plan(string root, AgentHost[] selected, bool install)
    {
        var changes = new InstallChanges(root);
        var installed = new Dictionary<AgentHost, InstallReceipt>();
        foreach (AgentHost host in AgentHost.All)
        {
            if (!selected.Contains(host) && !WorktreePath.IsSafe(root, Path.Combine(root, host.ReceiptPath)))
            {
                continue;
            }

            if (InstallReceipt.Read(host, changes.Read(host.ReceiptPath)) is { } receipt)
            {
                installed.Add(host, receipt);
            }
        }

        AgentHost[] remaining = AgentHost.All.Where(host => install
            ? selected.Contains(host) || installed.ContainsKey(host)
            : installed.ContainsKey(host) && !selected.Contains(host)).ToArray();

        AgentHost[] owners = AgentHost.All.Where(host => installed.TryGetValue(host, out var receipt) &&
            receipt.Files.Keys.Any(path => AgentAssets.ReferenceSuffix(host, path) is not null)).ToArray();
        AgentHost? owner = owners.FirstOrDefault();
        AgentHost? nextOwner = owner is not null && remaining.Contains(owner) ? owner : remaining.FirstOrDefault();
        string[] versions = installed.Values.Select(receipt => receipt.ToolVersion).Distinct(StringComparer.Ordinal).ToArray();
        bool moving = !install && nextOwner is not null && nextOwner != owner;
        if (moving && (owner is null || owners.Length != 1 || versions.Length != 1 ||
            installed.Values.Any(record => record.ReferenceHost != owner.Name)))
        {
            throw new CommandException("The installed hosts disagree on the shared references or tool version. Run a versioned init to reconcile them before removing one host.");
        }

        if (versions.Length > 1)
        {
            changes.Notice("Installed versions differ: " + string.Join(", ", installed.Select(pair =>
                $"{pair.Key.ReceiptPath} = {pair.Value.ToolVersion}")));
        }

        foreach (AgentHost host in AgentHost.All.Where(host => install ? remaining.Contains(host) : selected.Contains(host) || moving))
        {
            installed.TryGetValue(host, out InstallReceipt? receipt);
            bool retain = remaining.Contains(host);
            if (!retain && receipt is null)
            {
                continue;
            }

            if (host == AgentHost.Codex)
            {
                HookConfiguration.CheckCodexToml(changes.Read(".codex/config.toml"));
            }

            Dictionary<string, JsonObject>? hooks = install ? host.HookGroups() : retain ? receipt!.Hooks : null;
            HookConfiguration.EditResult configuration = HookConfiguration.Edit(
                host, changes.Read(host.Configuration), receipt, hooks);
            Dictionary<string, byte[]> desired = !retain ? [] : install ? AgentAssets.For(host, nextOwner!) :
                MoveReferences(changes, host, owner!, receipt!, installed[owner!]);
            var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var conflicts = new List<string>();
            foreach (string path in (receipt?.Files.Keys.AsEnumerable() ?? []).Union(desired.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                byte[]? before = changes.Read(path);
                desired.TryGetValue(path, out byte[]? after);
                string? hash = before is null ? null : InstallReceipt.Hash(before);
                if (before is not null)
                {
                    if (receipt is null || !receipt.Files.TryGetValue(path, out string? expected))
                    {
                        conflicts.Add(path + " (already exists)");
                        continue;
                    }

                    if (!string.Equals(hash, expected, StringComparison.Ordinal))
                    {
                        if (retain)
                        {
                            conflicts.Add(path + " (edited since installation)");
                        }
                        else
                        {
                            changes.Notice($"Keeping modified file {path}");
                        }

                        continue;
                    }
                }

                if (after is not null)
                {
                    hashes.Add(path, InstallReceipt.Hash(after));
                    if (hash == hashes[path])
                    {
                        continue;
                    }
                }

                changes.Set(path, after);
            }

            if (conflicts.Count > 0)
            {
                throw new CommandException("Resolve these FDG file conflicts and retry:\n  " + string.Join("\n  ", conflicts));
            }

            var record = retain ? new InstallReceipt(
                1, host.Name, install ? FdgCli.Version : receipt!.ToolVersion, nextOwner!.Name, configuration.CreatedConfiguration,
                configuration.CreatedHooks, configuration.CreatedEvents, hashes, hooks!) : null;
            changes.Set(host.ReceiptPath, record?.Content());
            changes.Set(host.Configuration, configuration.Content);
        }

        return changes;
    }

    private static Dictionary<string, byte[]> MoveReferences(
        InstallChanges changes, AgentHost host, AgentHost owner, InstallReceipt receipt, InstallReceipt referenceReceipt)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach ((string path, string hash) in receipt.Files)
        {
            byte[] content = ReadOwned(path, hash);
            foreach (string skill in new[] { "fdg-review", "fdg-write" })
            {
                if (path != $"{host.Skills}/{skill}/SKILL.md")
                {
                    continue;
                }

                string oldLink = AgentAssets.ReferenceLink(host, owner, skill).TrimEnd('/');
                string newLink = AgentAssets.ReferenceLink(host, host, skill).TrimEnd('/');
                content = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(content)
                    .Replace(oldLink, newLink, StringComparison.Ordinal));
            }

            files.Add(path, content);
        }

        foreach ((string path, string hash) in referenceReceipt.Files)
        {
            if (AgentAssets.ReferenceSuffix(owner, path) is { } suffix)
            {
                files.Add(host.References + suffix, ReadOwned(path, hash));
            }
        }

        return files;

        byte[] ReadOwned(string path, string hash)
        {
            byte[]? bytes = changes.Read(path);
            if (bytes is null || InstallReceipt.Hash(bytes) != hash)
            {
                throw new CommandException($"Cannot move the shared references because {path} is missing or edited. Restore that file or keep the current reference host.");
            }

            return bytes;
        }
    }

    private static void Describe(InstallChanges changes, AgentHost[] selected, bool install, string[] projects, TextWriter output)
    {
        output.WriteLine(changes.Count == 0 ? "No changes." : $"{(install ? "Configure" : "Remove")} FDG. Selected hosts: {string.Join(", ", selected.Select(host => host.Name))}");
        if (changes.Count > 0 && install)
        {
            output.WriteLine($"  Tool: dotnet-fdg {FdgCli.Version}. A missing version is prepared from the repository's NuGet sources.");
            output.WriteLine($"  SDK-style C# projects ({projects.Length}):");
            foreach (string project in projects)
            {
                output.WriteLine("    " + project);
            }

            output.WriteLine("  Stop formats and applies safe fixes to entire C# files changed this turn in onboarded projects.");
        }

        changes.Preview(output);
    }

    private static bool Confirm(bool yes, TextReader input, TextWriter output, string prompt)
    {
        if (yes)
        {
            return true;
        }

        if (Console.IsInputRedirected)
        {
            throw new CommandException("Pass --yes to apply this plan non-interactively, or --dry-run to inspect it.");
        }

        output.Write(prompt);
        if (string.Equals(input.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        output.WriteLine("No changes applied.");
        return false;
    }

    private static (string? Host, bool Preview, bool Yes) ParseOptions(IReadOnlyList<string> args)
    {
        string? host = null;
        bool preview = false;
        bool yes = false;
        for (int index = 1; index < args.Count; index++)
        {
            switch (args[index])
            {
                case "--host" when host is null && index + 1 < args.Count:
                    host = args[++index];
                    break;
                case "--dry-run":
                    preview = true;
                    break;
                case "--yes":
                    yes = true;
                    break;
                default:
                    throw new CommandException($"Usage: dotnet-fdg {args[0]} --host codex|claude|both [--dry-run | --yes]");
            }
        }

        return (host, preview, yes);
    }

    private static bool IsCandidate(string root, string path)
    {
        string full = Path.GetFullPath(path, root);
        return !path.Split('/')[..^1].Any(ProjectFiles.IsSkippedDirectory) &&
            WorktreePath.IsSafe(root, full) && File.Exists(full) && SdkStyleDetector.IsSdkStyle(full);
    }
}
