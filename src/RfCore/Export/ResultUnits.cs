// ================================================================
//  ResultUnits.cs — what a result cube's numbers are IN.
//
//  AUT-9 R-aut9-3. One run's document carried `Efficiency` reading 65.84 and `MXE_Eff` reading
//  0.7087 at the same operating point: the cube in percent, the scalar as a fraction, with nothing
//  anywhere saying which. A client that formats MXE_Eff as a percentage reports 0.7%; one that
//  multiplies Efficiency by 100 reports 6584%. Both are one plausible line of code, and the axes
//  already carry units where the values do not.
//
//  So the values carry one too. A cube states its own unit when its producer knows it
//  (DataCube.Unit) — which is the only way to tell the two spellings of PAE apart, since the
//  enriched cube is a percentage and the engine's raw one is a fraction under the same name — and
//  this table answers for the standard result vocabulary otherwise. Where neither can say, the
//  answer is the word "unknown" rather than a plausible guess: a measurement written by a designer
//  as `measure Foo = dB(S(2,1))` has whatever unit that expression has, and inventing one would put
//  this file's opinion in a document a client is entitled to trust.
// ================================================================

using System;
using System.Collections.Generic;

namespace RfCore.Export
{
    /// <summary>
    /// The unit vocabulary for result cubes, keyed by the name a run publishes them under.
    ///
    /// <para><b>Spellings.</b> SI symbols as they are written (<c>Hz</c>, <c>V</c>, <c>A</c>,
    /// <c>W</c>, <c>Ohm</c>, <c>F</c>, <c>H</c>, <c>S</c>, <c>K</c>, <c>m</c>, <c>s</c>), the
    /// logarithmic units as they are written (<c>dB</c>, <c>dBm</c>), <c>%</c> for a ratio already
    /// scaled to a percentage, <see cref="Dimensionless"/> for one that is not, and
    /// <see cref="Unknown"/> for a quantity circuitRF cannot state. <c>index</c> and <c>port</c> are
    /// counters, not measurements — the axes use the same two words.</para>
    /// </summary>
    public static class ResultUnits
    {
        /// <summary>A ratio, a reflection coefficient, an S-parameter: a number with no unit. Spelled
        /// as SI spells a dimensionless quantity rather than as an empty string, so "this quantity is
        /// dimensionless" is distinguishable from "nobody filled this in".</summary>
        public const string Dimensionless = "1";

        /// <summary>circuitRF cannot state this quantity's unit — a designer's own measurement
        /// expression, most often. Explicit, because a caller must know not to guess.</summary>
        public const string Unknown = "unknown";

        /// <summary>A flag, an index or a count. Not a measurement, and not dimensionless either:
        /// scaling one is meaningless rather than merely unnecessary.</summary>
        public const string Index = "index";

        private static readonly Dictionary<string, string> Table = new(StringComparer.Ordinal)
        {
            // ── network parameters and references ────────────────────────────
            ["S"]                 = Dimensionless,
            ["Gamma"]             = Dimensionless,
            ["GammaLoad"]         = Dimensionless,
            ["gamma_ld1"]         = Dimensionless,
            ["gamma_src1"]        = Dimensionless,
            ["Z0"]                = "Ohm",
            ["ZLoad"]             = "Ohm",
            ["ZSource"]           = "Ohm",
            ["Zc"]                = "Ohm",
            ["Zin_real"]          = "Ohm",
            ["Zin_imag"]          = "Ohm",

            // ── voltages, currents, powers ───────────────────────────────────
            ["V"]                 = "V",
            ["V_linear"]          = "V",
            ["I"]                 = "A",
            ["I_linear"]          = "A",
            ["Iin"]               = "A",
            ["INl"]               = "A",
            ["BiasVLoad"]         = "V",
            ["BiasVSrc"]          = "V",
            ["BiasILoad"]         = "A",
            ["BiasISrc"]          = "A",
            ["Pout"]              = "W",     // the engine's own raw name; Enrich splits it in two
            ["Pout_W"]            = "W",
            ["Pout_dBm"]          = "dBm",
            ["Pdc"]               = "W",
            ["Pdc_W"]             = "W",
            ["PavlDbm"]           = "dBm",
            ["Pavl"]              = "dBm",
            ["MXP_PoutDbm"]       = "dBm",

            // ── gains, losses, ratios ────────────────────────────────────────
            ["Gt"]                = "dB",
            ["Gp"]                = "dB",
            ["Gt_dB"]             = "dB",
            ["Gp_dB"]             = "dB",
            ["IRL_dB"]            = "dB",
            ["AttenDbPerM"]       = "dB/m",
            ["AMPM_deg"]          = "deg",
            ["CalElectricalDeg"]  = "deg",

            // The two spellings of a dimensionless ratio, and the whole of R-aut9-3's evidence.
            // `DE` and `PAE` are what the engine publishes — fractions. `Efficiency` is what
            // LoadpullPostProcessor.Enrich publishes, and it has multiplied by 100; the SAME
            // post-processor scales `PAE` in place under its own name, which is why that one cannot
            // be answered from the name alone and carries DataCube.Unit instead.
            ["DE"]                = Dimensionless,
            ["PAE"]               = Dimensionless,
            ["Efficiency"]        = "%",
            ["MXE_Eff"]           = Dimensionless,

            // ── transmission-line and material quantities ────────────────────
            ["Eeff"]              = Dimensionless,
            ["Rpul"]              = "Ohm/m",
            ["Lpul"]              = "H/m",
            ["Gpul"]              = "S/m",
            ["Cpul"]              = "F/m",
            ["C"]                 = "F",
            ["L"]                 = "H",
            ["Cj"]                = "F",
            ["Cjsw"]              = "F/m",
            ["W"]                 = "m",
            ["Tnom"]              = "K",
            ["TC1"]               = "1/K",
            ["TC2"]               = "1/K^2",

            // ── frequency carriers ───────────────────────────────────────────
            ["__Freq"]            = "Hz",
            ["ToneFreqs"]         = "Hz",

            // ── impedances the pursuit reports as bare reals ─────────────────
            ["MXP_ZRe"]           = "Ohm",
            ["MXP_ZIm"]           = "Ohm",
            ["MXP_ZsourceRe"]     = "Ohm",
            ["MXP_ZsourceIm"]     = "Ohm",
            ["MXE_ZRe"]           = "Ohm",
            ["MXE_ZIm"]           = "Ohm",
            ["MXE_ZsourceRe"]     = "Ohm",
            ["MXE_ZsourceIm"]     = "Ohm",

            // ── flags, counts and residuals ──────────────────────────────────
            ["Converged"]         = Index,
            ["IsTickle"]          = Index,
            ["StopCode"]          = Index,
            ["MXP_Converged"]     = Index,
            ["MXE_Converged"]     = Index,
            ["MXP_HasZsource"]    = Index,
            ["MXE_HasZsource"]    = Index,
            ["CacheCount"]        = Index,
            ["UnscorableCount"]   = Index,
            ["RecommTermCount"]   = Index,
            ["MetaMixOrder"]      = Index,
            ["CalibrationUsable"] = Index,
            ["DeembedRejected"]   = Index,
            ["__lpEnriched"]      = Index,
            ["__SrcNodeIdx"]      = Index,
            ["__LoadNodeIdx"]     = Index,
            ["__ProbeBranches"]   = Index,
            ["__LabeledNodes"]    = Index,
            ["__OpVars"]          = Index,
            ["__SrcZ"]            = "Ohm",

            // Residuals are ratios of the quantity they measure against itself.
            ["Residual"]              = Dimensionless,
            ["DeembedResidual"]       = Dimensionless,
            ["ModeCouplingResidual"]  = Dimensionless,

            // The DC operating-point bag: one cube holding a device's own op variables, each with
            // its own unit. Naming one for the cube would be wrong for most of its entries.
            ["OP"]                = Unknown,
        };

        /// <summary>
        /// This cube's unit: its own <see cref="Data.DataCube.Unit"/> when its producer stated one,
        /// otherwise the vocabulary's answer for that name, otherwise <see cref="Unknown"/>.
        ///
        /// <para>A qualified spelling (<c>SP1.S</c>) resolves on its last segment, because the group
        /// is which analysis published it and not what the numbers are.</para>
        /// </summary>
        public static string For(string cubeName, Data.DataCube? cube = null)
        {
            if (cube is not null && !string.IsNullOrEmpty(cube.Unit)) return cube.Unit;

            string name = cubeName;
            int dot = name.LastIndexOf('.');
            if (dot >= 0 && dot + 1 < name.Length) name = name[(dot + 1)..];

            return Table.TryGetValue(name, out string? u) ? u : Unknown;
        }
    }
}
