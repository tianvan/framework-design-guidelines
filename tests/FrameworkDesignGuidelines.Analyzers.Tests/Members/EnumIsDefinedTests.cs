using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.EnumIsDefinedAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class EnumIsDefinedTests
{
    [Fact]
    public Task Enum_IsDefined_range_checks_of_arguments_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public enum Color
            {
                Red = 1,
                Green = 2,
            }

            public class Painter
            {
                public void PickColor(Color color)
                {
                    if (!{|FDG5111:Enum.IsDefined(typeof(Color), color)|})
                    {
                        throw new ArgumentOutOfRangeException(nameof(color));
                    }
                }

                public void Pick(Color color, int size)
                {
                    if (size < 0 || !({|FDG5111:Enum.IsDefined(typeof(Color), color)|}))
                        throw new ArgumentException("Invalid.", nameof(color));
                }

                public void Paint(Color color)
                {
                    if ({|FDG5111:Enum.IsDefined(typeof(Color), color)|})
                    {
                        Console.WriteLine(color);
                    }
                    else
                    {
                        Console.WriteLine("Unknown");
                        throw new ArgumentOutOfRangeException(nameof(color));
                    }
                }

                public Color Checked(Color color)
                {
                    return {|FDG5111:Enum.IsDefined(typeof(Color), color)|}
                        ? color
                        : throw new ArgumentOutOfRangeException(nameof(color));
                }

                private static void Validate(object value)
                {
                    if (!{|FDG5111:Enum.IsDefined(typeof(Color), value)|})
                    {
                        throw new ArgumentOutOfRangeException(nameof(value));
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Enum_IsDefined_calls_that_are_not_argument_range_checks_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public enum Color
            {
                Red = 1,
                Green = 2,
            }

            public class Painter
            {
                private Color current;

                public string Describe(Color color)
                {
                    if (Enum.IsDefined(typeof(Color), color))
                    {
                        return color.ToString();
                    }

                    return "Unknown";
                }

                public bool IsKnown(Color color) => Enum.IsDefined(typeof(Color), color);

                public void Reset()
                {
                    if (!Enum.IsDefined(typeof(Color), current))
                    {
                        throw new InvalidOperationException();
                    }

                    Color local = Color.Red;
                    if (!Enum.IsDefined(typeof(Color), local))
                    {
                        throw new InvalidOperationException();
                    }
                }

                public void Pick(Color color)
                {
                    if (color < Color.Red || color > Color.Green)
                    {
                        throw new ArgumentOutOfRangeException(nameof(color));
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Generic_Enum_IsDefined_is_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Painter
            {
                public void PickDay(DayOfWeek day)
                {
                    if (!{|FDG5111:Enum.IsDefined<DayOfWeek>(day)|})
                    {
                        throw new ArgumentOutOfRangeException(nameof(day));
                    }
                }
            }
            """,
            ReferenceAssemblies.Net.Net80,
            Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7_3);
    }

    [Fact]
    public Task Message_names_the_argument()
    {
        return Verifier.VerifyWithAsync(
            """
            using System;

            public class Painter
            {
                public void PickDay(DayOfWeek day)
                {
                    if (!{|#0:Enum.IsDefined(typeof(DayOfWeek), day)|})
                    {
                        throw new ArgumentOutOfRangeException(nameof(day));
                    }
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5111).WithLocation(0).WithArguments("day"));
    }
}
