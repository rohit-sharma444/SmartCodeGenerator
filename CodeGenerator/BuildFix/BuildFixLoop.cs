using System.ComponentModel;
using System.Text.RegularExpressions;
using CodeGenerationEngine.Helper;
using CodeGenerator.App;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerator.BuildFix;

internal sealed record FixOutcome(
    bool Green,
    int Rounds,
    IReadOnlyList<FixEdit> Edits,
    IReadOnlyList<BuildDiagnostic> Remaining,
    IReadOnlyList<string> Notes,
    ProcessResult LastBuild);

/// <summary>
/// Fix, rebuild, repeat. Rules first; the AI only when no rule fixed anything in the round.
/// This is the safety net, so every round is checked against the build that follows it: a round
/// that raises the error count, brings errors into a file that had none, or leaves a failed build
/// whose errors cannot be read is undone, and so is an AI round that does not LOWER the count.
/// A rule or a model that throws, or answers badly, is skipped and never stops the loop.
/// </summary>
internal sealed class BuildFixLoop(IReadOnlyList<IBuildFixRule> rules, IBuildFixAi? ai)
{
    public const int MaxRounds = 3;
    public const int MaxAiAsksPerRound = 5;
    private const int ContextLines = 15;

    // A model that names another file must not have its text pasted into this one.
    private static readonly Regex FileHeader = new(
        @"^\s*(//|/\*|#)?\s*(file|path|filename)\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public FixOutcome Run(
        string projectPath, ProcessResult firstBuild, Func<ProcessResult> rebuild, Action<string>? progress = null)
    {
        progress ??= _ => { };

        var session = new FixSession(projectPath);
        var notes = new List<string>();
        var build = firstBuild;
        var diagnostics = BuildDiagnostics.Parse(build, projectPath);
        var rounds = 0;
        bool? aiUp = null;

        if (!BuildReport.Succeeded(build) && diagnostics.Count == 0)
            notes.Add("The build failed, but no compiler errors could be read from its output, so nothing was changed.");

        while (!BuildReport.Succeeded(build) && rounds < MaxRounds && diagnostics.Count > 0)
        {
            rounds++;
            session.BeginRound(rounds);

            var before = build;
            var beforeDiagnostics = diagnostics;
            var aiRound = false;

            var ruleFixes = ApplyRules(diagnostics, session, progress, notes);

            if (ruleFixes.Count == 0)
            {
                // A rule that wrote and then failed, or wrote and said "no", has left a half edit.
                if (session.Edits.Any(e => e.Round == rounds && !e.Reverted))
                {
                    session.RevertRound(rounds);
                    notes.Add("A fix rule changed a file without finishing, so its change was reverted.");
                }

                if (ai is null)
                {
                    notes.Add("No local AI is set up, so only the fix rules ran.");
                    break;
                }

                aiUp ??= IsUp(ai);

                if (aiUp != true)
                {
                    notes.Add("Ollama is not running, so only the fix rules ran.");
                    break;
                }

                var aiEdits = ApplyAi(diagnostics, session, progress);

                if (aiEdits == 0)
                {
                    notes.Add("The AI had no usable fix for the remaining errors.");
                    break;
                }

                aiRound = true;
                notes.Add($"Round {rounds}: the AI proposed {aiEdits} edit(s).");
            }
            else
            {
                notes.Add($"Round {rounds}: rules fixed {ruleFixes.Count} error(s): {string.Join(", ", ruleFixes.Distinct())}.");
            }

            try
            {
                progress("Building again");
                build = rebuild();
            }
            catch (Win32Exception)
            {
                if (aiRound)
                {
                    session.RevertRound(rounds);
                    notes.Add("Could not start dotnet to build again, so the AI edits could not be checked and were reverted.");
                }
                else
                {
                    notes.Add("Could not start dotnet to build again, so the rule fixes were not checked.");
                }

                break;
            }

            diagnostics = BuildDiagnostics.Parse(build, projectPath);

            if (BuildReport.Succeeded(build))
                break;

            var verdict = Judge(beforeDiagnostics, diagnostics, aiRound);

            if (verdict is not null)
            {
                session.RevertRound(rounds);
                notes.Add($"Round {rounds} was reverted: {verdict}");
                build = before;
                diagnostics = beforeDiagnostics;
                break;
            }

            notes.Add($"Round {rounds}: errors went from {beforeDiagnostics.Count} to {diagnostics.Count}.");
        }

        var green = BuildReport.Succeeded(build);

        if (green)
            notes.Add($"The build is green after {rounds} round(s).");
        else if (rounds >= MaxRounds && diagnostics.Count > 0)
            notes.Add($"Stopped after {MaxRounds} rounds; {diagnostics.Count} error(s) remain.");

        return new FixOutcome(green, rounds, session.Edits, diagnostics, notes, build);
    }

    /// <summary>Why a round made things worse, or null when it did not.</summary>
    private static string? Judge(
        IReadOnlyList<BuildDiagnostic> before, IReadOnlyList<BuildDiagnostic> after, bool aiRound)
    {
        if (after.Count == 0)
            return "the build still fails and no errors could be read from it.";

        if (after.Count > before.Count)
            return $"the errors rose from {before.Count} to {after.Count}.";

        var known = before.Select(d => d.File).Where(f => f.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (after.Any(d => d.File.Length > 0 && !known.Contains(d.File)))
            return "the edit brought errors into a file that had none.";

        if (aiRound && after.Count >= before.Count)
            return $"the AI edits did not reduce the errors ({before.Count} before, {after.Count} after).";

        return null;
    }

    private static bool IsUp(IBuildFixAi ai)
    {
        try
        {
            return ai.IsAvailable();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    private List<string> ApplyRules(
        IReadOnlyList<BuildDiagnostic> diagnostics, FixSession session, Action<string> progress, List<string> notes)
    {
        var fixedBy = new List<string>();

        foreach (var d in diagnostics)
            foreach (var rule in rules)
            {
                try
                {
                    if (!rule.Fix(d, session))
                        continue;

                    fixedBy.Add(rule.Name);
                    progress($"Fixed {d.Code} ({rule.Name})");
                    break;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // A rule that cannot do its job safely is skipped, not allowed to stop the loop.
                    notes.Add($"The rule {rule.Name} failed on {d.Code} ({ex.GetType().Name}) and was skipped.");
                }
            }

        return fixedBy;
    }

    private int ApplyAi(IReadOnlyList<BuildDiagnostic> diagnostics, FixSession session, Action<string> progress)
    {
        var applied = 0;
        var asked = 0;
        var edited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in diagnostics)
        {
            if (asked >= MaxAiAsksPerRound)
                break;

            // Line numbers in the diagnostic belong to the file as it was built; once the file
            // has been edited this round they point at the wrong place.
            if (d.Line < 1 || d.File.Length == 0 || edited.Contains(d.File)
                || !d.File.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                continue;

            string text;
            try
            {
                text = session.Read(d.File);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                continue;
            }

            var lines = text.Split('\n');

            // "a\nb\n" splits into a, b and an empty tail that is not a line of the file.
            var last = lines.Length - 1 - (text.EndsWith('\n') ? 1 : 0);

            if (d.Line - 1 > last)
                continue;

            var lo = Math.Max(0, d.Line - 1 - ContextLines);
            var hi = Math.Min(last, d.Line - 1 + ContextLines);
            var crlf = text.Contains("\r\n", StringComparison.Ordinal);
            var excerpt = string.Join("\n", lines[lo..(hi + 1)].Select(l => l.TrimEnd('\r')));

            asked++;
            progress($"Asking the local AI about {d.Code} in {session.Relative(d.File)}");

            string? reply;
            try
            {
                reply = ai!.Propose(d, excerpt);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(reply))
                continue;

            var clean = reply.Replace("\r\n", "\n");

            if (clean.EndsWith('\n') && !excerpt.EndsWith('\n'))
                clean = clean[..^1];

            if (clean == excerpt || FileHeader.IsMatch(clean.Split('\n')[0]))
                continue;

            // The last line of the file may have no line ending; do not invent one.
            var lastHadCr = lines[hi].EndsWith('\r');
            var newLines = clean.Split('\n');
            var replacement = newLines
                .Select((l, i) => crlf && (i < newLines.Length - 1 || lastHadCr) ? l + "\r" : l)
                .ToArray();

            var rebuilt = string.Join(
                "\n", lines[..lo].Concat(replacement).Concat(lines[(hi + 1)..]));

            if (rebuilt == text || SyntaxErrors(rebuilt) > SyntaxErrors(text))
                continue;

            try
            {
                session.Write(d.File, rebuilt, "AI");
                edited.Add(d.File);
                applied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
            }
        }

        return applied;
    }

    /// <summary>
    /// Syntax errors in the whole file. An edit is refused only when it ADDS some: the file may
    /// already have other broken places this round is not about.
    /// </summary>
    private static int SyntaxErrors(string source)
        => CSharpSyntaxTree.ParseText(source).GetDiagnostics().Count(x => x.Severity == DiagnosticSeverity.Error);
}
