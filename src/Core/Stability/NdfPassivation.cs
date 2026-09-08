using System.Text;
using CircuitRF.Core.Design;
using CircuitRF.Core.Elaboration;

namespace CircuitRF.Core.Stability;

/// <summary>
/// One placed component's answer to "can this run's <c>Δ0</c> be built?" — its
/// <see cref="Core.Activity"/>, the sentence describing what its passivation does, and, when it
/// cannot be passivated, the reason a refusal should print.
/// </summary>
/// <param name="InstancePath">The elaborated, dotted instance path.</param>
/// <param name="ComponentType">The netlist type name, as written.</param>
/// <param name="Activity">What the model answered for this instance and this frequency grid.</param>
/// <param name="Passivation">What the passive assembly does with it, in one line.</param>
/// <param name="Refusal">Null when the instance is fine; otherwise why it is not.</param>
public sealed record NdfInstance(
    string InstancePath, string ComponentType, Activity Activity, string Passivation, string? Refusal);

/// <summary>
/// The whole netlist's answer, produced BEFORE the frequency loop so a design that cannot yield an
/// NDF says so in one place rather than at the frequency the first offending stamp is reached.
/// </summary>
/// <param name="Instances">Every component, in netlist order.</param>
/// <param name="Refusal">Null when the run may proceed; otherwise the whole refusal, ready to throw.</param>
/// <param name="NeedsPassiveNetlist">
/// True when some instance is <see cref="Core.Activity.ActiveUserScaled"/>, i.e. the passive
/// assembly must be built from a SECOND elaboration with the user's scaling quantities at zero
/// rather than from this netlist's own <c>StampPassive</c> (R-wsp6-4).
/// </param>
public sealed record NdfSurvey(
    IReadOnlyList<NdfInstance> Instances, string? Refusal, bool NeedsPassiveNetlist);

/// <summary>
/// The NDF passivation contract, applied to a netlist — brief-wsprobe-6 §3, and the table in
/// <c>docs/design/stability-wsprobe.md</c> §11.
///
/// <para>T. A. Winslow, <i>General Circuit Analysis Using The WSProbe</i> (2023), §8 (Eq. 181–186)
/// and §5.4: <c>NDF = Δ/Δ0</c> is only meaningful when <c>Δ0</c> is the SAME network with every
/// dependent source, negative resistance and non-Foster element rendered passive, and building it
/// "requires having precise access to the transconductance elements in all active devices"
/// (p. 113). This class is where circuitRF states, per placed component, whether it has that
/// access — and refuses by NAME where it does not, rather than reporting a silently
/// passive-looking NDF.</para>
/// </summary>
public static class NdfPassivation
{
    /// <summary>The diagnostic key a refusal carries.</summary>
    public const string CannotPassivateKey = "ndf.cannot-passivate";

    /// <summary>
    /// Splits a <c>PassiveVars=</c> / <c>PassiveParams=</c> value into names. Commas, semicolons and
    /// whitespace all separate; surrounding quotes are stripped, because the directive's own example
    /// writes <c>PassiveVars="NDFgm"</c> and a quoted single name must not become a name with
    /// quotes in it.
    /// </summary>
    public static string[] ParseList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];
        string t = raw.Trim();
        if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\'')))
            t = t[1..^1];
        return [.. t.Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries
                                               | StringSplitOptions.TrimEntries)];
    }

    /// <summary>
    /// Splits one <c>PassiveParams</c> entry, <c>X1.gmscale</c>, into the instance path and the
    /// parameter name — the LAST dot separates them, because an instance path may itself be dotted.
    /// A bare word (no dot) has no instance and is returned with a null path.
    /// </summary>
    public static (string? Instance, string Param) SplitParamRef(string entry)
    {
        int dot = entry.LastIndexOf('.');
        return dot <= 0 ? (null, entry) : (entry[..dot], entry[(dot + 1)..]);
    }

    /// <summary>
    /// Every component's passivation, and the refusal if there is one. Nothing here solves, stamps
    /// or reads the matrix: it is the question <c>explain --analysis</c> answers without running,
    /// and the question the engine asks before its first factorisation.
    /// </summary>
    public static NdfSurvey Survey(
        ElaboratedNetlist netlist,
        IReadOnlyList<double> freqsHz,
        IReadOnlyList<string> passiveVars,
        IReadOnlyList<string> passiveParams)
    {
        var reports  = new List<NdfInstance>(netlist.Components.Count);
        var refusals = new List<string>();
        bool needsPassiveNetlist = false;

        // A PassiveVars name that is not a global at all is the first thing to say: it is a typo,
        // and every later "nothing reaches this device" would be its downstream symptom.
        foreach (string v in passiveVars)
            if (!netlist.ResolvedGlobals.ContainsKey(v))
                refusals.Add(
                    $"PassiveVars names '{v}', which is not a global variable of this netlist. " +
                    "The passive assembly is built by re-elaborating with each named global at 0, " +
                    "so a name that is not a global scales nothing — which is the classic silent " +
                    "failure of a hand-built NDF.");

        var reachedVars   = new HashSet<string>(StringComparer.Ordinal);
        var reachedParams = new HashSet<string>(StringComparer.Ordinal);

        foreach (var ec in netlist.Components)
        {
            var act = ec.ActivityFor(freqsHz);
            string? note = ec.Model.PassivationNote;
            string? refusal = null;
            string what;

            switch (act)
            {
                case Activity.Passive:
                    what = note is null ? "passive — stamped as it is" : $"passive ({note})";
                    break;

                case Activity.ActiveExact:
                    what = DescribeExact(ec);
                    break;

                case Activity.ActiveUserScaled:
                {
                    var vars  = passiveVars.Where(v => ec.Parameters.ContainsKey(v)).ToArray();
                    var pars  = passiveParams
                        .Select(SplitParamRef)
                        .Where(r => r.Instance is not null
                                 && string.Equals(r.Instance, ec.InstancePath, StringComparison.Ordinal)
                                 && ec.Parameters.ContainsKey(r.Param))
                        .Select(r => $"{r.Instance}.{r.Param}")
                        .ToArray();

                    foreach (var v in vars) reachedVars.Add(v);
                    foreach (var p in pars) reachedParams.Add(p);

                    if (vars.Length == 0 && pars.Length == 0)
                    {
                        refusal =
                            $"{ec.ComponentType} '{ec.InstancePath}' carries user-written activity " +
                            "that circuitRF cannot identify, and nothing passivates it. Multiply its " +
                            "controlled term by a global and name that global in PassiveVars=, or " +
                            "name an instance parameter the model exposes in " +
                            $"PassiveParams=\"{ec.InstancePath}.<param>\".";
                        what = "NOT PASSIVATED";
                    }
                    else
                    {
                        needsPassiveNetlist = true;
                        what = "re-elaborated with " +
                               string.Join(", ", vars.Concat(pars)) + " = 0";
                    }
                    break;
                }

                default:   // BlackBox
                    refusal =
                        $"{ec.ComponentType} '{ec.InstancePath}' cannot be passivated" +
                        (note is null ? "." : $": {note}.");
                    what = note is null ? "NOT PASSIVATED" : $"NOT PASSIVATED — {note}";
                    break;
            }

            if (refusal is not null) refusals.Add(refusal);
            reports.Add(new NdfInstance(ec.InstancePath, ec.ComponentType, act, what, refusal));
        }

        // A named scaling quantity that IS a global but reaches no user-scaled device scales
        // nothing — the same silent failure, one step further along.
        foreach (string v in passiveVars)
            if (netlist.ResolvedGlobals.ContainsKey(v) && !reachedVars.Contains(v))
                refusals.Add(
                    $"PassiveVars names the global '{v}', but no device reads it — it scales " +
                    "nothing, so setting it to 0 would leave Δ0 active and the encirclement count " +
                    "meaningless.");

        foreach (string p in passiveParams)
            if (!reachedParams.Contains(p))
            {
                var (inst, param) = SplitParamRef(p);
                refusals.Add(inst is null
                    ? $"PassiveParams names '{p}', which is not spelled <instance>.<parameter>."
                    : $"PassiveParams names '{p}', but no instance '{inst}' with a parameter " +
                      $"'{param}' is present in the elaborated netlist.");
            }

        string? whole = null;
        if (refusals.Count > 0)
        {
            var sb = new StringBuilder();
            sb.Append(CannotPassivateKey).AppendLine(": the normalized determinant function needs a");
            sb.AppendLine("PASSIVE version of this circuit (Δ0), and this design has parts that cannot supply one.");
            foreach (string r in refusals) sb.Append("  • ").AppendLine(r);
            sb.Append("Winslow, General Circuit Analysis Using The WSProbe (2023), §5.4 and §8: the passive ");
            sb.Append("determinant requires precise access to the transconductance elements in every active device.");
            whole = sb.ToString();
        }

        return new NdfSurvey(reports, whole, needsPassiveNetlist);
    }

    /// <summary>What an <see cref="Activity.ActiveExact"/> instance's passivation does, in one line
    /// — the table of <c>docs/design/stability-wsprobe.md</c> §11, generated from the code so the
    /// two cannot disagree.</summary>
    private static string DescribeExact(ElaboratedComponent ec)
    {
        if (ec.Model.PassivationNote is { } own) return $"exact — {own}";

        var ctl = ec.Model.ControlledConductances;
        if (ctl.Count > 0)
        {
            string terms = string.Join(", ", ctl.Select(t => $"∂I{t.P}/∂V{t.Q}"));
            return ec.Model.PassivatesTranscapacitance
                ? $"exact — {terms} → 0, transcapacitance removed, every other conductance and " +
                   "capacitance kept at bias"
                : $"exact — {terms} → 0";
        }
        return "exact — the model stamps its own passivated form";
    }

    // ── The second elaboration (R-wsp6-4) ────────────────────────────────────

    /// <summary>
    /// A netlist elaborated with every <paramref name="passiveVars"/> global and every
    /// <paramref name="passiveParams"/> instance parameter forced to <b>0</b> — the assembly
    /// <c>Δ0</c> is built from when a user-scaled device is present. Applied exactly as
    /// <c>--set var=expr</c> is, i.e. before elaboration, so everything derived from the named
    /// global re-derives.
    ///
    /// <para><paramref name="tb"/> is mutated and RESTORED around the call, because that is how
    /// <c>--set</c> already works and the elaborator reads the TestBench's own lists. Call it from
    /// the same place the other elaborations of a run are made — serially, before any worker
    /// starts.</para>
    ///
    /// <para><b>A dotted PassiveParams instance path is refused, not silently ignored.</b> Adding an
    /// override to an instance inside a sub-cell would mean editing a shared <c>Cell</c>, which
    /// would change every other instance of it in the same run.</para>
    /// </summary>
    public static ElaboratedNetlist BuildPassiveNetlist(
        Library lib, TestBench tb, string? baseDirectory,
        IReadOnlyList<string> passiveVars, IReadOnlyList<string> passiveParams)
    {
        var savedGlobals   = tb.GlobalVariables.ToList();
        var savedInstances = tb.Instances.ToList();
        try
        {
            foreach (string v in passiveVars)
            {
                tb.GlobalVariables.RemoveAll(g => g.Name == v);
                tb.GlobalVariables.Add(new Variable(v, "0"));
            }

            foreach (string entry in passiveParams)
            {
                var (instPath, param) = SplitParamRef(entry);
                if (instPath is null) continue;
                if (instPath.Contains('.'))
                    throw new InvalidOperationException(
                        $"{CannotPassivateKey}: PassiveParams names '{entry}', whose instance lives " +
                        "inside a sub-cell. Only a top-level instance can be overridden here — an " +
                        "override on a nested one would have to edit the shared cell definition and " +
                        "would change every other instance of it in the same run. Promote the " +
                        "device to the testbench, or scale it through a global named in PassiveVars.");

                int at = tb.Instances.FindIndex(i => i.InstanceName == instPath);
                if (at < 0) continue;   // Survey has already refused this by name
                var old = tb.Instances[at];
                var overrides = old.Overrides.Where(o => o.Name != param).ToList();
                overrides.Add(new ParameterAssignment(param, "0"));
                tb.Instances[at] = new Instance(old.InstanceName, old.Reference, old.NetBindings, overrides)
                {
                    RefNetBinding = old.RefNetBinding,
                };
            }

            return new Elaborator(lib) { BaseDirectory = baseDirectory }.Elaborate(tb);
        }
        finally
        {
            tb.GlobalVariables.Clear();
            tb.GlobalVariables.AddRange(savedGlobals);
            tb.Instances.Clear();
            tb.Instances.AddRange(savedInstances);
        }
    }
}
