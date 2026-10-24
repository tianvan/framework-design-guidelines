using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Members.PointerParameterAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Members;

public sealed class PointerParameterTests
{
    [Fact]
    public Task Pointer_and_multidimensional_array_parameters_of_visible_members_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            public unsafe class Buffer
            {
                public Buffer(byte* {|FDG5102:data|}, int length)
                {
                }

                public Buffer(byte[] data)
                {
                }

                public int this[int[,] {|FDG5102:cell|}] => 0;

                public static Buffer operator +(Buffer left, int[,] {|FDG5102:right|}) => left;

                public void Copy(int*[] {|FDG5102:sources|})
                {
                }

                public void Copy(int[][] sources)
                {
                }

                public void Fill(int[,] {|FDG5102:grid|})
                {
                }

                public void Fill(int[][,] {|FDG5102:grids|}, int value)
                {
                }

                public void Fill(int[][] grid)
                {
                }

                protected void Read(byte* {|FDG5102:destination|})
                {
                }

                protected void Read(byte[] destination)
                {
                }

                internal void Write(byte* source)
                {
                }

                private void Write(int[,] grid)
                {
                }
            }

            public unsafe delegate void Callback(byte* {|FDG5102:data|}, byte[] copy);

            internal unsafe class Hidden
            {
                public void Write(byte* source)
                {
                }
            }

            public sealed unsafe class Closed
            {
                protected internal void Write(byte* source)
                {
                }

                public void Write(byte[] source)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Overrides_and_interface_implementations_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public unsafe interface IReader
            {
                void Read(byte* {|FDG5102:destination|});

                void Read(byte[] destination);
            }

            public unsafe class Reader : IReader
            {
                public void Read(byte* destination)
                {
                }

                public void Read(byte[] destination)
                {
                }

                public virtual void Fill(int[,] {|FDG5102:grid|})
                {
                }
            }

            public class GridReader : Reader
            {
                public override void Fill(int[,] grid)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Function_pointer_parameters_are_reported()
    {
        return Verifier.VerifyLatestAsync(
            """
            public unsafe class Native
            {
                public void Register(delegate*<int, void> {|FDG5102:callback|})
                {
                }

                public void Register(System.Action<int> callback)
                {
                }

                public void {|FDG5124:Unregister|}(delegate*<int, void> {|FDG5102:callback|})
                {
                }
            }
            """);
    }

    [Fact]
    public Task Varargs_members_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            public class Printer
            {
                public {|FDG5123:Printer|}(__arglist)
                {
                }

                public void {|FDG5123:Print|}(string format, __arglist)
                {
                }

                public void Print(string format, params object[] args)
                {
                }

                internal void Trace(__arglist)
                {
                }
            }
            """);
    }

    [Fact]
    public Task Pointer_members_without_an_alternative_overload_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            public unsafe class Encoder
            {
                public Encoder(char* {|FDG5102:table|})
                {
                }

                public Encoder(string table)
                {
                }

                public int GetBytes(char* {|FDG5102:chars|}, int charCount) => 0;

                public int GetBytes(char[] chars) => 0;

                public int {|FDG5124:Encode|}(char* {|FDG5102:chars|}) => 0;

                public int {|FDG5124:Decode|}(byte* {|FDG5102:bytes|}) => 0;

                public int {|FDG5124:Decode|}(byte*[] {|FDG5102:bytes|}, int count) => 0;

                public void Fill(int[,] {|FDG5102:grid|})
                {
                }

                internal int Hash(byte* bytes) => 0;
            }

            public unsafe class Table
            {
                public {|FDG5124:Table|}(byte* {|FDG5102:data|})
                {
                }
            }
            """);
    }

    [Fact]
    public Task Messages_name_the_parameter_and_the_member()
    {
        return Verifier.VerifyWithAsync(
            """
            public unsafe class Encoder
            {
                public int {|#1:Encode|}(char* {|#0:chars|}) => 0;

                public void {|#2:Print|}(__arglist)
                {
                }
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG5102).WithLocation(0).WithArguments("chars", "Encode"),
            new DiagnosticResult(FdgDescriptors.FDG5124).WithLocation(1).WithArguments("Encode"),
            new DiagnosticResult(FdgDescriptors.FDG5123).WithLocation(2).WithArguments("Print"));
    }

    [Fact]
    public Task Generated_code_is_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            // <auto-generated/>
            public unsafe class Encoder
            {
                public int Encode(char* chars) => 0;
            }
            """);
    }
}
