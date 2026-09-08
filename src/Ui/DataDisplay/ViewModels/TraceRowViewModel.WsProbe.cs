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
        _trace.Expression = _trace.BuildPickerExpression();
        _parent.RebuildAndNotify();
        RefreshDescription();   // itself refreshes this section — see RefreshDescription
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
            }

            if (WspSpec is { } spec)
            {
                // A probe the display was saved against and this run does not have keeps its LABEL
                // on the spec — the resolve reports it by name with the run's own list beside it,
                // which is more use than silently re-pointing the trace at whatever is first.
                _selectedWspProbeItem = ItemOfLabel(spec.Probe) is { Length: > 0 } pi ? pi : null;
                _selectedWspWithItem  = ItemOfLabel(spec.With)  is { Length: > 0 } wi ? wi : null;
                _wspReadout = ds is not null
                    ? WspReadouts.For(_trace, ds, _parent.PlotType)
                    : null;
            }
            else
            {
                _selectedWspProbeItem = null;
                _selectedWspWithItem  = null;
                _wspReadout           = null;
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
