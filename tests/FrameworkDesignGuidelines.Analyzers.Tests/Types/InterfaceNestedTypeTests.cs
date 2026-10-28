using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Types.InterfaceNestedTypeAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Types;

public sealed class InterfaceNestedTypeTests
{
    [Fact]
    public Task Visible_type_inside_an_interface_is_reported()
    {
        // C# 8 and later allow types in interfaces.
        return Verifier.VerifyLatestAsync(
            """
            public interface IShape
            {
                public class {|FDG4056:Factory|}
                {
                }

                enum {|FDG4056:Kind|}
                {
                    Circle,
                    Square,
                }
            }
            """);
    }

    [Fact]
    public Task Types_in_classes_and_types_of_hidden_interfaces_are_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            public class Shape
            {
                public class Factory
                {
                }
            }

            internal interface IHidden
            {
                public class Factory
                {
                }
            }

            public interface IShape
            {
                private class Helper
                {
                }
            }
            """);
    }

    [Fact]
    public Task Interfaces_without_nested_types_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public interface IShape
            {
                int Sides { get; }
            }

            public interface IFactory
            {
                IShape Create();
            }
            """);
    }
}
