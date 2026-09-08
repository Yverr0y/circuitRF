// ================================================================
//  TraceRowViewModel.WsProbe.cs  —  the WSProbe section of the trace card
//
//  WSP-4 §2. The card half of the probe metrics: which probe (and, for
//  a pair or a set, which others), the reference the circulator and
//  block metrics normalise by, Ohtomo's active side, the readout, and
//  "Mark crossings".
//
//  It computes NOTHING. Every number it shows comes from WspReadouts,
//  which calls src/RfCore/Stability/; every value the trace draws comes
//  from WspSource, at TraceResolve's own interception point. What is
//  here is the pickers, their gating, and the notifications that keep
//  them in step.
// ================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using RfCore.Data;
using RfCore.Stability;

namespace CircuitRF.Ui.DataDisplay.ViewModels;

public partial class TraceRowViewModel
{
    private bool _syncingWsp;

    /// <summary>The section shows only for a trace that IS a probe metric — a source carrying a
    /// <c>wsp</c> cube offers the metrics in the item picker, and picking one is what opens
    /// this.</summary>
    public bool ShowWspSection => _trace.IsWspTrace;

    private WspTraceSpec? WspSpec => _trace.Wsp;

    private WspMetricGroup? WspSpecGroup
        => WspSpec is { } s ? WspMetrics.Info(s.Metric)?.Group : null;

    /// <summary>The probes of the trace's own analysis group, in <c>idx</c> order and showing it —
    /// R-wsp4-5's "labels from <c>__WspProbes</c>, in <c>idx</c> order, showing <c>idx</c> beside
    /// each". The index is shown because it is what a measure line types
    /// (<c>SP1.idx("GATE")</c>) and what the document's own equations index by.</summary>
    public ObservableCollection<string> WspProbeItems { get; } = new();

    private readonly List<string> _wspProbeLabels = new();

    /// <summary>The label behind a "<c>GATE</c> (idx 1)" row.</summary>
    private string LabelOfItem(string? item)
    {
        if (item is null) return "";
        int i = WspProbeItems.IndexOf(item);
        return i >= 0 && i < _wspProbeLabels.Count ? _wspProbeLabels[i] : "";
    }

    private string ItemOfLabel(string label)
    {
        int i = _wspProbeLabels.IndexOf(label);
        return i >= 0 ? WspProbeItems[i] : "";
    }

    // ── probe / with / set ───────────────────────────────────────────────────

    private string? _selectedWspProbeItem;
    public string? SelectedWspProbeItem
    {
        get => _selectedWspProbeItem;
        set
        {
            if (_selectedWspProbeItem == value) return;
            _selectedWspProbeItem = value;
            OnPropertyChanged();
            if (_syncingWsp || WspSpec is null) return;
            WspSpec.Probe = LabelOfItem(value);
            ApplyWspEdit();
        }
    }

    private string? _selectedWspWithItem;
    public string? SelectedWspWithItem
    {
        get => _selectedWspWithItem;
        set
        {
            if (_selectedWspWithItem == value) return;
            _selectedWspWithItem = value;
            OnPropertyChanged();
            if (_syncingWsp || WspSpec is null) return;
            WspSpec.With = LabelOfItem(value);
            ApplyWspEdit();
        }
    }

    /// <summary>The ordered probe SET, as the comma-separated list of labels the card edits. A list
    /// rather than a multi-select control because ORDER is part of the answer — Ohtomo's
    /// <c>G_i</c> are the trailing principal minors of <c>M</c> in the set's own order (Eq. 179,
    /// T-10), so re-ordering the set changes every value.</summary>
    public string WspSetText
    {
        get => WspSpec is null ? "" : string.Join(", ", WspSpec.Set);
        set
        {
            if (WspSpec is null) return;
            var wanted = (value ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (wanted.SequenceEqual(WspSpec.Set)) return;
            WspSpec.Set = wanted;
            OnPropertyChanged();
            ApplyWspEdit();
        }
    }

    public bool ShowWspWith => ShowWspSection && WspSpecGroup == WspMetricGroup.Pair;
    public bool ShowWspSet  => ShowWspSection && WspSpecGroup == WspMetricGroup.ProbeSet;
    public bool ShowWspZ0   => ShowWspSection && WspSpec is { } s && WspMetrics.UsesZ0(s.Metric);

    /// <summary>The Envelope sub-card (R-wsp4-9): shown for the five quantities that are read off
    /// the RE-TERMINATED circuit, and for nothing else.</summary>
    public bool ShowWspEnvelope => ShowWspSection && WspSpecGroup == WspMetricGroup.Envelope;

    // ── the Envelope sub-card ────────────────────────────────────────────────
    //
    //  Three probes and two ladders. The `probe` row above is the SUSPECT node — the one the pulled
    //  circuit is read at — and these two are where the pulling happens; naming them apart is the
    //  document's own §9 arrangement and it is what makes "a probe not at its termination" a
    //  question that can be asked at all.

    /// <summary>The probe rows plus an explicit OFF row at the top. A side is turned off by picking
    /// that row, which is R-wsp4-9's "an off state" made a choice rather than an empty combo box
    /// that reads as "not filled in yet".</summary>
    public ObservableCollection<string> WspSideProbeItems { get; } = new();

    /// <summary>The off row's text. Not a label any probe can have — a probe label comes from a
    /// schematic instance name.</summary>
    public const string WspSideOffItem = "(not pulled)";

    private string? _selectedWspSourceItem;
    public string? SelectedWspSourceItem
    {
        get => _selectedWspSourceItem;
        set
        {
            if (_selectedWspSourceItem == value) return;
            _selectedWspSourceItem = value;
            OnPropertyChanged();
            if (_syncingWsp || WspSpec is null) return;
            WspSpec.SourceProbe = value == WspSideOffItem ? "" : LabelOfItem(value);
            ApplyWspEdit();
        }
    }

    private string? _selectedWspLoadItem;
    public string? SelectedWspLoadItem
    {
        get => _selectedWspLoadItem;
        set
        {
            if (_selectedWspLoadItem == value) return;
            _selectedWspLoadItem = value;
            OnPropertyChanged();
            if (_syncingWsp || WspSpec is null) return;
            WspSpec.LoadProbe = value == WspSideOffItem ? "" : LabelOfItem(value);
            ApplyWspEdit();
        }
    }

    /// <summary>
    /// The source ladder as the card edits it: one <c>|ΓS|</c> per rung, comma separated.
    ///
    /// <para><b>A ladder rather than one magnitude, because that is the reading.</b> [E]'s own
    /// <c>0.9, 0.875, 0.874</c> is three rungs on either side of the ρ at which encirclements first
    /// appear, and the point of the card is to see all three at once rather than to re-run twice
    /// and remember what the last picture looked like.</para>
    /// </summary>
    public string WspGammaSText
    {
        get => WspSpec is null ? "" : LadderText(WspSpec.GammaSMags);
        set => SetLadder(value, s => s.GammaSMags, nameof(WspGammaSText));
    }

    /// <inheritdoc cref="WspGammaSText"/>
    public string WspGammaLText
    {
        get => WspSpec is null ? "" : LadderText(WspSpec.GammaLMags);
        set => SetLadder(value, s => s.GammaLMags, nameof(WspGammaLText));
    }

    private static string LadderText(IReadOnlyList<double> mags)
        => string.Join(", ", mags.Select(m => m.ToString("0.####",
                                                System.Globalization.CultureInfo.InvariantCulture)));

    private void SetLadder(string? text, Func<WspTraceSpec, List<double>> pick, string propertyName)
    {
        if (WspSpec is not { } spec) return;
        var wanted = new List<double>();
        foreach (string tok in (text ?? "").Split(
                     [',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (double.TryParse(tok, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double v))
                wanted.Add(v);

        var current = pick(spec);
        if (wanted.SequenceEqual(current)) { OnPropertyChanged(propertyName); return; }
        current.Clear();
        current.AddRange(wanted);
        OnPropertyChanged(propertyName);
        ApplyWspEdit();
    }

    /// <summary>The angular step of both Γ grids, in degrees.</summary>
    public double WspThetaStepDeg
    {
        get => WspSpec?.ThetaStepDeg ?? 15.0;
        set
        {
            if (WspSpec is null) return;
            double v = value;
            if (!(v > 0.0) || v > 360.0 || double.IsNaN(v)) { OnPropertyChanged(); return; }
            if (v == WspSpec.ThetaStepDeg) return;
            WspSpec.ThetaStepDeg = v;
            OnPropertyChanged();
            ApplyWspEdit();
        }
    }

    /// <summary>Which cube of this source the NDF is taken against — a second analysis of the same
    /// netlist, run with its devices passivated. Blank reads the <c>wsp_passive</c> beside this
    /// group's own <c>wsp</c>, which is what WSP-6 will emit.</summary>
    public string WspPassiveText
    {
        get => WspSpec?.PassiveSource ?? "";
        set
        {
            if (WspSpec is null) return;
            string v = (value ?? "").Trim();
            if (v == WspSpec.PassiveSource) { OnPropertyChanged(); return; }
            WspSpec.PassiveSource = v;
            OnPropertyChanged();
            ApplyWspEdit();
        }
    }

    /// <summary>Only <c>NDFenc</c> reads the passivated run; the field is hidden rather than shown
    /// inert beside the four quantities that do not.</summary>
    public bool ShowWspPassive => ShowWspEnvelope && WspSpec?.Metric == WspMetric.NDFenc;

    /// <summary>
    /// How many terminations this card is asking for, counted before anything is computed — the one
    /// number that decides whether the grid is a second or a minute. Each grid point is a rank-1
    /// update per frequency, so the cost is the product of the two sides and the sweep.
    /// </summary>
    public string WspEnvelopeGridText
    {
        get
        {
            if (WspSpec is not { } spec) return "";
            int nT = WspMetrics.ThetaCount(spec.ThetaStepDeg);
            string Side(string name, string probe, IReadOnlyList<double> mags)
                => probe.Length == 0 || mags.Count == 0 || (mags.Count == 1 && mags[0] == 0.0)
                    ? $"{name} not pulled"
                    : $"{name} {mags.Count} \u00d7 {nT}";
            int nS = Count(spec.SourceProbe, spec.GammaSMags, nT);
            int nL = Count(spec.LoadProbe,   spec.GammaLMags, nT);
            return $"{Side("source", spec.SourceProbe, spec.GammaSMags)}, "
                 + $"{Side("load", spec.LoadProbe, spec.GammaLMags)} \u2014 {nS * nL} terminations";

            static int Count(string probe, IReadOnlyList<double> mags, int nT)
                => probe.Length == 0 || mags.Count == 0 || (mags.Count == 1 && mags[0] == 0.0)
                    ? 1
                    : mags.Count * nT;
        }
    }

    // ── Z0, side, set index ──────────────────────────────────────────────────

    /// <summary>The reference the circulator, pair and Ohtomo metrics normalise by. Blank means
    /// "the source group's own port-1 Re(Z0), else 50 Ω" — resolved at draw time rather than frozen
    /// into the display, so a saved card re-read against a different run follows THAT run.</summary>
    public string WspZ0Text
    {
        get => WspSpec is null || WspSpec.Z0 == System.Numerics.Complex.Zero
            ? ""
            : ComplexStringHelper.Format(WspSpec.Z0);
        set
        {
            if (WspSpec is null) return;
            var z = string.IsNullOrWhiteSpace(value)
                ? System.Numerics.Complex.Zero
                : ComplexStringHelper.TryParse(value, out var parsed) ? parsed : WspSpec.Z0;
            if (z == WspSpec.Z0) { OnPropertyChanged(); return; }
            WspSpec.Z0 = z;
            OnPropertyChanged();
            ApplyWspEdit();
        }
    }

    public static IReadOnlyList<string> WspSides { get; } = ["G", "L"];

    /// <summary>Which side of every probe Ohtomo treats as the ACTIVE subnetwork. Explicit because
    /// the document is not consistent about it (overview D-8 / typo register T-9), and picking the
    /// wrong one gives a plausible wrong answer rather than an error.</summary>
    public string WspActiveSide
    {
        get => WspSpec is null ? "G" : WspSpec.ActiveSide.ToString();
        set
        {
            if (WspSpec is null) return;
            var side = value == "L" ? WspSide.L : WspSide.G;
            if (side == WspSpec.ActiveSide) return;
            WspSpec.ActiveSide = side;
            OnPropertyChanged();
            ApplyWspEdit();
        }
    }

    /// <summary>Which of Ohtomo's <c>G_i</c> the trace draws, 1-based.</summary>
    public int WspSetIndex
    {
        get => WspSpec?.SetIndex ?? 1;
        set
        {
            if (WspSpec is null) return;
            int i = Math.Max(1, value);
            if (i == WspSpec.SetIndex) return;
            WspSpec.SetIndex = i;
            OnPropertyChanged();
            ApplyWspEdit();
        }
    }

    // ── readouts (R-wsp4-7) ──────────────────────────────────────────────────

    private WspReadout? _wspReadout;

    /// <summary>Kurokawa's start-up frequencies, the encirclement count, or a margin's minimum and
    /// the search that pairs with it — whichever this metric's own reading is. See
    /// <see cref="WspReadouts"/> for which is which and why the margin carries both.</summary>
    public string WspReadoutText => _wspReadout?.Text ?? "";

    public bool ShowWspReadout => WspReadoutText.Length > 0;

    /// <summary>Enabled only when there is something to mark — a reading of "none" is a real answer
    /// and leaves nothing to place.</summary>
    public bool CanMarkWspCrossings => (_wspReadout?.Crossings.Count ?? 0) > 0;

    public string MarkWspCrossingsTooltip
        => CanMarkWspCrossings
            ? $"Place one marker at each of the {_wspReadout!.Value.Crossings.Count} reported " +
              "frequencies on this trace."
            : "Nothing was reported to mark.";

    /// <summary>
    /// One marker per reported frequency, on this trace (R-wsp4-7).
    ///
    /// <para>Added directly rather than through the undo stack: the Plot Inspector holds no
    /// container or display reference, which is why nothing else on this card pushes an undo entry
    /// either. A marker is removed the ordinary way.</para>
    /// </summary>
    [RelayCommand]
    private void MarkWspCrossings()
    {
        if (_wspReadout is not { } r || r.Crossings.Count == 0) return;

        int next = _trace.Markers.Count == 0 ? 1 : _trace.Markers.Max(m => m.Index) + 1;
        foreach (double f in r.Crossings)
        {
            if (_trace.Markers.Any(m => Math.Abs(m.Freq - f) < 1e-6)) continue;   // already marked
            _trace.Markers.Add(new Marker(_trace, f, isMulti: false, isDelta: false,
                                          index: next++, _parent.FreqUnit));
        }
        _parent.RebuildAndNotify();
        _parent.NotifyStructureChanged();
    }

    // ── keeping the card in step ─────────────────────────────────────────────

    /// <summary>Re-resolves the trace after a probe/option edit and refreshes every derived field.
    /// One place, so a control added later cannot forget half of it.</summary>
    private void ApplyWspEdit()
    {
        EnsureWspVirtualGroups();
        _trace.Expression = _trace.BuildPickerExpression();
        SyncWspSlice();
        _parent.RebuildAndNotify();
        RefreshDescription();   // itself refreshes this section — see RefreshDescription
    }

    /// <summary>
    /// Materializes the virtual NETWORK groups the pair and set pickers enable (R-wsp4-6's second
    /// half): the four two-ports a probe pair brackets, and <c>wsp_ymatrix</c> over an ordered set.
    ///
    /// <para><b>Picking the pair is what creates them, and that is the design.</b> A run with N
    /// probes has N(N-1) ordered pairs and four blocks each; materializing all of them so they could
    /// be browsed would be thousands of cubes on the 30-probe matrices §8's NDF work contemplates.
    /// The brief's own sentence is "a second picker enables the pair group" — so the picker is the
    /// gesture, and <see cref="DataSourceView.EnsureWspPairBlockGroups"/> is idempotent, so
    /// changing the pair adds the new blocks and leaves the old ones (which are still true).</para>
    /// </summary>
    private void EnsureWspVirtualGroups()
    {
        if (WspSpec is not { } spec || _trace.CubeName is not { } cn) return;
        if (ResolveTraceSourceEntry()?.Data is not { } ds) return;

        string group = WspSource.GroupOf(cn);
        bool added = false;
        if (spec.With.Length > 0 && spec.Probe.Length > 0)
            added |= DataSourceView.EnsureWspPairBlockGroups(ds, group, spec.Probe, spec.With);
        if (spec.Set.Count > 0)
            added |= DataSourceView.EnsureWspProbeSetGroup(ds, group, spec.Set);

        // The item picker lists a source's GROUPS, so a group that appeared after it was built is a
        // group nobody can select. Rebuilding it is what puts the blocks in front of the reader who
        // just asked for the pair.
        if (added) RebuildSignals();
    }

    /// <summary>
    /// Re-authors the trace's slice when the metric it now draws has DIFFERENT axes from the one it
    /// drew before.
    ///
    /// <para>Every ordinary probe metric is shaped over the run's own leading axes, which is what
    /// the item picker's slice is built from. An ENVELOPE metric is not: it carries four grid axes
    /// of its own (<c>rhoS</c>, <c>thetaS</c>, <c>rhoL</c>, <c>thetaL</c>) and <c>SMenv</c> has no
    /// frequency axis at all. A slice authored against the old axes would pin axes the cube does not
    /// have and leave its own unpinned — which draws the first grid point and calls it the
    /// answer.</para>
    ///
    /// <para>Matched by NAME and only when the names differ, so an ordinary edit (a different probe,
    /// a new Z0) leaves a slice the reader has arranged exactly as it was.</para>
    /// </summary>
    private void SyncWspSlice()
    {
        if (WspSpec is not { } spec || _trace.CubeName is not { } cn) return;
        if (ResolveTraceSourceEntry()?.Data is not { } ds) return;
        if (!WspSource.TryEvaluate(ds, cn, spec, out var cube, out _) || cube is null) return;

        var want = cube.Axes.Select(a => a.Name).ToArray();
        var have = _trace.Slice?.Select(s => s.AxisName).ToArray() ?? [];
        if (want.SequenceEqual(have)) return;

        var axes = cube.Axes.ToArray();
        _trace.Slice = BuildDefaultSlice(axes, DefaultEnvelopeXAxis(axes));
        RebuildAxisRoles();
    }

    /// <summary>
    /// Which axis a freshly re-authored probe slice keeps as X: the frequency axis when there is
    /// one, otherwise the pulled side's θ — because [E] Fig. 6-9 plot the margin envelope AGAINST
    /// PHASE, and a length-1 axis is not a phase sweep.
    /// </summary>
    private static int DefaultEnvelopeXAxis(RfCore.Data.Axis[] axes)
    {
        for (int d = 0; d < axes.Length; d++)
            if (axes[d].Name is "freq" or "ssfreq") return d;
        for (int d = 0; d < axes.Length; d++)
            if (axes[d].Name is "thetaS" or "thetaL" && axes[d].Length > 1) return d;
        return DefaultXAxis(axes);
    }

    /// <summary>
    /// Rebuilds the probe list from the trace's own source and re-reads the readout. Called
    /// whenever the trace's identity or the loaded data can have changed.
    /// </summary>
    public void RefreshWspSection()
    {
        _syncingWsp = true;
        try
        {
            var ds = ResolveTraceSourceEntry()?.Data;
            var probes = _trace.CubeName is { } cn && ds is not null
                ? WspSource.Probes(ds, WspSource.GroupOf(cn))
                : [];

            var items  = probes.Select(p => $"{p.Label}  (idx {p.Idx})").ToList();
            var labels = probes.Select(p => p.Label).ToList();

            if (!items.SequenceEqual(WspProbeItems))
            {
                WspProbeItems.Clear();
                foreach (string s in items) WspProbeItems.Add(s);
                _wspProbeLabels.Clear();
                _wspProbeLabels.AddRange(labels);

                // The two envelope sides carry the same rows plus an explicit "not pulled" one at the
                // top, so turning a side off is a choice on the same control rather than a second
                // checkbox beside it.
                WspSideProbeItems.Clear();
                WspSideProbeItems.Add(WspSideOffItem);
                foreach (string s in items) WspSideProbeItems.Add(s);
            }

            if (WspSpec is { } spec)
            {
                // A probe the display was saved against and this run does not have keeps its LABEL
                // on the spec — the resolve reports it by name with the run's own list beside it,
                // which is more use than silently re-pointing the trace at whatever is first.
                _selectedWspProbeItem = ItemOfLabel(spec.Probe) is { Length: > 0 } pi ? pi : null;
                _selectedWspWithItem  = ItemOfLabel(spec.With)  is { Length: > 0 } wi ? wi : null;
                _selectedWspSourceItem = spec.SourceProbe.Length == 0
                    ? WspSideOffItem
                    : ItemOfLabel(spec.SourceProbe) is { Length: > 0 } si ? si : null;
                _selectedWspLoadItem = spec.LoadProbe.Length == 0
                    ? WspSideOffItem
                    : ItemOfLabel(spec.LoadProbe) is { Length: > 0 } li ? li : null;
                _wspReadout = ds is not null
                    ? WspReadouts.For(_trace, ds, _parent.PlotType)
                    : null;
            }
            else
            {
                _selectedWspProbeItem  = null;
                _selectedWspWithItem   = null;
                _selectedWspSourceItem = null;
                _selectedWspLoadItem   = null;
                _wspReadout            = null;
            }
        }
        catch
        {
            // A readout is a convenience beside a curve that already drew — never the thing that
            // takes an unsaved workspace down. TraceResolve's own catch states the rule.
            _wspReadout = null;
        }
        finally { _syncingWsp = false; }

        OnPropertyChanged(nameof(ShowWspSection));
        OnPropertyChanged(nameof(SelectedWspProbeItem));
        OnPropertyChanged(nameof(SelectedWspWithItem));
        OnPropertyChanged(nameof(WspSetText));
        OnPropertyChanged(nameof(ShowWspWith));
        OnPropertyChanged(nameof(ShowWspSet));
        OnPropertyChanged(nameof(ShowWspZ0));
        OnPropertyChanged(nameof(ShowWspEnvelope));
        OnPropertyChanged(nameof(ShowWspPassive));
        OnPropertyChanged(nameof(SelectedWspSourceItem));
        OnPropertyChanged(nameof(SelectedWspLoadItem));
        OnPropertyChanged(nameof(WspGammaSText));
        OnPropertyChanged(nameof(WspGammaLText));
        OnPropertyChanged(nameof(WspThetaStepDeg));
        OnPropertyChanged(nameof(WspPassiveText));
        OnPropertyChanged(nameof(WspEnvelopeGridText));
        OnPropertyChanged(nameof(WspZ0Text));
        OnPropertyChanged(nameof(WspActiveSide));
        OnPropertyChanged(nameof(WspSetIndex));
        OnPropertyChanged(nameof(WspReadoutText));
        OnPropertyChanged(nameof(ShowWspReadout));
        OnPropertyChanged(nameof(CanMarkWspCrossings));
        OnPropertyChanged(nameof(MarkWspCrossingsTooltip));
        MarkWspCrossingsCommand.NotifyCanExecuteChanged();
    }
}
