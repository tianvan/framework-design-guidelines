using System.Reflection;
using System.Text;

namespace Fdg.Cli.Integration;

internal static class AgentAssets
{
    private const string ResourcePrefix = "Fdg.Agent/";

    private const string ReferencePrefix = "skills/fdg-write/references/";

    public static readonly string[] SkillNames = ["fdg-write", "fdg-setup", "fdg-fix", "fdg-review"];

    public static Dictionary<string, byte[]> For(AgentHost host, AgentHost referenceHost)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in SkillNames)
        {
            string path = $"skills/{name}/SKILL.md";
            string content = ReadText(path)
                .Replace("@FDG_LAUNCHER@", Invocation(host), StringComparison.Ordinal)
                .Replace("@FDG_RECEIPT@", "`" + host.ReceiptPath + "`", StringComparison.Ordinal);
            string sourceDirectory = host.Skills + "/" + name;
            string references = ReferenceLink(host, referenceHost, name);
            if (name == "fdg-write")
            {
                content = content.Replace("references/", references, StringComparison.Ordinal);
            }
            else if (name == "fdg-review")
            {
                content = content.Replace("../fdg-write/references", references.TrimEnd('/'), StringComparison.Ordinal);
            }

            if (host == AgentHost.Codex && name != "fdg-write")
            {
                content = content.Replace("disable-model-invocation: true\n", string.Empty, StringComparison.Ordinal);
                int start = content.IndexOf("## Where to run it", StringComparison.Ordinal);
                if (start >= 0)
                {
                    content = content.Remove(start, content.IndexOf("## Steps", start, StringComparison.Ordinal) - start);
                }

                files.Add(sourceDirectory + "/agents/openai.yaml", Encoding.UTF8.GetBytes("policy:\n  allow_implicit_invocation: false\n"));
            }

            files.Add(sourceDirectory + "/SKILL.md", Encoding.UTF8.GetBytes(content));
        }

        if (host == AgentHost.Claude)
        {
            files.Add(".claude/agents/fdg-reviewer.md",
                Encoding.UTF8.GetBytes(ReadText("agents/fdg-reviewer.md").Replace("@FDG_LAUNCHER@", Invocation(host), StringComparison.Ordinal)));
        }

        files.Add(host.LauncherDirectory + "/.gitattributes", Encoding.UTF8.GetBytes("fdg.sh text eol=lf\n"));
        AddLauncher("fdg.sh");
        if (host == AgentHost.Codex)
        {
            AddLauncher("fdg.ps1");
        }

        if (host == referenceHost)
        {
            foreach (string resource in ResourcesByPath.Keys.Where(name => name.StartsWith(ReferencePrefix, StringComparison.Ordinal)))
            {
                files.Add(host.Skills + "/" + resource["skills/".Length..], Encoding.UTF8.GetBytes(ReadText(resource)));
            }
        }

        return files;

        void AddLauncher(string name) => files.Add(
            host.LauncherDirectory + "/" + name,
            Encoding.UTF8.GetBytes(ReadText("hooks/" + name).Replace("@FDG_HOST@", host.Name, StringComparison.Ordinal)));
    }

    public static string ReferenceLink(AgentHost host, AgentHost referenceHost, string skill) =>
        Path.GetRelativePath(Path.GetFullPath(host.Skills + "/" + skill), Path.GetFullPath(referenceHost.References))
            .Replace('\\', '/') + "/";

    public static string? ReferenceSuffix(AgentHost host, string path) =>
        path.StartsWith(host.References + "/", StringComparison.Ordinal) ? path[host.References.Length..] : null;

    // This also constrains which paths an installation receipt can claim. Reference names may change between versions.
    public static bool IsManagedPath(AgentHost host, AgentHost referenceHost, string path)
    {
        if (SkillNames.Any(name => path == $"{host.Skills}/{name}/SKILL.md" ||
            (host == AgentHost.Codex && name != "fdg-write" && path == $"{host.Skills}/{name}/agents/openai.yaml")) ||
            (host == AgentHost.Claude && path == ".claude/agents/fdg-reviewer.md") ||
            path == host.LauncherDirectory + "/.gitattributes" || path == host.LauncherDirectory + "/fdg.sh" ||
            (host == AgentHost.Codex && path == host.LauncherDirectory + "/fdg.ps1"))
        {
            return true;
        }

        if (host != referenceHost || !path.StartsWith(host.References + "/", StringComparison.Ordinal))
        {
            return false;
        }

        string file = path[(host.References.Length + 1)..];
        if (file.StartsWith("examples/", StringComparison.Ordinal))
        {
            file = file["examples/".Length..];
        }

        return file.EndsWith(".md", StringComparison.Ordinal) && file[..^3].All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }

    private static Assembly Assembly => typeof(AgentAssets).Assembly;

    private static readonly Dictionary<string, string> ResourcesByPath = Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
        .ToDictionary(name => name[ResourcePrefix.Length..].Replace('\\', '/'), StringComparer.Ordinal);

    private static string ReadText(string path)
    {
        if (!ResourcesByPath.TryGetValue(path, out string? resource))
        {
            throw new InvalidOperationException($"Missing FDG package asset: {path}");
        }

        using var reader = new StreamReader(Assembly.GetManifestResourceStream(resource)!);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    private static string Invocation(AgentHost host) => host == AgentHost.Codex
        ? $"`bash {host.LauncherDirectory}/fdg.sh` (macOS/Linux) or `pwsh -NoProfile -File {host.LauncherDirectory}/fdg.ps1` (Windows)"
        : $"`bash {host.LauncherDirectory}/fdg.sh`";
}
