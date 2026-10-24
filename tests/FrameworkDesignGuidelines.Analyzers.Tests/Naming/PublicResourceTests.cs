using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Naming.PublicResourceAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Naming;

public sealed class PublicResourceTests
{
    private const string Resources =
        """
        namespace Sample.Properties
        {
            [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
            [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
            ACCESS class Resources
            {
                internal Resources()
                {
                }

                ACCESS static string Greeting
                {
                    get { return "Hello"; }
                }
            }
        }
        """;

    [Fact]
    public Task Public_resource_class_in_a_designer_file_is_reported()
    {
        return Verifier.VerifyAsync(
            "public class C { }",
            ("/0/Properties/Resources.Designer.cs", Resources
                .Replace("ACCESS class Resources", "public class {|FDG3087:Resources|}")
                .Replace("ACCESS", "public")));
    }

    [Fact]
    public Task Internal_resource_class_is_not_reported()
    {
        return Verifier.VerifyAsync(
            "public class C { }",
            ("/0/Properties/Resources.Designer.cs", Resources.Replace("ACCESS", "internal")));
    }

    [Fact]
    public Task Other_generated_code_and_hand_written_classes_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            [System.CodeDom.Compiler.GeneratedCode("xsd", "4.0")]
            public class Generated
            {
            }

            public class Resources
            {
                public static string Greeting => "Hello";
            }
            """);
    }
}
