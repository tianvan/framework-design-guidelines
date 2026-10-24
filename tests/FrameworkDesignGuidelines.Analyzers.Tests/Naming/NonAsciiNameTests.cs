using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Naming.NonAsciiNameAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Naming;

public sealed class NonAsciiNameTests
{
    [Fact]
    public Task Non_ascii_names_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            namespace {|FDG3013:Edelweiß|}
            {
                public class {|FDG3013:Résumé|}
                {
                    public int {|FDG3013:Mêlée|};

                    public bool {|FDG3013:Naïve|} { get; set; }

                    public void {|FDG3013:Röntgen|}<{|FDG3013:TÉlément|}>(int {|FDG3013:größe|})
                    {
                    }
                }

                public delegate void Callback(int {|FDG3013:données|});

                public enum Mode
                {
                    {|FDG3013:Défaut|},
                }
            }
            """);
    }

    [Fact]
    public Task Book_replacement_spellings_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            namespace Edelweiss
            {
                public class Resume
                {
                    public int Melee;

                    public bool Naive { get; set; }

                    public void Roentgen(int size)
                    {
                        string café = "Résumé";
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Names_that_are_not_externally_visible_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            namespace Internes
            {
                internal class Résumé
                {
                    public int Mêlée;
                }

                public class C
                {
                    private int naïve;

                    protected internal int Visible;

                    private protected void Röntgen()
                    {
                    }

                    internal void Größe(int größe)
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public Task Overrides_take_the_base_name_and_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            public abstract class Base
            {
                public abstract void {|FDG3013:Röntgen|}(int {|FDG3013:größe|});
            }

            public sealed class Derived : Base
            {
                public override void Röntgen(int größe)
                {
                }
            }
            """);
    }
}
