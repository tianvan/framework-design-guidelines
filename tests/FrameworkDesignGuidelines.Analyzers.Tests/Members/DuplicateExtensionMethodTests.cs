using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.DuplicateExtensionMethodAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class DuplicateExtensionMethodTests
{
    [Fact]
    public Task Extension_methods_with_the_same_signature_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Collections.Generic;

            namespace A
            {
                public static class AExtensions
                {
                    public static void {|FDG5077:ExtensionMethod|}(this string value)
                    {
                    }

                    public static T {|FDG5077:Smallest|}<T>(this List<T> list, int start) => list[start];
                }
            }

            namespace B
            {
                public static class BExtensions
                {
                    public static void {|FDG5077:ExtensionMethod|}(this string value)
                    {
                    }

                    public static TItem {|FDG5077:Smallest|}<TItem>(this List<TItem> items, int from) => items[from];
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_other_class()
    {
        return Verifier.VerifyWithAsync(
            """
            namespace A
            {
                public static class AExtensions
                {
                    public static void {|#0:ExtensionMethod|}(this string value)
                    {
                    }
                }
            }

            namespace B
            {
                public static class BExtensions
                {
                    public static void {|#1:ExtensionMethod|}(this string value)
                    {
                    }
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5077)
                .WithLocation(0)
                .WithArguments("ExtensionMethod", "BExtensions"),
            new DiagnosticResult(FdgDescriptors.FDG5077)
                .WithLocation(1)
                .WithArguments("ExtensionMethod", "AExtensions"));
    }

    [Fact]
    public Task Message_names_the_first_other_class_by_full_name()
    {
        return Verifier.VerifyWithAsync(
            """
            namespace C
            {
                public static class CExtensions
                {
                    public static void {|#2:ExtensionMethod|}(this string value)
                    {
                    }
                }
            }

            namespace B
            {
                public static class BExtensions
                {
                    public static void {|#1:ExtensionMethod|}(this string value)
                    {
                    }
                }
            }

            namespace A
            {
                public static class AExtensions
                {
                    public static void {|#0:ExtensionMethod|}(this string value)
                    {
                    }
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5077)
                .WithLocation(2)
                .WithArguments("ExtensionMethod", "AExtensions"),
            new DiagnosticResult(FdgDescriptors.FDG5077)
                .WithLocation(1)
                .WithArguments("ExtensionMethod", "AExtensions"),
            new DiagnosticResult(FdgDescriptors.FDG5077)
                .WithLocation(0)
                .WithArguments("ExtensionMethod", "BExtensions"));
    }

    [Fact]
    public Task Different_signatures_and_hidden_methods_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Collections.Generic;

            namespace A
            {
                public static class AExtensions
                {
                    public static void ExtensionMethod(this string value)
                    {
                    }

                    public static void Other(this string value, int count)
                    {
                    }

                    public static T Smallest<T>(this List<T> list) => list[0];

                    public static void Hidden(this string value)
                    {
                    }

                    public static void Plain(string value)
                    {
                    }
                }
            }

            namespace B
            {
                public static class BExtensions
                {
                    public static void ExtensionMethod(this object value)
                    {
                    }

                    public static void Other(this string value, string text)
                    {
                    }

                    public static int Smallest(this List<int> list) => list[0];

                    internal static void Hidden(this string value)
                    {
                    }

                    public static void Plain(string value)
                    {
                    }
                }
            }
            """);
    }
}
