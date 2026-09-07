using System.Text.Json;

namespace CircuitRF.Design.Revision;

/// <summary>
/// Reads the commit identity out of circuitRF's own per-user preference file, <b>so <c>src/Cli</c>
/// sees the value the Settings tab captured</b> (R-rc3-1c, <c>revision-control.md</c> §4.4, rev 5).
///
/// <para><b>Why this exists at all.</b> rev 4 let the headless case fall through to git's own
/// resolution, on the grounds that <c>AppPreferences</c> lives in <c>src/Ui</c>. On the fresh Windows
/// machine §4.4 describes, that resolution NAMES NOBODY — so the AI-batch checkpoint, which is the
/// governing motive of the whole feature, was refused for exactly the population it exists to protect,
/// with no remedy but the global config write circuitRF refuses to make. Reading a JSON file in the
/// per-user directory crosses no firewall; only the type that owns the DIALOG does.</para>
///
/// <para><b>This reader knows three things and nothing else about <c>AppPreferences</c>:</b> where the
/// file is (<see cref="UserStateDirectory.PreferencesPath"/>) and the two key names below. RC-4 writes
/// through its own type exactly as every other preference does, and the two agree by these
/// constants.</para>
///
/// <para><b>Identity is a per-user preference and is written to NO git config file</b> (§4.4). The
/// user's GLOBAL config is wrong because circuitRF has no business changing a setting that affects
/// every other repository on the machine. The REPOSITORY's config is worse: it makes a person a
/// property of a DIRECTORY, so on a network share the second designer to open the workspace commits
/// under the first one's name, silently, until somebody reads a history and disbelieves it. Two
/// further consequences follow — an archive with history copies <c>.git/config</c>, so the recipient
/// would commit under the sender's name; and it is a write, in a design whose posture is to make fewer
/// of them.</para>
/// </summary>
public static class RevisionIdentity
{
    /// <summary>The preferences key holding the name. Shared with RC-4's writer.</summary>
    public const string NameKey = "revision_identity_name";

    /// <summary>The preferences key holding the email. Shared with RC-4's writer.</summary>
    public const string EmailKey = "revision_identity_email";

    /// <summary>
    /// What circuitRF's own preference says, or null when it names nobody.
    ///
    /// <para><b>Never throws.</b> A missing, truncated or wrong-shaped preferences file is an absent
    /// one — the same rule the <c>.cwsuser</c> reader states, and for the same reason: this is called
    /// on the path that takes a safety-net checkpoint, and a reader that threw would turn a missing
    /// preference into a lost checkpoint.</para>
    /// </summary>
    public static GitIdentity? FromPreferences()
    {
        string path = UserStateDirectory.PreferencesPath;

        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            string? name  = ReadString(doc.RootElement, NameKey);
            string? email = ReadString(doc.RootElement, EmailKey);

            // BOTH, or neither. Half an identity leaves git to guess the other half at
            // `user@hostname`, which is the silent-wrong-answer this whole section exists to stop.
            if (name is null || email is null) return null;

            return new GitIdentity(name, email, "Settings ▸ Revision Control");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The identity a commit will actually carry: circuitRF's preference first, then git's own
    /// resolution, then nobody.
    ///
    /// <para><b>Where the file names nobody, git's ordinary resolution applies</b> — the user's global
    /// config, if they have one, which is still the right answer for a headless run on a machine where
    /// circuitRF was never configured. <b>Where neither does, the feature does not arm</b>, and the
    /// refusal names the Settings tab rather than a git command (<see cref="GitFailures.NoIdentity"/>).
    /// Returning null here is that state, checked BEFORE the invocation rather than recognised from
    /// git's English afterwards.</para>
    /// </summary>
    public static GitIdentity? Resolve(GitCommand git)
    {
        if (FromPreferences() is { } mine) return mine;

        var name  = git.Run(["config", "--get", "user.name"],  new GitRunOptions(ReadOnly: true));
        var email = git.Run(["config", "--get", "user.email"], new GitRunOptions(ReadOnly: true));

        if (name.Ok && email.Ok && name.Line.Length > 0 && email.Line.Length > 0)
            return new GitIdentity(name.Line, email.Line, "this machine's own git settings");

        return null;
    }

    /// <summary>
    /// What RC-4's Settings tab pre-fills with. <b>Reading someone's global config is unobjectionable;
    /// only writing it was ever the problem</b> (§4.4) — and for the population that already uses git,
    /// setup then costs one glance.
    /// </summary>
    public static GitIdentity? GlobalGitDefault(GitCommand git)
    {
        var name  = git.Run(["config", "--global", "--get", "user.name"],  new GitRunOptions(ReadOnly: true));
        var email = git.Run(["config", "--global", "--get", "user.email"], new GitRunOptions(ReadOnly: true));

        return name.Ok && email.Ok && name.Line.Length > 0 && email.Line.Length > 0
            ? new GitIdentity(name.Line, email.Line, "this machine's own git settings")
            : null;
    }

    private static string? ReadString(JsonElement root, string key)
        => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
           && v.GetString() is { Length: > 0 } s && s.Trim().Length > 0
            ? s.Trim()
            : null;
}
