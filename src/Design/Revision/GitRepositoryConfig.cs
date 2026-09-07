namespace CircuitRF.Design.Revision;

/// <summary>Which answer <c>revision-control.md</c> §12 Q4 got for this repository. Recorded in the
/// marker so the question is asked once (R-rc3-7b).</summary>
public enum RevisionManagement
{
    /// <summary>circuitRF created the repository itself. The ordinary case.</summary>
    Created,
    /// <summary>The user had a repository at the workspace root and adopted circuitRF's settings.</summary>
    Adopted,
    /// <summary>The user had a repository at the workspace root and kept their own settings. circuitRF
    /// keeps history in it; §4.5's guarantees are the user's own to maintain.</summary>
    KeptUserSettings,
    /// <summary>The user said not to keep history for this workspace. RC-6's hold state.</summary>
    Declined,
}

/// <summary>
/// What circuitRF writes into a repository's OWN config, and why each row is there
/// (<c>revision-control.md</c> §4.5, R-rc3-7).
///
/// <para><b>Never the user's global config.</b> circuitRF has no business changing a setting that
/// affects every other repository on the machine.</para>
///
/// <para><b>The dividing line is what a setting is a property OF.</b> Repository config carries
/// properties of the REPOSITORY — how it is packed, how its files reach disk — which are correctly
/// shared by everyone who opens the folder. <b>Anything that is a property of the PERSON, or of
/// circuitRF's own behaviour, is supplied per invocation</b> (<see cref="GitEnvironment"/>) and written
/// to no config file. The commit identity is the clearest case: identity in a repository's config
/// makes a person a property of a DIRECTORY, so on a network share the second designer to open the
/// workspace commits under the first one's name.</para>
///
/// <para><b>What is NOT here, deliberately:</b> the identity, the sign-off flag, and
/// <c>commit.gpgsign</c>. Writing <c>commit.gpgsign=false</c> would disable signing for THE DESIGNER'S
/// OWN commits from a shell in that folder, which is exactly the thing that must keep working.
/// Suppressing the signature on circuitRF's own commits is a flag on circuitRF's own invocation and
/// belongs nowhere else.</para>
/// </summary>
public static class GitRepositoryConfig
{
    /// <summary>The config section circuitRF's marker lives in. A section name, so it round-trips
    /// through <c>git config</c> untouched and shows up in <c>.git/config</c> as plainly as anything
    /// else a person might read there.</summary>
    public const string MarkerSection = "circuitrf";

    /// <summary>Whether circuitRF manages this repository.</summary>
    public const string MarkerManagedKey = MarkerSection + ".managed";

    /// <summary>Which answer §12 Q4 got — the point of recording it rather than merely recording that
    /// circuitRF was here, which is what stops RC-6 asking on every open.</summary>
    public const string MarkerAnswerKey = MarkerSection + ".management";

    /// <summary>
    /// Every row, with the failure it prevents. Read as a table — a table beats prose when someone is
    /// implementing from it, which is the lesson rev 4 took from a stray row that contradicted the note
    /// three lines below it.
    /// </summary>
    public static IReadOnlyList<(string Key, string Value, string Prevents)> Rows =>
    [
        ("gc.auto", "0",
         "circuitRF's byte-based packing schedule and git's COUNT-based one fighting. git's default "
       + "trigger is 6,700 loose objects with no notion of size, and a workspace whose history is a "
       + "handful of enormous files sits at a few dozen loose objects indefinitely — hundreds of "
       + "megabytes over its packed size — while git never once decides to do anything about it."),

        ("gc.pruneExpire", "never",
         "a ROUTINE PACK permanently destroying thinned restore points after two weeks. git gc runs a "
       + "prune at gc.pruneExpire whether or not the flag was passed; leaving the flag off was never "
       + "sufficient. RC-6's grace period and §1.3's recovery window are both false without this row."),

        ("gc.reflogExpire", "never",
         "the escape hatch for the DESIGNER'S OWN work — a mistaken reset or amend from a shell — "
       + "expiring at 90 days, before §1.3 says the designer looks. It is NOT the way back after a "
       + "retention sweep: a deleted reference takes its reflog with it, and RC-6's journal is that "
       + "way back."),

        ("gc.reflogExpireUnreachable", "never",
         "the same escape hatch expiring at THIRTY days, which is the default that actually bites."),

        ("core.autocrlf", "false",
         "git's end-of-line conversion making a design document's bytes platform-dependent. §2's whole "
       + "measurement rests on .clay being byte-stable line-oriented text; converted, it is a "
       + "whole-file diff on every cross-platform exchange and not the file LayoutPersistence wrote. "
       + "Belt and braces with the .gitattributes -text marking, because the two answer to different "
       + "scopes."),
    ];

    /// <summary>
    /// Windows only: nested cell folders plus git's own object paths pass 260 characters, and the
    /// failure is a refusal on ONE MACHINE CLASS only — which is the kind that reaches a user before it
    /// reaches a developer.
    /// </summary>
    public static IReadOnlyList<(string Key, string Value, string Prevents)> WindowsRows =>
    [
        ("core.longpaths", "true",
         "a path over 260 characters refusing on Windows and nowhere else."),
    ];

    /// <summary>Every row that applies on this machine.</summary>
    public static IEnumerable<(string Key, string Value, string Prevents)> RowsForThisPlatform()
        => OperatingSystem.IsWindows() ? Rows.Concat(WindowsRows) : Rows;
}
