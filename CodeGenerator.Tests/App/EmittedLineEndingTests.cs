using AppScaffolding;
using AppScaffolding.Targets;

namespace CodeGenerator.Tests.App;

/// <summary>
/// An emitted file must not mix line endings.
/// </summary>
/// <remarks>
/// Every template here is a raw string literal, so it carries the CRLF of the generator's own
/// source. A fragment spliced into one with <c>"\n"</c> puts a lone LF in the middle of a CRLF
/// file — which git then renormalises, so a re-scaffold reports a diff against output that is
/// otherwise identical. The repository's own remarks warn about this and it happened anyway, in
/// the auth branch, to every <c>using</c> the login inserted.
/// <para>
/// Asserted across the matrix rather than per template: the defect is in the seam between a
/// fragment and the literal it lands in, so it appears wherever a new one is added, not where the
/// last one was found.
/// </para>
/// </remarks>
[TestClass]
public class EmittedLineEndingTests
{
    private static IEnumerable<(string Target, string File, string Source)> EveryEmittedFile()
    {
        foreach (ITargetScaffolder target in new ITargetScaffolder[]
                 {
                     new WpfTarget(), new WinFormsTarget(), new BlazorTarget(),
                     new ConsoleTarget(), new WebApiTarget(), new WorkerTarget(),
                 })
        {
            foreach (var layout in new[]
                     {
                         AppLayout.Tabs, AppLayout.Sidebar,
                         AppLayout.MasterDetail, AppLayout.Dashboard,
                     })
            {
                foreach (var login in new[] { true, false })
                {
                    var customization = AppCustomization.Default with
                    {
                        Layout = layout,
                        Login = login,
                    };

                    foreach (var file in target.FrontEndFiles("Shop", ["Order"], customization))
                        yield return ($"{target.Key}/{layout}/login={login}", file.Name, file.Source);
                }
            }
        }
    }

    /// <summary>A lone LF in a file whose other lines end CRLF.</summary>
    private static bool MixesEndings(string source)
    {
        var crlf = 0;
        var loneLf = 0;

        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != '\n')
                continue;

            if (i > 0 && source[i - 1] == '\r')
                crlf++;
            else
                loneLf++;
        }

        return crlf > 0 && loneLf > 0;
    }

    [TestMethod]
    public void No_emitted_file_mixes_line_endings()
    {
        var offenders = EveryEmittedFile()
            .Where(f => MixesEndings(f.Source))
            .Select(f => $"{f.Target} -> {f.File}")
            .ToList();

        Assert.IsEmpty(
            offenders,
            "these emitted files mix CRLF and LF:\n  " + string.Join("\n  ", offenders));
    }
}
