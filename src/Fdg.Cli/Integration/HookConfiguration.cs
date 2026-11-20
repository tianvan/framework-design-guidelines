using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tomlyn;
using Tomlyn.Model;

namespace Fdg.Cli.Integration;

internal static class HookConfiguration
{
    public sealed record EditResult(byte[]? Content, bool CreatedConfiguration, bool CreatedHooks, string[] CreatedEvents);

    public static EditResult Edit(
        AgentHost host,
        byte[]? before,
        InstallReceipt? receipt,
        Dictionary<string, JsonObject>? desired)
    {
        JsonObject settings;
        try
        {
            settings = before is null
                ? new JsonObject()
                : JsonNode.Parse(Encoding.UTF8.GetString(before).TrimStart('\uFEFF')) as JsonObject
                    ?? throw new JsonException("Expected a JSON object.");
            // Materialize the JSON objects to catch duplicate keys before doing any edits.
            ValidateObjects(settings);
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            throw new CommandException($"Invalid JSON in {host.Configuration}: {e.Message}");
        }

        var original = (JsonObject)settings.DeepClone();
        if (desired is not null && host == AgentHost.Claude &&
            settings["disableAllHooks"] is JsonValue disabled && disabled.TryGetValue(out bool isDisabled) && isDisabled)
        {
            throw new CommandException($"Hooks are disabled in {host.Configuration}. Update disableAllHooks before installing FDG.");
        }

        JsonObject hooks = settings["hooks"] switch
        {
            JsonObject value => value,
            null when !settings.ContainsKey("hooks") => new JsonObject(),
            _ => throw new CommandException($"Expected a hooks object in {host.Configuration}."),
        };

        bool createdHooks = receipt?.CreatedHooks ?? hooks.Parent is null;
        var createdEvents = new HashSet<string>(receipt?.CreatedEvents ?? [], StringComparer.Ordinal);
        foreach (string name in AgentHost.Events.Keys)
        {
            JsonArray groups = hooks[name] switch
            {
                JsonArray value => value,
                null when !hooks.ContainsKey(name) => new JsonArray(),
                _ => throw new CommandException($"Expected a {name} hook list in {host.Configuration}."),
            };
            int owned = -1;
            for (int index = 0; index < groups.Count; index++)
            {
                if (groups[index] is not JsonObject group || group["hooks"] is not JsonArray handlers ||
                    handlers.Any(handler => handler is not JsonObject))
                {
                    throw new CommandException($"Invalid {name} hook group in {host.Configuration}.");
                }

                if (receipt is not null && JsonNode.DeepEquals(group, receipt.Hooks[name]) && owned == -1)
                {
                    owned = index;
                    continue;
                }

                if (IsFdgGroup(group))
                {
                    throw new CommandException($"Conflicting {name} FDG hook in {host.Configuration}. Inspect the existing handler and retry.");
                }
            }

            if (owned != -1)
            {
                if (desired is null)
                {
                    groups.RemoveAt(owned);
                    if (groups.Count == 0 && createdEvents.Contains(name))
                    {
                        hooks.Remove(name);
                    }
                }
                else if (!JsonNode.DeepEquals(groups[owned], desired[name]))
                {
                    groups[owned] = desired[name].DeepClone();
                }
            }
            else if (desired is not null)
            {
                groups.Add(desired[name].DeepClone());
                if (groups.Parent is null)
                {
                    createdEvents.Add(name);
                    hooks.Add(name, groups);
                }
            }
        }

        bool created = receipt?.CreatedConfiguration ?? (before is null);
        if (hooks.Count == 0 && createdHooks)
        {
            settings.Remove("hooks");
        }
        else if (hooks.Count > 0 && hooks.Parent is null)
        {
            settings.Add("hooks", hooks);
        }

        if (desired is null && created && settings.Count == 0)
        {
            return new EditResult(null, created, createdHooks, []);
        }

        return new EditResult(JsonNode.DeepEquals(original, settings)
            ? before
            : Encoding.UTF8.GetBytes(settings.ToJsonString(InstallReceipt.Json) + "\n"),
            created, createdHooks, createdEvents.Order(StringComparer.Ordinal).ToArray());
    }

    public static bool IsFdgGroup(JsonObject? group) =>
        group?["hooks"] is JsonArray handlers && handlers.OfType<JsonObject>().Any(handler =>
            IsFdgCommand(ReadString(handler["command"])) || IsFdgCommand(ReadString(handler["commandWindows"])));

    public static bool IsOwnedGroup(JsonObject? group) =>
        group?["hooks"] is JsonArray { Count: 1 } handlers && handlers[0] is JsonObject handler &&
        ReadString(handler["type"]) == "command" && IsFdgGroup(group);

    public static void CheckCodexToml(byte[]? config)
    {
        if (config is null)
        {
            return;
        }

        var syntax = Toml.Parse(Encoding.UTF8.GetString(config).TrimStart('\uFEFF'));
        if (syntax.HasErrors)
        {
            throw new CommandException($"Invalid .codex/config.toml: {syntax.Diagnostics.First()}");
        }

        TomlTable model;
        try
        {
            model = syntax.ToModel();
        }
        catch (TomlException e)
        {
            throw new CommandException($"Invalid .codex/config.toml: {e.Message}");
        }

        if (model.TryGetValue("hooks", out object? hooks) && DeclaresFdg(hooks))
        {
            throw new CommandException("FDG hooks already appear in .codex/config.toml. Remove those declarations before updating .codex/hooks.json.");
        }
    }

    private static bool DeclaresFdg(object? node) => node switch
    {
        TomlTable table => table.Any(entry =>
            (entry.Key is "command" or "commandWindows" or "command_windows" && IsFdgCommand(entry.Value as string)) ||
            DeclaresFdg(entry.Value)),
        TomlArray array => array.Any(DeclaresFdg),
        TomlTableArray tables => tables.Any(DeclaresFdg),
        _ => false,
    };

    private static bool IsFdgCommand(string? value) => value is not null &&
        (value.Contains("hooks/fdg/fdg.", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("hooks\\fdg\\fdg.", StringComparison.OrdinalIgnoreCase) ||
         value.Contains("fdg-hook.", StringComparison.OrdinalIgnoreCase) ||
         (value.Contains("dotnet-fdg", StringComparison.OrdinalIgnoreCase) && value.Contains("hook ", StringComparison.Ordinal)) ||
         value.Contains("dotnet fdg hook ", StringComparison.OrdinalIgnoreCase));

    private static string? ReadString(JsonNode? value) =>
        value is JsonValue scalar && scalar.TryGetValue(out string? text) ? text : null;

    private static void ValidateObjects(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach ((_, JsonNode? child) in obj)
            {
                if (child is not null)
                {
                    ValidateObjects(child);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode child in array.OfType<JsonNode>())
            {
                ValidateObjects(child);
            }
        }
    }
}
