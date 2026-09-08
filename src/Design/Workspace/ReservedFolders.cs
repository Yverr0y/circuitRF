namespace CircuitRF.Design.Workspace;

/// <summary>
/// <b>The folders a workspace holds that are not part of the design</b> — one spelling, one rule,
/// read by the project tree and by the headless verbs alike (RND-3 R-rnd3-4).
///
/// <para><b>Why this is here and not in the scanner.</b> <c>WorkspaceScanner</c> already knew both
/// of these and hid them from the project tree, but it lives in <c>src/Ui</c> and the CLI cannot
/// reference it. A second list of names in <c>src/Cli</c> would be free to drift, and the drift would
/// show up as <c>explain --cells</c> listing hundreds of machine-named cells the GUI does not show —
/// so the scanner delegates to this and there is exactly one place the names are written down.</para>
///
/// <para><b><c>.generated-cells</c></b> (R-L5-3) holds one content-addressed cell folder per distinct
/// PCell placement. It can hold hundreds, none of them is a cell anyone authored, and the project
/// tree excludes it deliberately. A caller debugging a PCell genuinely does want to reach one, which
/// is what <c>explain --cells --all</c> is for — the exclusion is a default, never a wall.</para>
///
/// <para><b><c>.git</c></b> is where a workspace's history lives (<c>revision-control.md</c> §5.7). It
/// holds hundreds of two-character directories of compressed objects; walked as design content it
/// would swamp any report of the workspace and invite a caller to open something meaningless.</para>
/// </summary>
public static class ReservedFolders
{
    /// <summary>The reserved workspace-root folder every generated PCell cell lives under. Dot-prefixed,
    /// matching the <c>.cws</c>/<c>.ccell</c>/<c>.ctech</c> convention.</summary>
    public const string GeneratedCells = ".generated-cells";

    /// <summary>The workspace's own history.</summary>
    public const string Git = ".git";

    /// <summary>True for a directory nothing walking a workspace for DESIGN content should descend
    /// into. Takes a path or a bare name; only the last segment is looked at.</summary>
    public static bool IsReserved(string dir)
    {
        string name = Path.GetFileName(
            dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return string.Equals(name, GeneratedCells, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, Git,            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when <paramref name="absolutePath"/> sits UNDER a generated-cells folder at any
    /// depth. By path SEGMENT rather than by prefix against one workspace root, so it holds for a
    /// foreign or loose path too.</summary>
    public static bool IsUnderGeneratedCells(string absolutePath)
        => Path.GetFullPath(absolutePath)
               .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
               .Any(seg => string.Equals(seg, GeneratedCells, StringComparison.OrdinalIgnoreCase));
}
