using System.Text.RegularExpressions;
using CodeGenerationEngine.Helper;
using CodeGenerator.App;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeGenerator.BuildFix;

/// <param name="BuildChecked">
/// False only when the fix stopped in a way that left no build to trust (the last-resort catch):
/// <see cref="LastBuild"/> is then the build the fix STARTED from, not the state of the files.
/// </param>
internal sealed record FixOutcome(
    bool Green,
    int Rounds,
    IReadOnlyList<FixEdit> Edits,
    IReadOnlyList<BuildDiagnostic> Remaining,
    IReadOnlyList<string> Notes,
    ProcessResult LastBuild,
    bool BuildChecked = true);

/// <summary>
/// Fix, rebuild, repeat. Rules first; the AI only when no rule fixed anything in the round.
/// This is the safety net, so every round is checked against the build that follows it: a round
/// that raises the error count, brings errors into a file that had none, or leaves a failed build
/// whose errors cannot be read is undone, and so is an AI round that does not LOWER the count.
/// A rule or a model that throws, or answers badly, is skipped and never stops the loop.
/// <para>
/// The whole run has a time budget (10 minutes unless told otherwise) and can be cancelled. Both are
/// checked before each round, each AI ask and each rebuild; a stop undoes the round whose edits no
/// build has checked, and the notes say which of the two it was.
/// </para>
/// </summary>
internal sealed class BuildFixLoop(
    IReadOnlyList<IBuildFixRule> rules, IBuildFixAi? ai, TimeSpan? budget = null, Func<DateTime>? clock = null)
{
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromMinutes(10);

    public const string TimeLimitNote = "The time limit was reached.";
    public const string CancelledNote = "The fix was cancelled.";

    private readonly TimeSpan _budget = budget ?? DefaultBudget;
    private readonly Func<DateTime> _clock = clock ?? (() => DateTime.UtcNow);
    private DateTime _started;
    private CancellationToken _ct;
    private string? _stopNote;
    private bool _aiTimedOut;

    public const int MaxRounds = 3;
    public const int MaxAiAsksPerRound = 5;
    private const int ContextLines = 15;

    /// <summary>A file this short is sent whole.</summary>
    internal const int WholeFileLines = 150;

    /// <summary>The largest member or type sent as one piece from a longer file.</summary>
    internal const int MaxUnitLines = 150;

    // A model that names another file must not have its text pasted into this one.
    private static readonly Regex FileHeader = new(
        @"^\s*(//|/\*|#)?\s*(file|path|filename)\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The session of the latest <see cref="Run"/>, or null before it starts.</summary>
    internal FixSession? LastSession { get; private set; }

    /// <summary>Why the run must stop now, or null. Cancelling wins over the clock.</summary>
    private string? ShouldStop()
    {
        if (_ct.IsCancellationRequested)
            return CancelledNote;

        return _clock() - _started >= _budget ? TimeLimitNote : null;
    }

    public FixOutcome Run(
        string projectPath, ProcessResult firstBuild, Func<ProcessResult> rebuild, Action<string>? progress = null,
        CancellationToken ct = default)
    {
        progress ??= _ => { };
        _ct = ct;
        _started = _clock();
        _stopNote = null;
        _aiTimedOut = false;

        var session = new FixSession(projectPath);
        LastSession = session;
        var notes = new List<string>();
        var build = firstBuild;
        IReadOnlyList<BuildDiagnostic> diagnostics = [];
        var rounds = 0;

        // What the user saw when the current round began: where an undone round leaves things.
        var roundBuild = firstBuild;
        IReadOnlyList<BuildDiagnostic> roundDiagnostics = [];

        try
        {
            return Loop();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever failed, edits that no build has checked are not kept.
            var undone = rounds == 0 || Revert(session, rounds, notes);

            notes.Add(rounds == 0
                ? $"The fix stopped ({ex.GetType().Name}) before it changed anything."
                : undone
                    ? $"The fix stopped ({ex.GetType().Name}), so round {rounds} was undone."
                    : $"The fix stopped ({ex.GetType().Name}), and round {rounds} could not be fully undone.");

            return new FixOutcome(false, rounds, session.Edits, roundDiagnostics, notes, roundBuild);
        }

        FixOutcome Loop()
        {
            bool? aiUp = null;
            diagnostics = BuildDiagnostics.Parse(build, projectPath);
            roundDiagnostics = diagnostics;

            if (!BuildReport.Succeeded(build) && diagnostics.Count == 0)
                notes.Add("The build failed, but no compiler errors could be read from its output, so nothing was changed.");

            while (!BuildReport.Succeeded(build) && rounds < MaxRounds && diagnostics.Count > 0)
            {
                // Nothing is unchecked at the top of a round: the last one has been judged.
                if (ShouldStop() is { } stopNow)
                {
                    notes.Add(stopNow);
                    break;
                }

                rounds++;
                session.BeginRound(rounds);

                var before = build;
                var beforeDiagnostics = diagnostics;
                roundBuild = before;
                roundDiagnostics = beforeDiagnostics;
                var aiRound = false;

                var ruleFixes = ApplyRules(diagnostics, session, progress, notes);

                if (ruleFixes.Count == 0)
                {
                    if (ai is null)
                    {
                        notes.Add("No local AI is set up, so only the fix rules ran.");
                        break;
                    }

                    if (_aiTimedOut)
                    {
                        notes.Add("The AI timed out earlier, so it was not asked again.");
                        break;
                    }

                    aiUp ??= IsUp(ai);

                    if (aiUp != true)
                    {
                        notes.Add("Ollama is not running, so only the fix rules ran.");
                        break;
                    }

                    var aiEdits = ApplyAi(diagnostics, session, progress, notes);

                    if (_stopNote is not null)
                    {
                        // Stopped between two asks: what this round wrote has not been built.
                        StopRound(session, rounds, aiEdits > 0, _stopNote, notes);
                        break;
                    }

                    if (aiEdits == 0)
                    {
                        notes.Add(_aiTimedOut
                            ? "The AI timed out, so it will not be asked again."
                            : "The AI had no usable fix for the remaining errors.");
                        break;
                    }

                    aiRound = true;
                    notes.Add($"Round {rounds}: the AI proposed {aiEdits} edit(s).");
                }
                else
                {
                    notes.Add($"Round {rounds}: rules fixed {ruleFixes.Count} error(s): {string.Join(", ", ruleFixes.Distinct())}.");
                }

                if (ShouldStop() is { } stopBeforeBuild)
                {
                    StopRound(session, rounds, true, stopBeforeBuild, notes);
                    break;
                }

                try
                {
                    progress("Building again");
                    build = rebuild();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Edits that no build has checked are not kept, whoever made them.
                    notes.Add(Revert(session, rounds, notes)
                        ? $"The rebuild with dotnet failed ({ex.GetType().Name}), so round {rounds} was reverted: its edits could not be checked."
                        : $"The rebuild with dotnet failed ({ex.GetType().Name}), and round {rounds} could not be fully undone.");
                    break;
                }

                // A build that ended because the user cancelled says nothing about the edits.
                if (_ct.IsCancellationRequested && !BuildReport.Succeeded(build))
                {
                    StopRound(session, rounds, true, CancelledNote, notes);
                    build = before;
                    break;
                }

                diagnostics = BuildDiagnostics.Parse(build, projectPath);

                if (BuildReport.Succeeded(build))
                    break;

                var verdict = Judge(beforeDiagnostics, diagnostics, aiRound);

                if (verdict is not null)
                {
                    notes.Add(Revert(session, rounds, notes)
                        ? $"Round {rounds} was reverted: {verdict}"
                        : $"Round {rounds} could not be fully undone: {verdict}");
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
    }

    /// <summary>Undoes a round. False, with a note per file, when any file could not be put back.</summary>
    private static bool Revert(FixSession session, int round, List<string> notes)
    {
        var failed = session.RevertRound(round);

        foreach (var file in failed)
            notes.Add($"{file} was not restored; check it by hand.");

        return failed.Count == 0;
    }

    /// <summary>Ends the run at a stop request: says why, and undoes the round when it has unchecked edits.</summary>
    private static void StopRound(FixSession session, int round, bool hasEdits, string why, List<string> notes)
    {
        notes.Add(why);

        if (!hasEdits)
            return;

        notes.Add(Revert(session, round, notes)
            ? $"Round {round} was undone because its edits were not checked."
            : $"Round {round} could not be fully undone.");
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

    private static bool TimedOut(IBuildFixAi? ai)
    {
        try
        {
            return ai?.LastTimedOut == true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
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
                var mark = session.Mark();

                try
                {
                    if (rule.Fix(d, session))
                    {
                        fixedBy.Add(rule.Name);
                        progress($"Fixed {d.Code} ({rule.Name})");
                        break;
                    }

                    UndoHalfEdit(session, mark, rule, notes);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // A rule that cannot do its job safely is skipped, not allowed to stop the loop.
                    notes.Add($"The rule {rule.Name} failed on {d.Code} ({ex.GetType().Name}) and was skipped.");
                    UndoHalfEdit(session, mark, rule, notes);
                }
            }

        return fixedBy;
    }

    /// <summary>A rule that wrote and then failed, or wrote and said "no", must leave no edit behind.</summary>
    private static void UndoHalfEdit(FixSession session, int mark, IBuildFixRule rule, List<string> notes)
    {
        if (session.Mark() == mark)
            return;

        var failed = session.UndoSince(mark);
        notes.Add($"The rule {rule.Name} changed a file without finishing, so its change was reverted.");

        foreach (var file in failed)
            notes.Add($"{file} was not restored; check it by hand.");
    }

    private int ApplyAi(
        IReadOnlyList<BuildDiagnostic> diagnostics, FixSession session, Action<string> progress, List<string> notes)
    {
        var applied = 0;
        var asked = 0;
        var edited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in diagnostics)
        {
            if (asked >= MaxAiAsksPerRound)
                break;

            if (ShouldStop() is { } stopAsk)
            {
                _stopNote = stopAsk;
                break;
            }

            // Line numbers in the diagnostic belong to the file as it was built; once the file
            // has been edited this round they point at the wrong place.
            if (d.Line < 1 || d.File.Length == 0 || edited.Contains(d.File)
                || !d.File.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                continue;

            // Never the model's business: files outside the project, and build or tool output.
            if (!session.IsEditable(d.File))
            {
                notes.Add($"Skipped {d.Code}: its file is outside the project or in an obj, bin or .codegen folder.");
                continue;
            }

            string text;
            try
            {
                text = session.Read(d.File);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                or ArgumentException or NotSupportedException)
            {
                continue;
            }

            var lines = text.Split('\n');

            // "a\nb\n" splits into a, b and an empty tail that is not a line of the file.
            var last = lines.Length - 1 - (text.EndsWith('\n') ? 1 : 0);

            if (d.Line - 1 > last)
                continue;

            var (lo, hi) = ExcerptRange(text, lines, last, d);
            var crlf = text.Contains("\r\n", StringComparison.Ordinal);
            var excerpt = string.Join("\n", lines[lo..(hi + 1)].Select(l => l.TrimEnd('\r')));

            asked++;
            progress($"Asking the local AI about {d.Code} in {session.Relative(d.File)}");

            string? reply;
            try
            {
                reply = ai!.Propose(d, excerpt, _ct);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                continue;
            }

            // Every further ask would wait as long again: one timeout ends the asking for the run.
            if (string.IsNullOrWhiteSpace(reply) && TimedOut(ai))
            {
                _aiTimedOut = true;
                break;
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

            string Splice(string reply)
            {
                var newLines = reply.Split('\n');
                var replacement = newLines
                    .Select((l, i) => crlf && (i < newLines.Length - 1 || lastHadCr) ? l + "\r" : l)
                    .ToArray();

                return string.Join("\n", lines[..lo].Concat(replacement).Concat(lines[(hi + 1)..]));
            }

            var rebuilt = Splice(clean);
            var errorsBefore = SyntaxErrors(text);

            // A small model often closes the class it was shown a part of, or closes it twice: the
            // fix is right, and the braces after it are not. Those trailing closers, and only
            // those, are dropped so the reply closes exactly what the excerpt closed.
            if (SyntaxErrors(rebuilt) > errorsBefore && TrimTrailingClosers(clean, excerpt) is { } trimmed)
                rebuilt = Splice(trimmed);

            if (rebuilt == text || SyntaxErrors(rebuilt) > errorsBefore)
                continue;

            try
            {
                session.Write(d.File, rebuilt, "AI");
                session.Edits[^1].Change = DescribeChange(text, rebuilt);
                edited.Add(d.File);
                applied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                or ArgumentException or NotSupportedException)
            {
                // A half-finished round is worse than none: undo what this round wrote and stop.
                notes.Add(Revert(session, session.CurrentRound, notes)
                    ? $"Writing an AI edit for {d.Code} failed ({ex.GetType().Name}), so the AI edits of this round were reverted."
                    : $"Writing an AI edit for {d.Code} failed ({ex.GetType().Name}), and the AI edits of this round could not be fully undone.");
                return 0;
            }
        }

        return applied;
    }

    /// <summary>
    /// Which lines (0-based, inclusive) the model is shown. Whole syntax units only, so the excerpt
    /// never stops inside a class and the model has no braces to guess: the whole file when it is
    /// short; else the largest member or type around the error that fits; else, for one huge member,
    /// the lines around the error.
    /// </summary>
    private static (int Lo, int Hi) ExcerptRange(string text, string[] lines, int last, BuildDiagnostic d)
    {
        var window = (Math.Max(0, d.Line - 1 - ContextLines), Math.Min(last, d.Line - 1 + ContextLines));

        if (last + 1 <= WholeFileLines)
            return (0, last);

        var tree = CSharpSyntaxTree.ParseText(text);
        var source = tree.GetText();

        // Roslyn also breaks lines at a lone \r and a few Unicode separators; then its line numbers
        // are not ours, and the plain window is the safe choice.
        if (source.Lines.Count != lines.Length)
            return window;

        var line = source.Lines[d.Line - 1];
        var position = line.Start + Math.Clamp(d.Column - 1, 0, Math.Max(0, line.End - line.Start - 1));
        var token = tree.GetRoot().FindToken(position);
        (int, int)? best = null;

        foreach (var node in token.Parent?.AncestorsAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax>() ?? [])
        {
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.BaseNamespaceDeclarationSyntax)
                break;

            // From its doc comment or first attribute, not from the blank lines before it.
            var first = node.GetLeadingTrivia()
                .FirstOrDefault(t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia));
            var start = first.RawKind == 0 ? node.SpanStart : first.SpanStart;
            var lo = source.Lines.GetLineFromPosition(start).LineNumber;
            var hi = source.Lines.GetLineFromPosition(Math.Max(node.SpanStart, node.Span.End - 1)).LineNumber;

            if (hi - lo + 1 > MaxUnitLines)
                break;

            if (lo <= d.Line - 1 && d.Line - 1 <= hi)
                best = (lo, hi);
        }

        return best ?? window;
    }

    /// <summary>
    /// The reply less the lone closing braces at its end that close more than the excerpt closed,
    /// or null when that cannot make the two balance.
    /// </summary>
    private static string? TrimTrailingClosers(string reply, string excerpt)
    {
        var target = Balance(excerpt);
        var balance = Balance(reply);

        if (balance >= target)
            return null;

        var kept = reply.Split('\n').ToList();

        while (balance < target)
        {
            while (kept.Count > 0 && kept[^1].Trim().Length == 0)
                kept.RemoveAt(kept.Count - 1);

            if (kept.Count == 0 || kept[^1].Trim() is not ("}" or "};"))
                return null;

            kept.RemoveAt(kept.Count - 1);
            balance++;
        }

        return string.Join("\n", kept);
    }

    /// <summary>Opening braces less closing ones, as the parser sees them (none in strings or comments).</summary>
    private static int Balance(string source)
        => CSharpSyntaxTree.ParseText(source).GetRoot().DescendantTokens(descendIntoTrivia: true)
            .Where(t => !t.IsMissing)
            .Sum(t => t.IsKind(SyntaxKind.OpenBraceToken) ? 1 : t.IsKind(SyntaxKind.CloseBraceToken) ? -1 : 0);

    /// <summary>The first changed line, and how many lines differ. Small on purpose: no copy of the file is kept.</summary>
    private static EditChange DescribeChange(string before, string after)
    {
        var a = before.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var b = after.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var prefix = 0;

        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
            prefix++;

        var suffix = 0;

        while (suffix < a.Length - prefix && suffix < b.Length - prefix
            && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix])
            suffix++;

        var oldCount = a.Length - prefix - suffix;
        var newCount = b.Length - prefix - suffix;

        return new EditChange(
            prefix + 1,
            oldCount > 0 ? a[prefix] : string.Empty,
            newCount > 0 ? b[prefix] : string.Empty,
            Math.Max(1, Math.Max(oldCount, newCount)));
    }

    /// <summary>
    /// Syntax errors in the whole file. An edit is refused only when it ADDS some: the file may
    /// already have other broken places this round is not about.
    /// </summary>
    private static int SyntaxErrors(string source)
        => CSharpSyntaxTree.ParseText(source).GetDiagnostics().Count(x => x.Severity == DiagnosticSeverity.Error);
}
