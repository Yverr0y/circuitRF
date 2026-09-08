// ================================================================
//  DocumentedWalkthroughTests.cs — brief-render-5-mcp-and-user-docs.md gate B.4.3.
//
//  "Every command in the chapter runs. The worked example is executed as a test, end to end, from an
//  empty directory, exactly as the `em` walkthrough already is."
//
//  ── Why it READS the chapter rather than restating it ─────────────────────────────────────────────
//
//  A test that re-typed the walkthrough would prove that a sequence someone once wrote still works.
//  It would say nothing about the PAGE, which is the artefact a reader actually runs — and a page and
//  a test that each hold their own copy of a command line drift silently, in the direction where the
//  page is wrong and everything is green.
//
//  So the commands, the `.clay` and the expected transcript all come out of
//  `docs/user/src/reference/cli.md` itself. Edit the worked example into something that does not run
//  and this fails; add a step to it and the step is run too, with nothing to remember.
//
//  ── What is compared, and the three lines that deliberately are not ───────────────────────────────
//
//  Every command must exit 0, and every line of the documented transcript must appear in the real
//  output — except:
//
//    * a line carrying an absolute path. The chapter anonymizes those to the SHAPE of a path
//      (feedback-no-personal-paths-in-repo), so `/home/you/work/Amp/.cws` is correct prose and cannot
//      match a temp directory.
//    * the chapter's own `…` elision of a long list.
//    * the one number that belongs to the machine rather than to circuitRF: an encoded file's LENGTH
//      in bytes. The rest of that line — the file name, the page size, the unit — is compared.
//
//  The render's COUNTERS are compared in full, because `cli.md` §13.6 says in as many words that they
//  are deterministic and machine-independent by construction, which is the property that makes them
//  worth printing in a manual at all.
//
//  NOT tagged Benchmark, measured rather than assumed: six CLI process launches, ~2 s together.
// ================================================================

using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace CircuitRF.Ui.Tests.Render;

public sealed class DocumentedWalkthroughTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "crf-walkthrough-" + Guid.NewGuid().ToString("N")[..12]);

    public void Dispose() { try { Directory.Delete(_root, true); } catch { /* best effort */ } }

    /// <summary>The one command in the example that is not <c>circuitrf</c>: it makes the directory
    /// the rest run in, which this test's own temp directory already is. Everything else is asserted
    /// to start with <c>circuitrf</c>, so a step added to the chapter as some other shell command
    /// cannot be skipped in silence.</summary>
    private const string TheShellStep = "mkdir work && cd work";

    [Fact]
    public void TheChaptersWorkedRenderExample_RunsFromAnEmptyDirectory_AndPrintsWhatItSaysItDoes()
    {
        string example = Section(File.ReadAllText(ChapterPath()), "render-example");
        var    blocks  = CommandBlocks(example);
        Assert.True(blocks.Count >= 4, $"only {blocks.Count} command blocks parsed out of the worked example");

        Directory.CreateDirectory(_root);

        // The path is not in the chapter's JSON fence; it is the file `new cell` creates, so it is
        // written the moment that command has produced it.
        string clay           = Path.Combine(_root, "Amp", "Stage1", "layout", "Stage1.clay");
        bool   artworkWritten = false;
        int    commandsRun    = 0;
        int    linesCompared  = 0;

        foreach (var (commands, documented) in blocks)
        {
            string transcript = "";

            foreach (string command in commands)
            {
                if (command == TheShellStep) continue;
                Assert.StartsWith("circuitrf ", command, StringComparison.Ordinal);

                var (exit, stdout, stderr) = RunCli(Words(command));
                commandsRun++;
                output.WriteLine($"$ {command}\n{stdout}{stderr}");

                Assert.True(exit == 0, $"'{command}' exited {exit}\n{stderr}{stdout}");
                transcript = Normalize(stdout + stderr);

                if (!artworkWritten && File.Exists(clay))
                {
                    File.WriteAllText(clay, JsonFence(example));
                    artworkWritten = true;
                }
            }

            foreach (string line in documented)
            {
                string want = line.TrimEnd();
                if (want.Length == 0 || want.Trim() == "…") continue;
                if (want.Contains("/home/you/", StringComparison.Ordinal)) continue;

                if (want.Contains(" bytes)", StringComparison.Ordinal))
                    want = want[..want.LastIndexOf(',', want.IndexOf(" bytes)", StringComparison.Ordinal))];

                Assert.True(transcript.Contains(want, StringComparison.Ordinal),
                            $"the chapter prints a line the command does not:\n  {want}\n\nactual:\n{transcript}");
                linesCompared++;
            }
        }

        Assert.True(artworkWritten, "the chapter's `.clay` fence was never written — no cell folder appeared");
        Assert.Equal(6, commandsRun);

        // Neither half vacuous: a transcript that failed to parse, or a block list with no output in
        // it, would satisfy every assertion above.
        Assert.True(linesCompared >= 12, $"only {linesCompared} documented lines were compared");
        Assert.True(File.Exists(Path.Combine(_root, "stage1-top.png")),
                    "the worked example's own output file is not there");
    }

    /// <summary>
    /// Gate B.4.2: <b>every option the two verbs advertise is written down in the chapter</b> —
    /// checked against the verbs' OWN usage text rather than against a brief, because the code is the
    /// contract and a brief is a plan.
    ///
    /// <para>It is the cheap half of documentation rot and the half nobody notices: a flag added in a
    /// later round works, is reachable, and is invisible to everyone who has only read the manual.
    /// The usage string is where a verb says what it takes, so that is what this compares against.
    /// </para>
    ///
    /// <para>What it does NOT claim is that each one is <i>explained</i> — only that it appears. A
    /// mention is a floor, not a standard.</para>
    /// </summary>
    [Fact]
    public void EveryOptionTheVerbsAdvertise_IsWrittenDownInTheChapter()
    {
        string chapter = File.ReadAllText(ChapterPath());
        Directory.CreateDirectory(_root);

        var missing = new List<string>();
        int checkedFlags = 0;

        foreach (string verb in new[] { "render", "explain" })
        {
            // With no arguments each verb prints its own usage and exits 1 — a refusal, which is the
            // point: nothing is read, run or written.
            var (_, stdout, stderr) = RunCli(verb);

            foreach (System.Text.RegularExpressions.Match m in
                     Regex.Matches(stdout + stderr, @"(?<![\w-])--[a-z][a-z-]*"))
            {
                string flag = m.Value;
                checkedFlags++;
                if (!chapter.Contains(flag, StringComparison.Ordinal)) missing.Add($"{verb} {flag}");
            }
        }

        Assert.True(missing.Count == 0,
                    "the chapter never mentions: " + string.Join(", ", missing.Distinct()));

        // Not vacuous: a usage string that failed to print would satisfy the assertion above.
        Assert.True(checkedFlags >= 25, $"only {checkedFlags} options were found in the usage text");
    }

    // ── reading the chapter ──────────────────────────────────────────────────

    private static string ChapterPath()
    {
        string path = Path.Combine(RepoRoot(), "docs", "user", "src", "reference", "cli.md");
        Assert.True(File.Exists(path), $"the chapter is not there: {path}");
        return path;
    }

    /// <summary>One <c>&lt;h3 id="…"&gt;</c> section, up to the next horizontal rule.</summary>
    private static string Section(string chapter, string id)
    {
        int start = chapter.IndexOf($"<h3 id=\"{id}\">", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the chapter has no section '{id}'");

        int end = chapter.IndexOf("\n---\n", start, StringComparison.Ordinal);
        return end < 0 ? chapter[start..] : chapter[start..end];
    }

    /// <summary>The chapter's own <c>.clay</c>, out of its own fenced JSON block.</summary>
    private static string JsonFence(string section)
    {
        var m = Regex.Match(section, "```json\n(.*?)```", RegexOptions.Singleline);
        Assert.True(m.Success, "the worked example has no json fence in it");
        return m.Groups[1].Value;
    }

    /// <summary>
    /// Every <c>&lt;pre&gt;&lt;code class="cmd"&gt;</c> block, as (the commands in it, the transcript
    /// it documents).
    ///
    /// <para>A <c>$</c> prompt starts a command and a blank prompt continues the one above it; a
    /// trailing backslash joins the two, which is how the chapter wraps a long command line. What
    /// follows in a <c>&lt;span class="output"&gt;</c> is the transcript.</para>
    /// </summary>
    private static List<(List<string> Commands, string[] Output)> CommandBlocks(string section)
    {
        var blocks = new List<(List<string>, string[])>();

        foreach (System.Text.RegularExpressions.Match block in Regex.Matches(section, "<pre><code class=\"cmd\">(.*?)</code></pre>",
                                              RegexOptions.Singleline))
        {
            string body     = block.Groups[1].Value;
            int    outputAt = body.IndexOf("<span class=\"output\">", StringComparison.Ordinal);

            var commands = new List<string>();
            foreach (string raw in (outputAt < 0 ? body : body[..outputAt]).Split('\n'))
            {
                // The prompt is MARKUP, not text: a `$ ` span starts a command and anything else is
                // a continuation of the one above it — the chapter wraps a long command line both
                // ways, with a blank prompt span and with a bare indent. Reading it from the raw line
                // rather than from the stripped one is what keeps a continuation that happens to
                // begin with a dollar sign from starting a second command.
                bool starts = raw.Contains("<span class=\"prompt\">$ </span>", StringComparison.Ordinal);

                // The prompt SPAN goes first, so its `$ ` never survives into the command line.
                string line = Strip(Regex.Replace(raw, "<span class=\"prompt\">.*?</span>", "")).Trim();
                if (line.Length == 0) continue;

                if (starts)                commands.Add(line);
                else if (commands.Count > 0) commands[^1] = commands[^1].TrimEnd('\\').TrimEnd() + " " + line;
            }

            blocks.Add((commands, [.. Strip(outputAt < 0 ? "" : body[outputAt..]).Split('\n')]));
        }

        return blocks;
    }

    /// <summary>The markup off, and the entities back.</summary>
    private static string Strip(string html)
        => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", "")).Trim('\n').TrimEnd();

    /// <summary>Splits a command line on spaces, honouring the one pair of quotes the chapter uses —
    /// <c>--layers "Top Copper"</c>. A layer name with a space in it is exactly the case the chapter
    /// has to show, so the parser has to survive it. The leading <c>circuitrf</c> is dropped: this
    /// process IS the executable.</summary>
    private static string[] Words(string command)
        => [.. Regex.Matches(command, @"""([^""]*)""|(\S+)")
                    .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
                    .Skip(1)];

    private static string Normalize(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal);

    // ── the CLI, as a process, in the walkthrough's own empty directory ──────

    private (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            // The walkthrough's paths are RELATIVE, which is the whole shape of it — "from an empty
            // folder". So the child's working directory is that folder and nothing is rewritten.
            WorkingDirectory       = _root,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };
        psi.ArgumentList.Add(CliDll());
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        // Both pipes drained concurrently: `render` writes a stage line per phase to stderr, and
        // reading one to the end before the other deadlocks as soon as the unread one fills.
        var outTask = proc.StandardOutput.ReadToEndAsync();
        var errTask = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit();
        return (proc.ExitCode, outTask.GetAwaiter().GetResult(), errTask.GetAwaiter().GetResult());
    }

    private static string CliDll()
    {
        string cliDir = System.Reflection.CustomAttributeExtensions
            .GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(
                typeof(DocumentedWalkthroughTests).Assembly)
            .First(a => a.Key == "CliDir").Value!;
        string path = Path.GetFullPath(Path.Combine(cliDir, "CircuitRF.Cli.dll"));
        Assert.True(File.Exists(path), $"the CLI was not built beside these tests: {path}");
        return path;
    }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir is { Length: > 0 } && !File.Exists(Path.Combine(dir, "circuitRF.slnx")))
            dir = Path.GetDirectoryName(dir) ?? "";
        return dir.Length > 0 ? dir : AppContext.BaseDirectory;
    }
}
