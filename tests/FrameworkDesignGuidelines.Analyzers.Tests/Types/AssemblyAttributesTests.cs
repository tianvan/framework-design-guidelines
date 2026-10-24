using FrameworkDesignGuidelines.Analyzers.Types;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Types.AssemblyAttributesAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Types;

public sealed class AssemblyAttributesTests
{
    private const string AllAttributes =
        """
        using System.Reflection;

        [assembly: AssemblyTitle("System.Core.dll")]
        [assembly: AssemblyCompany("Microsoft Corporation")]
        [assembly: AssemblyProduct("Microsoft .NET Framework")]
        [assembly: AssemblyDescription("Core types")]
        [assembly: AssemblyFileVersion("3.5.21022.8")]
        [assembly: AssemblyCopyright("Copyright")]

        public class Widget
        {
        }
        """;

    private static DiagnosticResult Missing(string id, string attribute)
    {
        DiagnosticDescriptor descriptor = id == "FDG4059" ? FdgDescriptors.FDG4059 : FdgDescriptors.FDG4061;
        return new DiagnosticResult(descriptor).WithArguments(attribute);
    }

    [Fact]
    public Task Assembly_with_all_attributes_is_not_reported()
    {
        return Verifier.VerifyWithAsync(AllAttributes);
    }

    [Fact]
    public Task Each_missing_attribute_is_reported_once()
    {
        string source = AllAttributes
            .Replace("[assembly: AssemblyDescription(\"Core types\")]", string.Empty)
            .Replace("[assembly: AssemblyCopyright(\"Copyright\")]", string.Empty);
        return Verifier.VerifyWithAsync(
            source,
            [Missing("FDG4059", "AssemblyDescriptionAttribute"), Missing("FDG4061", "AssemblyCopyrightAttribute")]);
    }

    [Fact]
    public Task Assembly_without_any_attribute_reports_all_six()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Widget
            {
            }
            """,
            [
                Missing("FDG4059", "AssemblyTitleAttribute"),
                Missing("FDG4059", "AssemblyCompanyAttribute"),
                Missing("FDG4059", "AssemblyProductAttribute"),
                Missing("FDG4059", "AssemblyDescriptionAttribute"),
                Missing("FDG4061", "AssemblyFileVersionAttribute"),
                Missing("FDG4061", "AssemblyCopyrightAttribute"),
            ]);
    }

    [Fact]
    public Task Assembly_without_visible_types_is_not_reported()
    {
        return Verifier.VerifyWithAsync(
            """
            internal class Widget
            {
                public class Nested
                {
                }
            }
            """);
    }
}
