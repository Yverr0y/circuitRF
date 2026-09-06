using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CircuitRF.Ui.Updates;

/// <summary>Why the Release Notes dialog is showing what it is showing.</summary>
public enum ReleaseNotesOutcome
{
    /// <summary>The release for this version was found and carries a body.</summary>
    Found,

    /// <summary>The feed answered, but nothing in it matches this version — or its body is empty.</summary>
    NotPublished,

    /// <summary>The feed could not be reached, or did not answer with a release list.</summary>
    Unavailable,
}

/// <summary>
/// One release's notes, as one titled block of the dialog.
/// </summary>
/// <param name="Version">The version this block's notes belong to — its own banner, since a dialog
/// showing several of them cannot be labelled by the window heading alone.</param>
/// <param name="Markdown">That release's body, exactly as published. Never blank: a release with an
/// empty body is not a section at all (<see cref="ReleaseNotesFetcher.Select"/>).</param>
public sealed record ReleaseNoteSection(string Version, string Markdown);

/// <summary>What the dialog was handed.</summary>
/// <param name="Outcome">Which of the three forms to render.</param>
/// <param name="Version">The version the notes were asked for — shown in every form.</param>
/// <param name="Sections">
/// The running version's notes FIRST, then every skipped version's beneath it, newest to oldest.
/// Empty unless <see cref="ReleaseNotesOutcome.Found"/>.
///
/// <para><b>A list rather than one string, because the versions have to stay distinguishable.</b>
/// Concatenating the bodies would produce a document whose sections are separated only by whatever
/// headings their authors happened to type — and a release body's own <c>## Fixed</c> renders
/// identically to a synthesised version heading, so the reader could not tell where one release
/// ended and the previous one began.</para>
/// </param>
/// <param name="BrowseUrl">The repository's releases page, for the user to check themselves.</param>
public sealed record ReleaseNotesResult(
    ReleaseNotesOutcome Outcome, string Version, IReadOnlyList<ReleaseNoteSection> Sections,
    string BrowseUrl)
{
    /// <summary>
    /// The version the dialog opened FOR — the newest section's body. Convenience for the common
    /// single-release case; nothing that renders may use it, since it hides every older section.
    /// </summary>
    public string Markdown => Sections.Count > 0 ? Sections[0].Markdown : "";
}

/// <summary>
/// Fetches the running version's release notes from the same feed the updater already reads.
///
/// <para><b>It reuses <see cref="GitHubReleasesFeed"/> rather than calling <c>/releases/latest</c> or
/// a per-tag endpoint</b>, and that is the point: the feed URL, the allow-list, the User-Agent and the
/// response-size cap are all decisions this feature must not get a second, weaker copy of. Design
/// §15's move to another host carries this along with everything else for free.</para>
///
/// <para><b>The version asked for is the RUNNING one, not the newest one.</b> The dialog opens because
/// this build has just been installed, so the notes that matter are its own; the newest release on the
/// feed may be one the user has not been offered yet, and showing its notes would describe an
/// application they are not running.</para>
///
/// <para><b>...and every version the user skipped between the two.</b> Automatic updates only offer
/// the newest release, so a machine that was off — or simply not launched — while two releases went
/// out jumps straight from the first to the third, and the middle one's notes would never be shown
/// anywhere. The range is therefore <c>(last version whose notes were shown, running version]</c>,
/// newest first, capped at <see cref="MaxSections"/>.</para>
/// </summary>
public static class ReleaseNotesFetcher
{
    /// <summary>
    /// Where the dialog sends a user whose notes could not be fetched — derived from the feed URL
    /// rather than written down, so the two cannot name different repositories.
    ///
    /// <para><c>https://api.github.com/repos/OWNER/REPO/releases</c> is the API address of the page at
    /// <c>https://github.com/OWNER/REPO/releases</c>. A feed URL of any other shape (a manifest may
    /// have re-pointed it) has no derivable web page, so the API address is offered as-is: a URL that
    /// answers is better than a guess that does not.</para>
    /// </summary>
    public static string BrowseUrl(string feedUrl)
    {
        const string apiPrefix = "https://api.github.com/repos/";

        if (feedUrl.StartsWith(apiPrefix, StringComparison.OrdinalIgnoreCase))
            return "https://github.com/" + feedUrl[apiPrefix.Length..];

        return feedUrl;
    }

    /// <summary>
    /// The page size asked for. GitHub's <c>/releases</c> defaults to <b>30</b>, and a default that
    /// silently truncates the list is the wrong thing to depend on for a lookup that has to work for
    /// <i>any</i> release, not just a recent one — the version being asked about is whichever one the
    /// user has installed, which need not be the newest.
    ///
    /// <para>100 is the API's own maximum. Applied HERE and not to
    /// <see cref="GitHubReleasesFeed.DefaultApiUrl"/>, because that constant is the updater's and this
    /// is not the updater: the update check wants the newest candidate and is correct on one page.
    /// Past 100 releases this degrades to <see cref="ReleaseNotesOutcome.NotPublished"/> with a working
    /// link, which is the honest answer rather than a wrong one.</para>
    /// </summary>
    public const int PageSize = 100;

    /// <summary>
    /// Adds the page size to a feed URL, preserving any query it already carries. Same scheme and same
    /// host, so <see cref="FeedUrlAllowList"/> is unaffected — it is checked against this exact string
    /// inside <see cref="GitHubReleasesFeed"/> either way.
    /// </summary>
    public static string Paged(string feedUrl)
    {
        if (feedUrl.Contains("per_page=", StringComparison.OrdinalIgnoreCase)) return feedUrl;
        return feedUrl + (feedUrl.Contains('?') ? '&' : '?') + "per_page=" + PageSize;
    }

    /// <summary>
    /// How many releases' notes the dialog will ever show at once — the owner's cap.
    ///
    /// <para>An installation that sat unused for a year would otherwise open with a document nobody
    /// reads, and the oldest entries in it describe an application several versions removed from the
    /// one being launched. Ten is enough to cover any realistic gap; past it the releases page has
    /// the rest.</para>
    /// </summary>
    public const int MaxSections = 10;

    /// <summary>
    /// Asks the feed for <paramref name="version"/>'s notes, together with those of every version
    /// released after <paramref name="since"/>. Never throws: every failure is an
    /// <see cref="ReleaseNotesOutcome.Unavailable"/> result the dialog can render, because the only
    /// alternative on this path is an unhandled exception on a background task during launch.
    /// </summary>
    /// <param name="version">The running version — the newest notes shown, and the top of the range.</param>
    /// <param name="since">
    /// The newest version whose notes this user has already been shown, EXCLUSIVE. Null shows
    /// <paramref name="version"/>'s notes alone — see <see cref="Select"/> for why that is the safe
    /// answer rather than "everything".
    /// </param>
    public static async Task<ReleaseNotesResult> FetchAsync(string version, string? since = null,
                                                            CancellationToken ct = default)
    {
        string feedUrl = UpdateScheduler.FeedUrl();
        string browse  = BrowseUrl(feedUrl);

        try
        {
            using HttpClient http = UpdateDownloader.CreateHttpClient();
            var feed = new GitHubReleasesFeed(http, Paged(feedUrl));

            IReadOnlyList<ReleaseInfo> releases = await feed.ListReleasesAsync(ct).ConfigureAwait(false);
            return Select(releases, version, browse, since);
        }
        catch (Exception)
        {
            // Offline, DNS failure, rate limit, a body over the size cap, a feed that answered with
            // something that is not a release list. All one thing to the user: we could not fetch it,
            // here is where to look.
            return new ReleaseNotesResult(ReleaseNotesOutcome.Unavailable, version, [], browse);
        }
    }

    /// <summary>
    /// The choosing half, with no network in it.
    ///
    /// <para><b>Matched on parsed version, not on tag text.</b> A release tagged <c>v1.0.0-beta.4</c>
    /// is this build when <c>VERSION</c> says <c>1.0.0-beta.4</c>, and a tag written <c>1.0</c> is
    /// version <c>1.0.0</c> — the same normalisation trap <see cref="ReleaseInfo.VersionText"/>
    /// documents from the other direction. A string comparison would look right and miss.</para>
    ///
    /// <para>A draft is skipped: it is visible only to the publisher, so matching one would show notes
    /// nobody else can see. A prerelease is NOT skipped — every beta is one, and its own notes are
    /// exactly what its users need.</para>
    ///
    /// <para><b>The result is the running version's notes plus every skipped version's, newest
    /// first</b>, over the half-open range <c>(<paramref name="since"/>, <paramref name="version"/>]</c>.
    /// The outcome is decided by the running version alone: a feed that carries older bodies but not
    /// this one is still <see cref="ReleaseNotesOutcome.NotPublished"/>, because the dialog's own
    /// question is "what changed in the version you are now running" and a list that silently answered
    /// with the previous one instead would be worse than saying nothing.</para>
    ///
    /// <para><b>Null <paramref name="since"/> shows one release, not all of them.</b> It means nothing
    /// has been recorded — a state directory that was wiped, or an installation that predates the
    /// record — and there is no evidence about what the user has read. Guessing "everything" opens a
    /// ten-release document in front of someone who may have read all of it; guessing "one" costs
    /// nothing that the releases page does not already offer.</para>
    ///
    /// <para><b>A prerelease is offered only to a prerelease.</b> A user running a stable build never
    /// installed the betas that led to it, so their notes describe work they saw arrive in one piece;
    /// a user running a beta is on that channel by definition and wants every one. The RUNNING version
    /// decides, since it is the only evidence here of which channel this machine follows.</para>
    /// </summary>
    public static ReleaseNotesResult Select(IReadOnlyList<ReleaseInfo> releases, string version,
                                            string browseUrl, string? since = null)
    {
        if (!SemanticVersion.TryParse(version, out SemanticVersion? running) || running is null)
            return new ReleaseNotesResult(ReleaseNotesOutcome.NotPublished, version, [], browseUrl);

        ReleaseInfo? current = null;
        foreach (ReleaseInfo r in releases)
            if (!r.IsDraft && r.Version.Equals(running)) { current = r; break; }

        if (current is null || string.IsNullOrWhiteSpace(current.Body))
            return new ReleaseNotesResult(ReleaseNotesOutcome.NotPublished, version, [], browseUrl);

        var sections = new List<ReleaseNoteSection> { new(current.VersionText, current.Body) };

        if (SemanticVersion.TryParse(since, out SemanticVersion? read) && read is not null && read < running)
        {
            var skipped = new List<ReleaseInfo>();
            foreach (ReleaseInfo r in releases)
            {
                if (r.IsDraft || string.IsNullOrWhiteSpace(r.Body)) continue;
                if (r.Version <= read || r.Version >= running) continue;
                if (r.Version.IsPreRelease && !running.IsPreRelease) continue;
                skipped.Add(r);
            }

            // Newest first, and sorted rather than trusted: the feed's order is the host's business,
            // and the cap below has to drop the OLDEST entries rather than whichever ones happened to
            // arrive last.
            skipped.Sort(static (a, b) => b.Version.CompareTo(a.Version));

            foreach (ReleaseInfo r in skipped)
            {
                if (sections.Count >= MaxSections) break;
                sections.Add(new ReleaseNoteSection(r.VersionText, r.Body));
            }
        }

        return new ReleaseNotesResult(ReleaseNotesOutcome.Found, version, sections, browseUrl);
    }
}
