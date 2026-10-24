using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.TupleSignatureAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class TupleSignatureTests
{
    [Fact]
    public Task Unnamed_tuples_in_visible_signatures_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections.Generic;

            public class People
            {
                public (int, int)[] {|FDG5128:Pairs|};

                private (int, int) hidden;

                public IEnumerable<(string, int)> {|FDG5128:All|} => null;

                public Tuple<string, int> {|FDG5128:GetPerson|}() => null;

                public ValueTuple<string, int> {|FDG5128:GetPair|}() => default;

                public (string, int) {|FDG5128:GetTuple|}() => default;

                public (string Name, int) {|FDG5128:GetPartial|}() => default;

                public (int Offset, int Length) GetOffsetAndLength(int length) => default;

                internal (int, int) Hidden() => default;
            }

            public class Base
            {
                public virtual (int, int) {|FDG5128:Get|}() => default;
            }

            public class Derived : Base
            {
                public override (int, int) Get() => default;
            }
            """);
    }

    [Fact]
    public Task Tuple_element_names_that_are_not_PascalCase_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System.Collections.Generic;

            public class Ranges
            {
                public (int Offset, int Length) Range;

                public List<(string {|FDG5129:name|}, int Age)> People { get; } = null;

                public (int {|FDG5129:offset|}, int Length) GetOffsetAndLength(int length) => default;

                internal (int offset, int length) Hidden() => default;
            }
            """);
    }

    [Fact]
    public Task Tuples_with_more_than_three_fields_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Threading.Tasks;

            public class People
            {
                public (string FirstName, string LastName, int Age) GetPerson() => default;

                public (string FirstName, string LastName, int Age, string City) {|FDG5130:GetAddress|}() => default;

                public Task<(int A, int B, int C, int D, int E, int F, int G, int H)> {|FDG5130:GetMany|}() => null;

                public Tuple<int, int, int, int> {|FDG5128:{|FDG5130:GetQuad|}|}() => null;

                internal (int A, int B, int C, int D) Hidden() => default;
            }
            """);
    }

    [Fact]
    public Task Tuple_parameters_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections.Generic;

            public class People
            {
                public {|FDG5128:People|}(Tuple<int, int> {|FDG5131:range|})
                {
                }

                public int this[(int Row, int Column) {|FDG5131:cell|}] => 0;

                public void Add((string FirstName, string LastName) {|FDG5131:person|})
                {
                }

                public void AddAll((string FirstName, int Age)[] {|FDG5131:people|})
                {
                }

                public static T Max<T>(IComparer<T> comparer, T first, T second) => first;

                public static (int Base, int Exponent) Max(IComparer<(int Base, int Exponent)> {|FDG5131:comparer|}) =>
                    default;

                internal void Hidden((int X, int Y) point)
                {
                }
            }

            public delegate void Handler((int X, int Y) {|FDG5131:point|});

            public class PowerComparer : IComparer<(int Base, int Exponent)>
            {
                public int Compare((int Base, int Exponent) x, (int Base, int Exponent) y) => 0;
            }
            """);
    }

    [Fact]
    public Task Extension_methods_over_tuples_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            using System;
            using System.Collections.Generic;

            public static class Vectors
            {
                public static int {|FDG5132:DotProduct|}(
                    this (int X, int Y) first, (int X, int Y) {|FDG5131:second|}) =>
                    first.X * second.X + first.Y * second.Y;

                public static int {|FDG5128:{|FDG5132:Sum|}|}(this Tuple<int, int> pair) => pair.Item1 + pair.Item2;

                public static int Count(this IEnumerable<(int X, int Y)> {|FDG5131:points|}) => 0;

                internal static int Hidden(this (int X, int Y) value) => value.X;
            }
            """);
    }

    [Fact]
    public Task Messages_name_the_tuple_and_the_member()
    {
        return Verifier.VerifyWithAsync(
            """
            public static class Vectors
            {
                public static (int, int) {|#0:Pair|}() => default;

                public static (int {|#1:x|}, int Y) Point() => default;

                public static (int A, int B, int C, int D) {|#2:Quad|}() => default;

                public static int {|#3:Length|}(this (int X, int Y) vector, (int X, int Y) {|#4:other|}) => 0;
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5128).WithLocation(0).WithArguments("(int, int)", "Pair"),
            new DiagnosticResult(FdgDescriptors.FDG5129).WithLocation(1).WithArguments("x", "Point"),
            new DiagnosticResult(FdgDescriptors.FDG5130).WithLocation(2).WithArguments(4, "Quad"),
            new DiagnosticResult(FdgDescriptors.FDG5132).WithLocation(3).WithArguments("Length", "(int X, int Y)"),
            new DiagnosticResult(FdgDescriptors.FDG5131).WithLocation(4).WithArguments("other", "Length"));
    }

    [Fact]
    public Task Generated_code_is_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            // <auto-generated/>
            public class People
            {
                public (int, int) Get() => default;
            }
            """);
    }
}
