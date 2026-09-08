// ================================================================
//  AppSettings.cs  —  User preferences (in-memory defaults for circuitRF)
//
//  Ported from splotRF/src/Models/AppSettings.cs.  Disk persistence
//  deferred to 7.x: the Load() method returns in-memory defaults rather
//  than reading splotRF's config path, and Save() is a no-op until wired.
//
//  TODO 7.x: wire AppSettings persistence to circuitRF's app-data dir.
//
//  RND-4 moved this below the UI firewall and gave it `Current`, because
//  AxesRenderer reads AlwaysDisplayDataSourcePrefix and the exporter reads
//  ExportTheme / ExportTransparentBackground / MarkerBoxTransparentBackground
//  — and until now the only way to ask was AppSettingsViewModel.Instance,
//  a view model. `Current` IS the object that view model wraps: it
//  constructs itself over this one and writes every change through, so a
//  setting changed in the Settings dialog is the setting a headless render
//  reads, and there is no second copy to drift.
// ================================================================

namespace CircuitRF.Render.DataDisplay
{
    /// <summary>Which color theme to use when exporting or copying plots.</summary>
    public enum ExportThemeMode
    {
        UseSystemTheme,   // Follow the OS light/dark appearance
        ForceLightTheme,  // Always export with the light theme
        ForceDarkTheme    // Always export with the dark theme
    }

    /// <summary>
    /// Plain-object settings model.  All properties have sensible defaults.
    /// Disk persistence is deferred to 7.x.
    /// </summary>
    public class AppSettings
    {
        // ── Export & Copy ────────────────────────────────────────────────────

        /// <summary>Color theme override for exported / copied plots.</summary>
        public ExportThemeMode ExportTheme { get; set; } = ExportThemeMode.UseSystemTheme;

        /// <summary>When true, plots are exported / copied with a transparent background.</summary>
        public bool ExportTransparentBackground { get; set; } = true;

        // ── Display ──────────────────────────────────────────────────────────

        /// <summary>When true, marker info boxes are drawn without a filled background.</summary>
        public bool MarkerBoxTransparentBackground { get; set; } = true;

        /// <summary>
        /// When true, file-name prefix is always shown in marker info boxes and
        /// axis labels, regardless of how many SNPs are loaded.
        /// </summary>
        public bool AlwaysDisplayDataSourcePrefix { get; set; } = false;

        // ── New Marker Defaults ──────────────────────────────────────────────

        /// <summary>Number of significant / decimal digits for newly created markers.</summary>
        public int MarkerMaxFractionDigits { get; set; } = 4;

        /// <summary>Number format for newly created markers (Auto / Fixed / Scientific).</summary>
        public PrecisionFormat MarkerPrecisionFormat { get; set; } = PrecisionFormat.G;

        // ── Rect Plot ────────────────────────────────────────────────────────

        /// <summary>Golden aspect ratio (width / height) φ ≈ 1.618.</summary>
        public static double GoldenAspectRatio = 1.618;

        /// <summary>
        /// Aspect ratio (width / height) applied during Shift+drag resize of Rect plots.
        /// Default is the golden ratio φ ≈ 1.618.
        /// </summary>
        public double RectAspectRatio { get; set; } = GoldenAspectRatio;

        // ── Persistence ──────────────────────────────────────────────────────

        /// <summary>
        /// The one live settings object. <c>AppSettingsViewModel.Instance</c> wraps THIS instance —
        /// see the header — so a renderer or an exporter reading it sees what the user chose.
        /// </summary>
        public static AppSettings Current { get; } = Load();

        /// <summary>
        /// Returns in-memory defaults.
        /// TODO 7.x: wire AppSettings persistence to circuitRF's app-data dir.
        /// </summary>
        public static AppSettings Load() => new AppSettings();

        // ---- Decisions the renderers and the exporter ask for --------------
        //  Pure functions of the settings above, and the reason they are HERE rather than on the
        //  view model that used to own them: AxesRenderer and PlotExporter are the callers, and
        //  neither may reach a view model from below the firewall.

        /// <summary>
        /// The <see cref="RenderTheme"/> to export or copy with, applying <see cref="ExportTheme"/>
        /// against the live system theme.
        /// </summary>
        public RenderTheme GetExportRenderTheme(RenderTheme systemTheme) => ExportTheme switch
        {
            ExportThemeMode.ForceLightTheme => RenderTheme.Light,
            ExportThemeMode.ForceDarkTheme  => RenderTheme.Dark,
            _                               => systemTheme
        };

        /// <summary>
        /// The effective show-file-prefix flag: always true when
        /// <see cref="AlwaysDisplayDataSourcePrefix"/> is set, otherwise the caller's own
        /// library-count heuristic.
        /// </summary>
        public bool EffectiveShowFilePrefix(bool libraryHasMultiple) =>
            AlwaysDisplayDataSourcePrefix || libraryHasMultiple;

        /// <summary>
        /// No-op until 7.x wires persistence.
        /// TODO 7.x: wire AppSettings persistence to circuitRF's app-data dir.
        /// </summary>
        public void Save() { }
    }
}
