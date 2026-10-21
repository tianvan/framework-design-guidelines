using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;
using YamlDotNet.Serialization.NamingConventions;

namespace Fdg.Generator.Rules;

/// <summary>Reads and writes the rule list format of <c>rules/*.yaml</c>.</summary>
public static class RuleYaml
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.Preserve)
        .WithIndentedSequences()
        .WithQuotingNecessaryStrings()
        .WithEventEmitter(next => new LiteralBlockEmitter(next), loc => loc.OnBottom())
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static string Serialize(IEnumerable<Rule> rules)
    {
        string yaml = Serializer.Serialize(rules.Select(RuleDto.From).ToList());

        // Keep files byte-identical across platforms.
        return yaml.Replace("\r\n", "\n");
    }

    public static IReadOnlyList<Rule> Deserialize(string yaml)
    {
        List<RuleDto>? dtos = Deserializer.Deserialize<List<RuleDto>>(yaml);
        return dtos?.Select(dto => dto.ToRule()).ToList() ?? new List<Rule>();
    }

    internal static T ParseEnum<T>(string? text, string field)
        where T : struct, Enum
    {
        if (!Enum.TryParse(text, ignoreCase: true, out T value) || !Enum.IsDefined(value))
        {
            throw new FormatException($"Invalid {field} '{text}'.");
        }

        return value;
    }

    internal static string EnumText<T>(T value)
        where T : struct, Enum
    {
        return value.ToString().ToLowerInvariant();
    }

    /// <summary>Writes null as an explicit <c>null</c> and strings with line breaks as YAML literal blocks.</summary>
    private sealed class LiteralBlockEmitter : ChainedEventEmitter
    {
        public LiteralBlockEmitter(IEventEmitter next)
            : base(next)
        {
        }

        public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
        {
            if (eventInfo.Source.Value is null)
            {
                eventInfo.RenderedValue = "null";
            }
            else if (eventInfo.Source.Value is string text && text.Contains('\n'))
            {
                eventInfo.Style = ScalarStyle.Literal;
                eventInfo.IsPlainImplicit = false;
                eventInfo.IsQuotedImplicit = true;
            }

            base.Emit(eventInfo, emitter);
        }
    }

    internal sealed class RuleDto
    {
        public string Id { get; set; } = string.Empty;

        public string Strength { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public string? Context { get; set; }

        public string Section { get; set; } = string.Empty;

        public string Anchor { get; set; } = string.Empty;

        public List<BlockDto> Body { get; set; } = new();

        public string Status { get; set; } = string.Empty;

        public bool Style { get; set; }

        public CheckDto Check { get; set; } = new();

        public string Autofix { get; set; } = string.Empty;

        public SeverityDto Severity { get; set; } = new();

        public List<string> Config { get; set; } = new();

        public string? MinLanguage { get; set; }

        public string? Alternative { get; set; }

        public AnalyzerDto? Analyzer { get; set; }

        public string? Notes { get; set; }

        public static RuleDto From(Rule rule)
        {
            RuleMapping mapping = rule.Mapping;
            return new RuleDto
            {
                Id = rule.Id,
                Strength = rule.Strength.ToText(),
                Text = rule.Text,
                Context = rule.Context,
                Section = rule.Section,
                Anchor = rule.Anchor,
                Body = rule.Body.Select(b => new BlockDto
                {
                    Kind = EnumText(b.Kind),
                    Text = b.Text,
                    Author = b.Author,
                }).ToList(),
                Status = EnumText(mapping.Status),
                Style = mapping.Style,
                Check = new CheckDto
                {
                    Kind = EnumText(mapping.Check.Kind),
                    Diagnostics = mapping.Check.Diagnostics.ToList(),
                    Coverage = EnumText(mapping.Check.Coverage),
                },
                Autofix = EnumText(mapping.Autofix),
                Severity = new SeverityDto
                {
                    Library = EnumText(mapping.Severity.Library),
                    Application = EnumText(mapping.Severity.Application),
                    Test = EnumText(mapping.Severity.Test),
                },
                Config = mapping.Config.ToList(),
                MinLanguage = mapping.MinLanguage,
                Alternative = mapping.Alternative,
                Analyzer = mapping.Analyzer is null
                    ? null
                    : new AnalyzerDto { Message = mapping.Analyzer.Message, Category = mapping.Analyzer.Category },
                Notes = mapping.Notes,
            };
        }

        public Rule ToRule()
        {
            var mapping = new RuleMapping(
                ParseEnum<RuleStatus>(Status, "status"),
                Style,
                new CheckInfo(
                    ParseEnum<CheckKind>(Check.Kind, "check.kind"),
                    Check.Diagnostics,
                    ParseEnum<CoverageLevel>(Check.Coverage, "check.coverage")),
                ParseEnum<AutofixKind>(Autofix, "autofix"),
                new ProfileSeverity(
                    ParseEnum<SeverityLevel>(Severity.Library, "severity.library"),
                    ParseEnum<SeverityLevel>(Severity.Application, "severity.application"),
                    ParseEnum<SeverityLevel>(Severity.Test, "severity.test")),
                Config,
                MinLanguage,
                Alternative,
                Analyzer is null ? null : new AnalyzerInfo(Analyzer.Message, Analyzer.Category),
                Notes);

            return new Rule(
                Id,
                StrengthExtensions.Parse(Strength),
                Text,
                Context,
                Section,
                Anchor,
                Body.Select(b => new Block(ParseEnum<BlockKind>(b.Kind, "body.kind"), b.Text, b.Author)).ToList(),
                mapping);
        }
    }

    internal sealed class BlockDto
    {
        public string Kind { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
        public string? Author { get; set; }
    }

    internal sealed class CheckDto
    {
        public string Kind { get; set; } = string.Empty;

        public List<string> Diagnostics { get; set; } = new();

        public string Coverage { get; set; } = string.Empty;
    }

    internal sealed class SeverityDto
    {
        public string Library { get; set; } = string.Empty;

        public string Application { get; set; } = string.Empty;

        public string Test { get; set; } = string.Empty;
    }

    internal sealed class AnalyzerDto
    {
        public string Message { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;
    }
}
