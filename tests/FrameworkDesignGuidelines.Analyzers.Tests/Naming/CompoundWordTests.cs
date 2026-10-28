using Microsoft.CodeAnalysis.Testing;
using Verifier = FrameworkDesignGuidelines.Analyzers.Tests.Verifiers.AnalyzerVerifier<
    FrameworkDesignGuidelines.Analyzers.Naming.CompoundWordAnalyzer>;

namespace FrameworkDesignGuidelines.Analyzers.Tests.Naming;

public sealed class CompoundWordTests
{
    [Fact]
    public Task Book_not_forms_are_reported()
    {
        return Verifier.VerifyAsync(
            """
            namespace {|FDG3006:NameSpace|}.Tools
            {
                public delegate void {|FDG3006:CallBack|}(int {|FDG3006:userName|});

                public class {|FDG3006:HashTable|}
                {
                    public string {|FDG3006:FileName|};

                    public int {|FDG3006:ID|} { get; set; }

                    public bool {|FDG3006:IsWriteable|} { get; set; }

                    public void {|FDG3006:LogIn|}(string {|FDG3006:eMail|})
                    {
                    }

                    public void Read(int {|FDG3006:metaData|}, long {|FDG3006:timeStamp|})
                    {
                    }
                }

                public enum State
                {
                    {|FDG3006:Cancelled|},
                    {|FDG3006:OK|},
                }
            }
            """);
    }

    [Fact]
    public Task Book_pascal_forms_and_other_words_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            namespace Namespace.Tools
            {
                public delegate void Callback(int username);

                public class Hashtable
                {
                    public string Filename;

                    public int Id { get; set; }

                    public int UserId { get; set; }

                    public bool IsWritable { get; set; }

                    public string Identity;

                    public string Okay;

                    public string Pipeline;

                    public void LogOut()
                    {
                    }

                    public void Logon(string email)
                    {
                    }

                    public void Read(int metadata, long timestamp, string filename)
                    {
                    }
                }

                public enum State
                {
                    Canceled,
                    Ok,
                }
            }
            """);
    }

    [Fact]
    public Task Log_out_is_a_verb_in_a_method_name_only()
    {
        return Verifier.VerifyAsync(
            """
            public class Session
            {
                public bool {|FDG3006:IsLogOut|} { get; set; }

                public void LogOut()
                {
                }

                public void {|FDG3006:LogInAsync|}()
                {
                }
            }
            """);
    }

    [Fact]
    public Task Names_that_are_not_externally_visible_or_are_inherited_are_not_reported()
    {
        return Verifier.VerifyAsync(
            """
            internal class HashTable
            {
                public string FileName;
            }

            public abstract class Base
            {
                private string fileName;

                internal int ID;

                public abstract void Save(string {|FDG3006:fileName|});
            }

            public sealed class Derived : Base
            {
                public override void Save(string fileName)
                {
                    string userName = fileName;
                }
            }
            """);
    }

    [Fact]
    public Task Message_names_the_found_and_the_book_form()
    {
        return Verifier.VerifyWithAsync(
            """
            public class Account
            {
                public void Rename(string {|#0:userName|})
                {
                }

                public string {|#1:FileName|};
            }
            """,
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(0)
                .WithArguments("userName", "userName", "username"),
            new DiagnosticResult(FdgDescriptors.FDG3006).WithLocation(1)
                .WithArguments("FileName", "FileName", "Filename"));
    }
}
