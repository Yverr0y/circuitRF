using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CircuitRF.Design.Revision;
using Xunit;

namespace CircuitRF.Ui.Tests.Revision;

/// <summary>
/// Like <see cref="FactAttribute"/>, but SKIPS with a stated reason on a machine with no usable git.
///
/// <para><b>R-rc0-10b / RC-3 gate 18.</b> Every gate in this brief needs a git executable and not every
/// developer machine has one. The repo already has the idiom — <c>RfCore.Tests</c>' <c>FixtureFact</c>
/// skips rather than failing when its lab fixtures are absent — and it is the right one here: a skip
/// that names why is honest, and a red suite on a machine missing a prerequisite trains people to
/// ignore red. <b>CI has git on all three platforms, so no coverage is lost.</b> Do not commit or
/// vendor a git binary.</para>
/// </summary>
public sealed class GitFactAttribute : FactAttribute
{
    public GitFactAttribute()
    {
        if (GitProbe.Reason is { } why) Skip = why;
    }
}

/// <inheritdoc cref="GitFactAttribute"/>
public sealed class GitTheoryAttribute : TheoryAttribute
{
    public GitTheoryAttribute()
    {
        if (GitProbe.Reason is { } why) Skip = why;
    }
}

internal static class GitProbe
{
    private static readonly Lazy<string?> _reason = new(() =>
    {
        // Deliberately NOT GitDiscovery.Find: the point is to answer "does this machine have a git at
        // all", independently of the preference plumbing and the shim seam these tests then drive.
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            psi.ArgumentList.Add("--version");
            using var p = Process.Start(psi);
            if (p is null) return "no git on PATH — RC-3's gates need one (CI has it on all three platforms)";
            string banner = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            if (!p.WaitForExit(20_000)) { try { p.Kill(true); } catch { } return "git did not answer --version"; }

            var version = GitDiscovery.ParseVersion(banner.Trim());
            if (version is null) return "the 'git' on PATH did not identify itself as git";
            if (version < GitDiscovery.MinimumVersion)
                return $"git {version} is below circuitRF's floor of {GitDiscovery.MinimumVersion}";
            return null;
        }
        catch (Exception e)
        {
            return "no usable git on PATH — " + e.Message;
        }
    });

    public static string? Reason => _reason.Value;
}

/// <summary>
/// A throwaway workspace with an isolated git environment, so a test never reads or writes the
/// developer's own git configuration.
///
/// <para><b>Isolation is not tidiness here — it is one of the assertions.</b> Gate 8 requires that the
/// user's global config is untouched, and a test that ran against the real one could neither prove
/// that nor be trusted on a machine whose owner has an identity, a signing key or a credential helper
/// configured.</para>
/// </summary>
internal sealed class GitWorkspace : IDisposable
{
    private readonly List<(string Key, string? Old)> _restore = [];

    public string Root       { get; }
    public string HomeDir    { get; }
    public string GlobalConfig => Path.Combine(HomeDir, "gitconfig");

    public GitWorkspace(bool withCws = true)
    {
        Root    = Path.Combine(Path.GetTempPath(), "crf-rc3-" + Guid.NewGuid().ToString("N")[..12]);
        HomeDir = Path.Combine(Root + "-home");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(HomeDir);
        File.WriteAllText(GlobalConfig, "");

        // GIT_CONFIG_GLOBAL/SYSTEM are git 2.32 — above circuitRF's own 2.9 floor, but this is TEST
        // scaffolding, not product code, and CI's gits are all far newer. HOME/USERPROFILE are set too
        // so an older git still lands in the throwaway directory rather than the developer's own.
        Set("GIT_CONFIG_GLOBAL", GlobalConfig);
        Set("GIT_CONFIG_SYSTEM", Path.Combine(HomeDir, "no-such-system-config"));
        Set("GIT_CONFIG_NOSYSTEM", "1");
        Set("HOME", HomeDir);
        Set("USERPROFILE", HomeDir);
        Set("XDG_CONFIG_HOME", HomeDir);

        if (withCws) File.WriteAllText(Path.Combine(Root, ".cws"), "{}");
    }

    private void Set(string key, string? value)
    {
        _restore.Add((key, Environment.GetEnvironmentVariable(key)));
        Environment.SetEnvironmentVariable(key, value);
    }

    public string File_(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    public void Write(string relative, string text)
    {
        string p = File_(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, text);
    }

    public string ReadGlobalConfig() => System.IO.File.ReadAllText(GlobalConfig);

    /// <summary>
    /// Writes a file of exactly <paramref name="bytes"/> bytes — for RC-5's large-file guard, whose
    /// whole question is about size and nothing about content.
    /// </summary>
    public void WriteBytes(string relative, int bytes)
    {
        string p = File_(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllBytes(p, new byte[bytes]);
    }

    /// <summary>
    /// Moves the clock git stamps its objects with (RC-5 gate 7).
    ///
    /// <para><b>Through git's own date variables rather than the machine's clock</b>, which a test
    /// has no business changing — and they are what git actually reads, so setting them measures the
    /// same thing a dead CMOS battery or a re-synced time server would produce.</para>
    /// </summary>
    public void SetClock(DateTimeOffset when)
    {
        string stamp = when.ToUnixTimeSeconds() + " +0000";
        Set("GIT_AUTHOR_DATE", stamp);
        Set("GIT_COMMITTER_DATE", stamp);
    }

    /// <summary>A driver for this workspace, or a failing assertion if git could not be found.</summary>
    public GitCommand Git(GitIdentity? identity = null)
        => GitCommand.For(Root, identity) ?? throw new InvalidOperationException("no git");

    /// <summary>Raw git, for setting a fixture up in ways circuitRF itself never would.</summary>
    public (int Code, string Out, string Err) Raw(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory       = Root,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["LC_ALL"] = "C";

        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        string e = p.StandardError.ReadToEnd();
        p.WaitForExit(60_000);
        return (p.ExitCode, o, e);
    }

    public void Dispose()
    {
        foreach (var (key, old) in _restore) Environment.SetEnvironmentVariable(key, old);
        foreach (string dir in (string[])[Root, HomeDir])
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}


/// <summary>
/// Redirects the per-user state directory for the duration, so a test never reads or writes the
/// developer's own <c>preferences.json</c>.
///
/// <para>It is what makes RC-5's identity gate honest: "circuitRF's own preference names nobody" has
/// to be a real state, and on a machine whose owner has one configured it would otherwise be
/// unreachable.</para>
/// </summary>
internal sealed class AppDataRootScope : IDisposable
{
    public string Dir { get; }

    public AppDataRootScope()
    {
        Dir = Path.Combine(Path.GetTempPath(), "crf-rc5state-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Dir);
        CircuitRF.Ui.AppDataRoot.RedirectTo(Dir);
    }

    public void Dispose()
    {
        CircuitRF.Ui.AppDataRoot.RedirectTo(null);
        try { if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
