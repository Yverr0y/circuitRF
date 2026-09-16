using System.Runtime.InteropServices;
using System.Text;

namespace CircuitRF.Ui.Diagnostics;

/// <summary>
/// Opt-in diagnostic for the intermittent macOS "only a circuitRF menu, no File/Edit/..." launch
/// (owner-reported, roughly 1 in 20 under <c>dotnet run</c>, never yet seen from the packaged .app).
/// Reads the REAL menu bar (<c>NSApp.mainMenu</c>) plus the managed NativeMenu state, so a bad launch
/// can be told from a good one from inside the process. Gated on <c>CRF_MENU_DIAG=&lt;logfile&gt;</c>;
/// inert otherwise. Not reproduced in 61 instrumented launches (2026-08-26) — this exists to catch
/// the next one in the act.
///
/// <para><b>What the menu bar actually depends on</b>, read out of <c>libAvaloniaNative.dylib</c>'s
/// own disassembly rather than assumed:
/// <code>
/// -[AvnWindow becomeKeyWindow]     -> showWindowMenuWithAppMenu   // NSWindow OVERRIDE, synchronous
/// -[AvnWindow windowDidResignKey:] -> showAppMenuOnly             // DELEGATE NOTIFICATION, unconditional
/// WindowBaseImpl::SetMainMenu(m)   -> applyMenu:, then showWindowMenuWithAppMenu ONLY if [w isKeyWindow]
/// </code>
/// The window's menu is INSTALLED in exactly one place: <c>becomeKeyWindow</c>. circuitRF's menu is
/// handed to the exporter in <c>WorkspaceWindow</c>'s XAML, i.e. in the window CONSTRUCTOR, long
/// before the window is shown — so <c>SetMainMenu</c>'s <c>isKeyWindow</c> branch is false and only
/// <c>applyMenu:</c> runs. Everything then rides on a later <c>becomeKeyWindow</c>.
/// <c>showAppMenuOnly</c> is a one-item bar, rendered as the process name.</para>
///
/// <para><b>A late-teardown race was the first theory and the owner REFUTED it (2026-08-26):</b> when
/// it happens, clicking away to another app and back does NOT bring the menu back. A real
/// resign/become cycle runs <c>becomeKeyWindow</c> again, and that calls
/// <c>showWindowMenuWithAppMenu</c> unconditionally — so a bar that stays bare means that method took
/// its own fallback branch, which it does when the window's <c>_menu</c> is empty/nil. In other
/// words the REAL menu never reached <c>SetMainMenu</c>, which the managed exporter calls ONLY on its
/// FIRST bind (<c>if (flag) avnWindow?.SetMainMenu(...)</c>). Prime suspect: something hands that
/// window's exporter a menu BEFORE the XAML one arrives — note that
/// <c>AvaloniaNativeMenuExporter.SetNativeMenu(null)</c> substitutes a brand-new EMPTY
/// <c>NativeMenu</c> (<c>_menu = menu ?? new NativeMenu()</c>), which would bind and apply an empty
/// bar permanently. <c>applied=</c> in the log is what distinguishes these: <c>NIL</c> = never
/// applied, <c>0</c>/<c>1</c> = bound to an empty menu first, <c>9</c> = applied fine and the fault
/// is elsewhere.</para>
///
/// <para><b>Reading the log.</b> <c>mainMenu[9]</c> is healthy. <c>mainMenu[1]</c> with
/// <c>winKey=False</c> is NORMAL — Avalonia reverts the bar whenever no window is key, and it is
/// restored on the next <c>becomeKeyWindow</c>. The BUG signature is <c>mainMenu[1]</c> together with
/// <c>winKey=True</c>. <c>appActive</c> separates "app frontmost but no key window" from
/// "key window with a stale bar" — a different fix for each. <c>key=</c>/<c>main=</c> name the
/// NSWindow AppKit itself calls key and main, which is not always the window Avalonia reports as
/// active: a bare bar whose <c>key=</c> is a WorkspaceWindow says the install never ran, while a bare
/// bar with <c>key=none</c> says AppKit simply has no key window to install from.</para>
///
/// <para><b>A second, repeatable report (owner, macOS 27, 2026-09-15):</b> open and close
/// <c>circuitRF ▸ About circuitRF…</c> and the bar keeps only the application menu until the user
/// switches to another application and back. That one DOES recover on a resign/become cycle, so it is
/// not the launch fault above — it is the window's own <c>becomeKeyWindow</c> not running, or running
/// before the dialog's <c>windowDidResignKey:</c> tore the bar down again. It did not reproduce under
/// automation: opening the dialog from the managed handler, and dispatching the real
/// <c>NSMenuItem</c> action with <c>-[NSMenu performActionForItemAtIndex:]</c>, both restored
/// <c>mainMenu[9]</c> within one 60 ms sample, with <c>key=AvnWindow:circuitRF</c> and
/// <c>[NSApp modalWindow]</c> nil the whole time. What automation cannot supply is the menu-bar
/// TRACKING session a real click runs, so the capture has to come from a real one.</para>
/// </summary>
internal static class MenuBarProbe
{
    private const string Objc = "/usr/lib/libobjc.A.dylib";

    [DllImport(Objc, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Objc, EntryPoint = "sel_registerName")]
    private static extern IntPtr Sel([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr sel);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern long SendLong(IntPtr receiver, IntPtr sel);

    [DllImport(Objc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIdx(IntPtr receiver, IntPtr sel, long index);

    [DllImport(Objc, EntryPoint = "object_getClass")]
    private static extern IntPtr ObjClass(IntPtr obj);

    [DllImport(Objc, EntryPoint = "class_getName")]
    private static extern IntPtr ClassName(IntPtr cls);

    [DllImport(Objc, EntryPoint = "class_getInstanceVariable")]
    private static extern IntPtr IvarDef(IntPtr cls, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Objc, EntryPoint = "object_getIvar")]
    private static extern IntPtr IvarGet(IntPtr obj, IntPtr ivar);

    /// <summary>
    /// What menu the WINDOW itself is holding — Avalonia's <c>AvnWindow._menu</c>, set by
    /// <c>applyMenu:</c> from <c>WindowBaseImpl::SetMainMenu</c>. THE decisive field: when the bar is
    /// bare and this reads <c>nil</c>, the real menu never reached the native side at all and the
    /// fault is on the managed exporter's side, not AppKit's. Read through the public ObjC runtime
    /// (<c>object_getIvar</c>), so no private selector is called.
    /// </summary>
    private static string AppliedMenu(Avalonia.Controls.TopLevel w)
    {
        try
        {
            var h = w.TryGetPlatformHandle();
            if (h is null || h.Handle == IntPtr.Zero) return "nohandle";
            IntPtr ivar = IvarDef(ObjClass(h.Handle), "_menu");
            if (ivar == IntPtr.Zero) return "noivar";
            IntPtr menu = IvarGet(h.Handle, ivar);
            return menu == IntPtr.Zero ? "NIL" : SendLong(menu, Sel("numberOfItems")).ToString();
        }
        catch { return "err"; }
    }

    /// <summary>
    /// Which NSWindow AppKit currently calls key (and main), as "&lt;class&gt;:&lt;title&gt;". The bar is
    /// installed by <c>-[AvnWindow becomeKeyWindow]</c> and by nothing else, so "which window is key"
    /// and "what is on the bar" have to be read together: a bare bar over a key window that HAS a
    /// menu is a different fault from a bare bar with no key window at all.
    /// </summary>
    private static string WindowDesc(IntPtr window)
    {
        if (window == IntPtr.Zero) return "none";
        IntPtr cls = ObjClass(window);
        string name = cls == IntPtr.Zero ? "?" : Marshal.PtrToStringUTF8(ClassName(cls)) ?? "?";
        return name + ":" + (NsString(Send(window, Sel("title"))) ?? "?");
    }

    private static string? NsString(IntPtr ns)
    {
        if (ns == IntPtr.Zero) return null;
        IntPtr utf8 = Send(ns, Sel("UTF8String"));
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }

    /// <summary>The real menu bar: one line, e.g. "mainMenu[3]: circuitRF | File | Edit".</summary>
    internal static string NativeMenuBar()
    {
        try
        {
            IntPtr app = Send(GetClass("NSApplication"), Sel("sharedApplication"));
            if (app == IntPtr.Zero) return "mainMenu: <no NSApplication>";

            long policy = SendLong(app, Sel("activationPolicy"));
            bool appActive = SendLong(app, Sel("isActive")) != 0;
            string key  = WindowDesc(Send(app, Sel("keyWindow")));
            string main = WindowDesc(Send(app, Sel("mainWindow")));
            IntPtr menu = Send(app, Sel("mainMenu"));
            if (menu == IntPtr.Zero)
                return $"mainMenu: <null>  policy={policy} appActive={appActive} key={key} main={main}";

            long n = SendLong(menu, Sel("numberOfItems"));
            var sb = new StringBuilder();
            sb.Append($"mainMenu[{n}]@{menu:x} policy={policy} appActive={appActive} key={key} main={main}: ");
            for (long i = 0; i < n; i++)
            {
                IntPtr item = SendIdx(menu, Sel("itemAtIndex:"), i);
                sb.Append(NsString(Send(item, Sel("title"))) ?? "?");

                // A nine-item menu can still DRAW as one. Hidden and disabled are the two ways that
                // happens without anything going missing from the menu, and they are annotated rather
                // than always printed so an ordinary healthy line stays one glance wide.
                if (SendLong(item, Sel("isHidden")) != 0) sb.Append("(hidden)");
                if (SendLong(item, Sel("isEnabled")) == 0) sb.Append("(disabled)");
                if (Send(item, Sel("submenu")) == IntPtr.Zero) sb.Append("(nosub)");

                if (i < n - 1) sb.Append(" | ");
            }
            return sb.ToString();
        }
        catch (Exception e) { return "mainMenu: probe failed: " + e.Message; }
    }

    /// <summary>The managed side: every window, whether it is key, and what menu it carries.</summary>
    internal static string ManagedMenus()
    {
        var sb = new StringBuilder();
        try
        {
            var app = Avalonia.Application.Current;
            var appMenu = app is null ? null : Avalonia.Controls.NativeMenu.GetMenu(app);
            sb.Append($"appScope={Id(appMenu)}");

            if (app?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d)
                foreach (var w in d.Windows)
                {
                    var m = Avalonia.Controls.NativeMenu.GetMenu(w);
                    sb.Append($"  [{w.GetType().Name} winKey={w.IsActive} vis={w.IsVisible} " +
                              $"menu={Id(m)} applied={AppliedMenu(w)} " +
                              $"exported={Avalonia.Controls.NativeMenu.GetIsNativeMenuExported(w)}]");
                }
        }
        catch (Exception e) { sb.Append(" managed probe failed: " + e.Message); }
        return sb.ToString();

        static string Id(Avalonia.Controls.NativeMenu? m)
            => m is null ? "null" : $"#{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(m)}({m.Items.Count})";
    }

    /// <summary>
    /// Starts the probe if CRF_MENU_DIAG names a log file. Call once, after the first window.
    ///
    /// <para><b>Cannot affect a normal or packaged build.</b> It returns before touching anything
    /// unless the environment variable is set AND the platform is macOS, so the <c>libobjc</c>
    /// imports below are never resolved elsewhere — P/Invoke binding is lazy, and this project
    /// publishes with neither trimming nor AOT, so an uncalled import is not even inspected. The
    /// whole body is wrapped as well: a diagnostic that can break the application it is diagnosing
    /// is worse than no diagnostic.</para>
    /// </summary>
    internal static void StartIfRequested()
    {
        if (!OperatingSystem.IsMacOS()) return;
        string? log = Environment.GetEnvironmentVariable("CRF_MENU_DIAG");
        if (string.IsNullOrWhiteSpace(log)) return;
        try { Start(log); } catch { /* never a reason for a launch to fail */ }
    }

    private static void Start(string log)
    {

        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            Append(log, "DISPATCHER-EX handled=" + e.Handled + ": " + e.Exception);

        // Runs for the whole session and logs only when the state CHANGES (plus a heartbeat), so the
        // probe can be left on through an ordinary working day without growing without bound. A
        // fixed number of samples would have been useless here: the failure is at launch but the
        // report of it comes minutes later.
        string last = "";
        int lines = 0, ticks = 0;
        // 100 ms, not 500. The launch fault this was built for is a state that PERSISTS, so a coarse
        // sample found it; the faults since are transitions of a few hundred milliseconds, and a
        // sample of the same order as the thing being measured cannot say which of two events came
        // first. It costs a handful of ObjC calls per tick and still writes only when something
        // changes.
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            ticks++;
            string now = $"{NativeMenuBar()}   {ManagedMenus()}";
            bool heartbeat = ticks % 600 == 0;                 // roughly once a minute
            if (now == last && !heartbeat) return;
            last = now;
            if (++lines > 2000) { timer.Stop(); Append(log, "probe stopped: line cap reached"); return; }
            Append(log, $"[{DateTime.Now:HH:mm:ss.fff}] {now}");
        };
        timer.Start();
        Append(log, $"[{DateTime.Now:HH:mm:ss.fff}] probe started (pid {Environment.ProcessId})");
    }

    /// <summary>
    /// Writes one line into the <c>CRF_MENU_DIAG</c> log if one was asked for, so code OUTSIDE this
    /// class can say what it did next to the samples that show the effect. Silent and free otherwise.
    /// </summary>
    internal static void Note(string line)
    {
        string? log = Environment.GetEnvironmentVariable("CRF_MENU_DIAG");
        if (string.IsNullOrWhiteSpace(log)) return;
        Append(log, $"[{DateTime.Now:HH:mm:ss.fff}] {line}");
    }

    private static void Append(string path, string line)
    {
        try { File.AppendAllText(path, line + Environment.NewLine); } catch { }
    }
}
