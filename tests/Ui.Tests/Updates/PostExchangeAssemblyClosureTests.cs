using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using CircuitRF.Ui.Updates;

namespace CircuitRF.Ui.Tests.Updates;

/// <summary>
/// The rule <c>NativeLaunch</c> states, enforced instead of remembered: <b>nothing reachable from the
/// hand-over may reference an assembly that is not already loaded when the bundle is exchanged.</b>
///
/// <para><b>Why a mechanical gate and not another source scan</b> (owner report, 2026-09-13, beta.19
/// to beta.20 — the fourth in the series). beta.19 shipped the fix for the third: the hand-over spawns
/// <c>/usr/bin/open</c> through <c>libc</c> instead of <c>System.Diagnostics.Process</c>. It crashed
/// anyway, in the same place, because a RETRY LOOP added to <c>AppRelaunch.TryRelaunchBundle</c> in
/// between slept between attempts — and <c>System.Threading.Thread</c> is its own assembly, and is not
/// among the 31 a launch has loaded by the time it exchanges its bundle. The sleep never ran: the
/// runtime resolves a method's call targets when it PREPARES the method, so the whole hand-over died
/// at the prestub before its first instruction and <c>open</c> was never spawned.</para>
///
/// <para><b>That is the shape of the whole family.</b> Every previous fix named the one call that had
/// gone wrong, and the next innocuous line brought it back. What the failure actually depends on is
/// not any particular API but the ASSEMBLY GRAPH of everything the post-exchange window can reach — so
/// that is what this reads: the IL of each method, transitively through our own code, collecting every
/// assembly any token in it refers to.</para>
///
/// <para><b>The allowed set is measured, not reasoned about.</b> It is what a real single-file
/// <c>osx-arm64</c> publish has loaded at the moment <c>UpdateSwap.SwapBundle</c> exchanges the
/// bundle — 31 assemblies, printed by running the startup sequence up to that line and asking
/// <c>AppDomain.CurrentDomain.GetAssemblies()</c>. Adding a name here is a claim that something loads
/// it before the exchange, and that claim has to be measured the same way.</para>
/// </summary>
public sealed class PostExchangeAssemblyClosureTests
{
    /// <summary>
    /// What a launch has loaded by the time it exchanges its own bundle. Measured on a single-file
    /// <c>osx-arm64</c> publish of this repository, 2026-09-13.
    /// </summary>
    private static readonly HashSet<string> LoadedAtTheExchange = new(StringComparer.Ordinal)
    {
        "Avalonia.Base", "Avalonia.Controls",
        "CircuitRF.Core", "CircuitRF.Design", "CircuitRF.Diagnostics", "CircuitRF.Render", "CircuitRF.Ui",
        "Microsoft.Win32.Primitives", "SkiaSharp",
        "System.Collections", "System.Collections.Concurrent", "System.Collections.NonGeneric",
        "System.ComponentModel.Primitives", "System.Console", "System.Diagnostics.Process",
        "System.IO.Pipelines", "System.Linq", "System.Memory", "System.Numerics.Vectors",
        "System.ObjectModel", "System.Private.CoreLib", "System.Private.Uri",
        "System.Reflection.Emit.ILGeneration", "System.Reflection.Emit.Lightweight",
        "System.Reflection.Primitives", "System.Runtime", "System.Runtime.InteropServices",
        "System.Text.Encoding.Extensions", "System.Text.Encodings.Web", "System.Text.Json",
        "System.Threading",
    };

    /// <summary>
    /// Everything the session runs after the exchange. <c>HandOverTo</c> is the whole hand-over;
    /// the two <c>Leave…</c> methods are how it ends when there is no successor to hand to;
    /// <c>RecordSwap</c> is the state write every post-exchange branch makes.
    /// </summary>
    private static readonly string[] Roots =
    [
        "HandOverTo", "LeaveTheUpdateForTheNextLaunch", "LeaveRatherThanOutliveTheExchange",
        "RecordSwap", "ThisSessionReplacedItsOwnBundle",
    ];

    [Fact]
    public void NothingTheHandOverCanReachNeedsAnAssemblyTheExchangeHasMadeUnloadable()
    {
        var start = Roots.Select(Root).ToList();

        var offenders = new SortedDictionary<string, string>(StringComparer.Ordinal);
        Walk(start, (asm, where) =>
        {
            if (!LoadedAtTheExchange.Contains(asm)) offenders.TryAdd(asm, where);
        });

        Assert.True(offenders.Count == 0,
            "These assemblies are referenced by code that runs AFTER the macOS bundle exchange, and a "
          + "single-file application cannot load one then — it re-opens its own executable by path, and "
          + "that path is a different file now. The reference does not have to be REACHED: the runtime "
          + "resolves it when it prepares the method.\n"
          + string.Join("\n", offenders.Select(o => $"    {o.Key}   (from {o.Value})"))
          + "\nEither use something already resident (libc through NativeLaunch, or the core library), "
          + "or measure that the assembly is loaded before the exchange and add it to LoadedAtTheExchange.");
    }

    /// <summary>
    /// The specific line that produced the fourth report, kept as its own failure so the diagnosis is
    /// legible when someone reintroduces it — the test above would say only "System.Threading.Thread".
    /// </summary>
    [Fact]
    public void TheLaunchServicesRetryDoesNotSleepOnThread()
    {
        Assert.DoesNotContain(
            Closure(Roots.Select(Root)).Select(m => $"{m.DeclaringType?.FullName}.{m.Name}"),
            name => name == "System.Threading.Thread.Sleep");
    }

    // ── the walk ────────────────────────────────────────────────────────────────────────────────

    private static MethodBase Root(string name)
    {
        MethodBase? m = typeof(UpdateStartup)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(x => x.Name == name);

        Assert.True(m is not null, $"UpdateStartup.{name} is not there any more — repoint this test.");
        return m!;
    }

    /// <summary>Every method any of <paramref name="roots"/> can reach, recursing through ours only.</summary>
    private static List<MethodBase> Closure(IEnumerable<MethodBase> roots)
    {
        var all = new List<MethodBase>();
        Walk(roots, (_, _) => { }, all);
        return all;
    }

    private static void Walk(IEnumerable<MethodBase> roots, Action<string, string> onAssembly,
                             List<MethodBase>? collect = null)
    {
        var seen  = new HashSet<MethodBase>();
        var queue = new Queue<MethodBase>();
        foreach (MethodBase r in roots) { if (seen.Add(r)) queue.Enqueue(r); }

        while (queue.Count > 0)
        {
            MethodBase m = queue.Dequeue();
            string where = $"{m.DeclaringType?.Name}.{m.Name}";

            foreach ((int token, MemberInfo? member) in TokensIn(m))
            {
                // WHERE THE ASSEMBLY NAME COMES FROM, and the only part of this test that is subtle.
                // A resolved runtime member answers System.Private.CoreLib for everything the facades
                // forward — so asking IT is exactly the question that cannot see the bug. The JIT
                // resolves the ASSEMBLY REFERENCE recorded in metadata (System.Threading.Thread,
                // System.Collections, …) and loads that facade, so the metadata is what to read.
                string? asm = ReferencedAssembly(m.Module, token);
                if (asm is not null) onAssembly(asm, where);

                if (member is null) continue;

                Type? owner = member as Type ?? member.DeclaringType;
                if (owner is null) continue;

                // Recurse through our own code only: an assembly that is already loaded can prepare
                // its own methods, and following the whole framework would never terminate.
                bool ours = (owner.Assembly.GetName().Name ?? "").StartsWith("CircuitRF", StringComparison.Ordinal);

                if (member is MethodBase callee)
                {
                    collect?.Add(callee);
                    if (ours && seen.Add(callee)) queue.Enqueue(callee);
                }
            }
        }
    }

    // ── metadata: which assembly REFERENCE a token names ────────────────────────────────────────

    private static readonly Dictionary<string, MetadataReader> Readers = new(StringComparer.Ordinal);

    private static MetadataReader? ReaderFor(Module module)
    {
        string path = module.Assembly.Location;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

        if (Readers.TryGetValue(path, out MetadataReader? cached)) return cached;

        var pe = new PEReader(File.OpenRead(path));      // held open for the life of the test run
        MetadataReader r = pe.GetMetadataReader();
        Readers[path] = r;
        return r;
    }

    /// <summary>The assembly a metadata token refers OUT to, or null when it stays in this module.</summary>
    private static string? ReferencedAssembly(Module module, int token)
    {
        MetadataReader? r = ReaderFor(module);
        if (r is null) return null;

        try   { return ScopeOf(r, MetadataTokens.EntityHandle(token)); }
        catch { return null; }
    }

    private static string? ScopeOf(MetadataReader r, EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeReference   => ScopeOf(r, r.GetTypeReference((TypeReferenceHandle)h).ResolutionScope),
        HandleKind.MemberReference => ScopeOf(r, r.GetMemberReference((MemberReferenceHandle)h).Parent),
        HandleKind.MethodSpecification
            => ScopeOf(r, r.GetMethodSpecification((MethodSpecificationHandle)h).Method),
        HandleKind.AssemblyReference
            => r.GetString(r.GetAssemblyReference((AssemblyReferenceHandle)h).Name),

        // A definition in this module, or a type this module owns: nothing to load.
        _ => null,
    };

    // ── IL ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every type, field and method token one method's IL names, with the runtime member each
    /// resolves to. The operand sizes come from <see cref="OpCodes"/> itself rather than a
    /// hand-written table, so a new opcode cannot silently desynchronise the decoder.
    /// </summary>
    private static IEnumerable<(int Token, MemberInfo? Member)> TokensIn(MethodBase method)
    {
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null) yield break;

        Type[]? typeArgs   = method.DeclaringType?.IsGenericType == true
                             ? method.DeclaringType.GetGenericArguments() : null;
        Type[]? methodArgs = method.IsGenericMethodDefinition ? method.GetGenericArguments() : null;

        for (int i = 0; i < il.Length; )
        {
            OpCode op;
            if (il[i] == 0xFE && i + 1 < il.Length)
            {
                if (!TwoByte.TryGetValue(il[i + 1], out op)) yield break;   // unknown: stop, do not guess
                i += 2;
            }
            else
            {
                if (!OneByte.TryGetValue(il[i], out op)) yield break;
                i += 1;
            }

            int operand = OperandSize(op, il, i);
            if (operand < 0) yield break;

            if (op.OperandType is OperandType.InlineMethod or OperandType.InlineField
                                or OperandType.InlineType or OperandType.InlineTok
                && i + 4 <= il.Length)
            {
                int token = BitConverter.ToInt32(il, i);
                MemberInfo? member = null;
                try { member = method.Module.ResolveMember(token, typeArgs, methodArgs); }
                catch { /* a token this module cannot resolve names nothing we can walk */ }
                yield return (token, member);
            }

            i += operand;
        }
    }

    private static int OperandSize(OpCode op, byte[] il, int at) => op.OperandType switch
    {
        OperandType.InlineNone                                             => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI
            or OperandType.ShortInlineVar                                  => 1,
        OperandType.InlineVar                                              => 2,
        OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI
            or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
            or OperandType.InlineTok or OperandType.InlineType
            or OperandType.ShortInlineR                                    => 4,
        OperandType.InlineI8 or OperandType.InlineR                        => 8,
        OperandType.InlineSwitch => at + 4 <= il.Length ? 4 + 4 * BitConverter.ToInt32(il, at) : -1,
        _                                                                  => -1,
    };

    private static readonly Dictionary<byte, OpCode> OneByte = Opcodes(false);
    private static readonly Dictionary<byte, OpCode> TwoByte = Opcodes(true);

    private static Dictionary<byte, OpCode> Opcodes(bool twoByte)
    {
        var map = new Dictionary<byte, OpCode>();
        foreach (FieldInfo f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (f.GetValue(null) is not OpCode op) continue;
            bool isTwo = (op.Value & 0xFF00) == 0xFE00;
            if (isTwo == twoByte) map[(byte)(op.Value & 0xFF)] = op;
        }
        return map;
    }
}
