using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fdg.Cli.Integration;

internal sealed record InstallReceipt(
    int SchemaVersion,
    string Host,
    string ToolVersion,
    string ReferenceHost,
    bool CreatedConfiguration,
    bool CreatedHooks,
    string[] CreatedEvents,
    SortedDictionary<string, string> Files,
    Dictionary<string, JsonObject> Hooks)
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public static InstallReceipt? Read(AgentHost host, byte[]? content)
    {
        if (content is null)
        {
            return null;
        }

        try
        {
            InstallReceipt? receipt = JsonSerializer.Deserialize<InstallReceipt>(Encoding.UTF8.GetString(content).TrimStart('\uFEFF'), Json);
            if (receipt is not null && receipt.SchemaVersion == 1 && receipt.Host == host.Name &&
                ToolCache.IsVersion(receipt.ToolVersion) && AgentHost.Find(receipt.ReferenceHost) is { } owner &&
                receipt.Files is not null && receipt.Files.Count > 0 &&
                receipt.Files.All(file => AgentAssets.IsManagedPath(host, owner, file.Key) &&
                    file.Value is { Length: 64 } && file.Value.All(char.IsAsciiHexDigit)) &&
                receipt.CreatedEvents is not null && receipt.CreatedEvents.All(AgentHost.Events.ContainsKey) &&
                receipt.Hooks is not null && receipt.Hooks.Count == AgentHost.Events.Count &&
                AgentHost.Events.Keys.All(key => receipt.Hooks.TryGetValue(key, out JsonObject? group) &&
                    HookConfiguration.IsOwnedGroup(group)))
            {
                return receipt;
            }
        }
        catch (JsonException)
        {
        }

        throw new CommandException($"Invalid FDG installation record: {host.ReceiptPath}.");
    }

    public static string Hash(byte[] content)
    {
        // Git can check out text as CRLF and editors may add a UTF-8 BOM.
        string text = Encoding.UTF8.GetString(content).TrimStart('\uFEFF').ReplaceLineEndings("\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public byte[] Content() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, Json) + "\n");
}
