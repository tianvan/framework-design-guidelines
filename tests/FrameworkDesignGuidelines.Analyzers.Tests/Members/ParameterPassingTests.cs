using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.ParameterPassingAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class ParameterPassingTests
{
    [Fact]
    public Task Out_parameters_before_other_parameters_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public class Parser
            {
                public static bool TryParse(string input, out int result)
                {
                    result = 0;
                    return true;
                }

                public static bool TryParse(string input, IFormatProvider provider, out int result, out int length)
                {
                    result = 0;
                    length = 0;
                    return true;
                }

                public static bool TryParse(out int {|FDG5103:result|}, string input)
                {
                    result = 0;
                    return true;
                }

                public static void Split(string input, out string {|FDG5103:head|}, ref int position, out string tail)
                {
                    head = null;
                    tail = null;
                }

                public static void Read(out int count, params string[] names)
                {
                    count = 0;
                }

                public Parser(out int {|FDG5103:state|}, int seed)
                {
                    state = seed;
                }

                internal static void Hidden(out int result, string input)
                {
                    result = 0;
                }
            }

            public delegate void Reader(out int {|FDG5103:value|}, string input);
            """);
    }

    [Fact]
    public Task Members_with_two_or_more_Boolean_parameters_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.IO;

            public class Files
            {
                public static Stream {|FDG5105:Open|}(string path, bool caseSensitive, bool create) => null;

                public static Stream Open(string path, bool create) => null;

                public static bool Read(string path, bool create, out bool exists, out bool empty)
                {
                    exists = false;
                    empty = false;
                    return true;
                }

                public bool this[int index, bool wrap] => false;

                public bool {|FDG5105:this|}[bool wrap, bool clamp] => false;

                internal static void Hidden(bool first, bool second)
                {
                }
            }

            public delegate void {|FDG5105:Toggle|}(bool on, bool animate);

            public interface IFilter
            {
                void {|FDG5105:Apply|}(bool invert, bool clamp);
            }

            public class Filter : IFilter
            {
                public void Apply(bool invert, bool clamp)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Constructors_whose_Boolean_parameters_initialize_Boolean_properties_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class Options
            {
                public Options(bool isGlobal, bool isPartial)
                {
                    IsGlobal = isGlobal;
                    IsPartial = isPartial;
                }

                public {|FDG5105:Options|}(bool isGlobal, bool recursive, int depth)
                {
                    IsGlobal = isGlobal;
                }

                public {|FDG5105:Options|}(bool isGlobal, string isPartial, bool depth)
                {
                    IsGlobal = isGlobal;
                }

                public bool IsGlobal { get; }

                public bool IsPartial { get; }

                public int Depth { get; }

                public void {|FDG5105:Update|}(bool isGlobal, bool isPartial)
                {
                }
            }

            public class Derived : Options
            {
                public Derived(bool isGlobal, bool isPartial) : base(isGlobal, isPartial)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Positional_records_with_Boolean_properties_are_not_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            public record Flags(bool IsGlobal, bool IsPartial);
            """);
    }

    [Fact]
    public Task Reference_types_passed_by_reference_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Collections.Generic;

            public static class Reference
            {
                public static void Swap<T>(ref T first, ref T second)
                {
                }

                public static void Swap(ref string first, ref string second)
                {
                }

                public static void Replace(ref string {|FDG5114:value|}, string replacement)
                {
                }

                public static void Replace(ref List<int> {|FDG5114:list|}, ref int[] {|FDG5114:items|})
                {
                }

                public static int Count(in string {|FDG5114:value|}) => 0;

                public static void Compare(in string {|FDG5114:first|}, in string {|FDG5114:second|})
                {
                }

                public static void Constrained<T>(ref T {|FDG5114:value|})
                    where T : class
                {
                }

                public static void Increment(ref int count)
                {
                }

                public static bool TryGet(out string value)
                {
                    value = null;
                    return true;
                }

                internal static void Hidden(ref string value)
                {
                }
            }

            public class Holder
            {
                public virtual void Store(ref object {|FDG5114:value|})
                {
                }
            }

            public class DerivedHolder : Holder
            {
                public override void Store(ref object value)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Value_types_passed_by_read_only_reference_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;

            public struct Matrix
            {
                public static Matrix operator *(in Matrix {|FDG5115:left|}, in Matrix {|FDG5115:right|}) => left;

                public static double Determinant(in Matrix {|FDG5115:matrix|}) => 0;

                public static void Scale<T>(in T {|FDG5115:value|})
                    where T : struct
                {
                }

                public static void Print<T>(in T value)
                {
                }

                public static void Print(in DayOfWeek {|FDG5115:day|})
                {
                }

                public static void Update(ref Matrix matrix)
                {
                }

                internal static double Trace(in Matrix matrix) => 0;
            }
            """);
    }

    [Fact]
    public Task Ref_readonly_parameters_are_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            public struct Point
            {
                public static int Length(ref readonly Point {|FDG5115:point|}) => 0;

                public static int Length(ref readonly string {|FDG5114:text|}) => 0;
            }
            """);
    }

    [Fact]
    public Task Messages_name_the_parameter_and_the_member()
    {
        return Verifier.VerifyWithAsync(
            """
            public struct Matrix
            {
                public Matrix(out int {|#0:state|}, int seed)
                {
                    state = seed;
                }

                public void {|#1:Open|}(bool create, bool share, bool cache)
                {
                }

                public static void Replace(ref string {|#2:value|}, string replacement)
                {
                }

                public static Matrix operator -(in Matrix {|#3:left|}, Matrix right) => left;
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5103).WithLocation(0).WithArguments("state", "Matrix"),
            new DiagnosticResult(FdgDescriptors.FDG5105).WithLocation(1).WithArguments(3, "Open"),
            new DiagnosticResult(FdgDescriptors.FDG5114).WithLocation(2).WithArguments("value", "Replace"),
            new DiagnosticResult(FdgDescriptors.FDG5115).WithLocation(3).WithArguments("left", "operator -"));
    }
}
