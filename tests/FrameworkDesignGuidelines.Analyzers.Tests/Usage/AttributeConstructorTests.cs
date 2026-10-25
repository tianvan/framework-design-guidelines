using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Usage.AttributeConstructorAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Usage;

public sealed class AttributeConstructorTests
{
    [Fact]
    public Task Attributes_with_more_than_one_usable_constructor_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            [AttributeUsage(AttributeTargets.All)]
            public sealed class {|FDG8011:NameAttribute|} : Attribute
            {
                public NameAttribute(string userName) { }
                public NameAttribute(string userName, int age) { }
            }

            [AttributeUsage(AttributeTargets.All)]
            internal sealed class {|FDG8011:TraceAttribute|} : Attribute
            {
                public TraceAttribute() { }
                internal TraceAttribute(string category) { }
            }

            [AttributeUsage(AttributeTargets.All)]
            public abstract class RuleAttribute : Attribute
            {
                protected RuleAttribute() { }
                public RuleAttribute(string name) { }
                public RuleAttribute(int id) { }
            }

            [AttributeUsage(AttributeTargets.All)]
            public class {|FDG8011:CheckAttribute|} : RuleAttribute
            {
                public CheckAttribute() { }
                protected internal CheckAttribute(int id) : base(id) { }
            }

            public class Options
            {
                public Options() { }
                public Options(int count) { }
            }
            """);
    }

    [Fact]
    public Task Attributes_with_a_single_usable_constructor_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            [AttributeUsage(AttributeTargets.All)]
            public sealed class NameAttribute : Attribute
            {
                private int _age;

                public NameAttribute(string userName)
                {
                    UserName = userName;
                }

                public string UserName { get; }

                public int Age
                {
                    get { return _age; }
                    set { _age = value; }
                }
            }

            [AttributeUsage(AttributeTargets.All)]
            public sealed class MarkerAttribute : Attribute
            {
            }

            [AttributeUsage(AttributeTargets.All)]
            public class StepAttribute : Attribute
            {
                public StepAttribute(string name) { }
                private StepAttribute() { }
                protected StepAttribute(int order) { }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_attribute()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;

            public sealed class {|#0:NameAttribute|} : Attribute
            {
                public NameAttribute() { }
                public NameAttribute(string userName) { }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG8011).WithLocation(0).WithArguments("NameAttribute"));
    }
}
