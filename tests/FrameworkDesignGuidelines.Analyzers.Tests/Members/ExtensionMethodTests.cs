using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.ExtensionMethodAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class ExtensionMethodTests
{
    [Fact]
    public Task Extension_method_on_object_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            namespace Library.Formatting
            {
                public static class Describing
                {
                    public static string {|FDG5074:Describe|}(this object value) => value.ToString();

                    public static string Shout(this string value) => value.ToUpperInvariant();

                    public static string Describe<T>(this T value) => value.ToString();

                    public static string Format(object value) => value.ToString();
                }

                internal static class Hidden
                {
                    public static string Describe(this object value) => value.ToString();
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_method()
    {
        return Verifier.VerifyWithAsync(
            """
            namespace Library
            {
                public static class Describing
                {
                    public static string {|#0:Describe|}(this object value) => value.ToString();
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5074).WithLocation(0).WithArguments("Describe"));
    }

    [Fact]
    public Task Class_named_for_the_extended_type_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            namespace Library.Text
            {
                public static class {|FDG5075:StringExtensions|}
                {
                    public static bool IsPalindrome(this string value) => true;
                }

                public static class Palindromes
                {
                    public static bool IsPalindrome(this char[] value) => true;
                }

                public interface IRoute
                {
                }

                public static class {|FDG5075:RouteExtensions|}
                {
                    public static bool IsShort(this IRoute route) => true;
                }

                public static class {|FDG5075:Extensions|}
                {
                    public static bool IsLong(this IRoute route) => true;
                }

                public static class PalindromeStringExtensions
                {
                    public static bool IsPalindrome(this string value, bool ignoreCase) => true;
                }

                public static class TextExtensions
                {
                    public static bool IsWord(this string value) => true;
                }

                public static class HelperExtensions
                {
                    public static bool IsEmpty(string value) => value.Length == 0;
                }

                internal static class CharExtensions
                {
                    public static bool IsVowel(this char value) => true;
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_class()
    {
        return Verifier.VerifyWithAsync(
            """
            namespace Library
            {
                public static class {|#0:StringExtensions|}
                {
                    public static bool IsPalindrome(this string value) => true;
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5075).WithLocation(0).WithArguments("StringExtensions"));
    }

    [Fact]
    public Task Extension_method_in_the_namespace_of_its_own_type_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            namespace Library.Routing
            {
                public class Route
                {
                }

                public interface IRoute
                {
                }

                public class Table<T>
                {
                }

                public static class Layout
                {
                    public static void {|FDG5076:Optimize|}(this Route route)
                    {
                    }

                    public static void {|FDG5076:Clear|}<T>(this Table<T> table)
                    {
                    }

                    public static void Describe(this IRoute route)
                    {
                    }

                    public static int Sum(this Table<int> table) => 0;

                    public static void DisposeAll<T>(this Table<T> table)
                        where T : IDisposable
                    {
                    }

                    public static void Shout(this string value)
                    {
                    }
                }

                internal static class Hidden
                {
                    public static void Optimize(this Route route)
                    {
                    }
                }
            }

            namespace Library.Planning
            {
                using Library.Routing;

                public static class Planner
                {
                    public static void Plan(this Route route)
                    {
                    }
                }
            }

            namespace System
            {
                public static class Texts
                {
                    public static void Whisper(this string value)
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_method_and_the_extended_type()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Route
            {
            }

            public static class Layout
            {
                public static void {|#0:Optimize|}(this Route route)
                {
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5076).WithLocation(0).WithArguments("Optimize", "Route"));
    }
}
