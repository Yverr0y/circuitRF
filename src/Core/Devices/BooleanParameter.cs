using CircuitRF.Core.Expressions;

namespace CircuitRF.Core.Devices;

/// <summary>
/// The one reading of a boolean-valued component parameter — <c>BiasTee</c>, <c>RefPin</c>,
/// <c>RefNode</c>, <c>IncludeCapacitance</c>, <c>OpVars</c>, wBond's <c>GroundPlane</c>.
///
/// <para><b>Why this exists (AUT-8 R-aut8-5).</b> There were three rules for one concept and every
/// one of them was silent about what it did not recognise:</para>
/// <list type="bullet">
/// <item><c>BiasTee</c> tested for the literal string <c>"on"</c>. <c>true</c>, <c>1</c>, <c>True</c>
/// and <c>Yes</c> were each accepted and each produced a Tuner with no bias at all — the circuit
/// quietly differing from the document, which is the one outcome nothing downstream can notice.</item>
/// <item><c>IsTrue</c> tested for the literal string <c>"true"</c>, so <c>on</c> was false.</item>
/// <item>The Verilog-A op-vars flag inverted the sloppiness: it recognised four spellings of FALSE
/// and read <b>everything else</b>, including a typo, as true.</item>
/// </list>
///
/// <para><b>The two halves are equally important.</b> Widening the accepted spellings without
/// refusing the rest just moves the silent boundary; refusing without widening makes a document that
/// reads perfectly well to a person into an error. So: the ordinary spellings all work, and anything
/// else is refused BY NAME with the list — which is the pattern the artwork half of this surface
/// already sets with its unknown-technology refusal.</para>
/// </summary>
public static class BooleanParameter
{
    private static readonly string[] _true  = ["true",  "on",  "yes", "1"];
    private static readonly string[] _false = ["false", "off", "no",  "0"];

    /// <summary>The spellings, for a refusal message and for the generated reference.</summary>
    public static IReadOnlyList<string> TrueSpellings  => _true;
    public static IReadOnlyList<string> FalseSpellings => _false;

    /// <summary>
    /// Reads a resolved parameter as a boolean, or throws naming the parameter, what it was given and
    /// what it accepts.
    /// </summary>
    /// <param name="parameterName">The parameter's own name, so the refusal names the thing to change.</param>
    /// <param name="value">The resolved value. A Real is non-zero-is-true; a Bool is itself.</param>
    public static bool Parse(string parameterName, Value value)
        => TryParse(value, out bool b)
           ? b
           : throw new InvalidOperationException(
               $"{parameterName}={Describe(value)} is not something circuitRF can read as yes or no. " +
               $"True: {string.Join(", ", _true)}. False: {string.Join(", ", _false)}.");

    /// <summary>
    /// Reads a resolved parameter as a boolean, defaulting when it is absent. The <b>absent</b> case
    /// takes the default; a PRESENT value that is not a boolean spelling is still refused, because
    /// falling back there is exactly how <c>BiasTee=true</c> came to mean <c>off</c>.
    /// </summary>
    public static bool Parse(IReadOnlyDictionary<string, Value> parameters,
                             string parameterName, bool whenAbsent)
        => parameters.TryGetValue(parameterName, out var v) ? Parse(parameterName, v) : whenAbsent;

    /// <summary>The non-throwing form, for a caller that has its own report to make.</summary>
    public static bool TryParse(Value value, out bool result)
    {
        switch (value.Kind)
        {
            case ValueKind.Bool:
                result = value.AsBool();
                return true;

            // A number is unambiguous and every netlist dialect writes 1/0 for a flag. Anything other
            // than exactly 0 or 1 is not a flag, though — a boolean parameter given 2.5 is a mistake
            // about which parameter is being set, not a spelling of "true".
            case ValueKind.Real:
            {
                double d = value.AsReal();
                result = d != 0.0;
                return d is 0.0 or 1.0;
            }

            case ValueKind.String:
            {
                string s = value.AsString().Trim();
                if (_true .Any(t => s.Equals(t, StringComparison.OrdinalIgnoreCase))) { result = true;  return true; }
                if (_false.Any(t => s.Equals(t, StringComparison.OrdinalIgnoreCase))) { result = false; return true; }
                result = false;
                return false;
            }

            default:
                result = false;
                return false;
        }
    }

    private static string Describe(Value v) => v.Kind switch
    {
        ValueKind.String => $"'{v.AsString()}'",
        ValueKind.Real   => v.AsReal().ToString(System.Globalization.CultureInfo.InvariantCulture),
        ValueKind.Bool   => v.AsBool() ? "true" : "false",
        _                => v.Kind.ToString(),
    };
}
