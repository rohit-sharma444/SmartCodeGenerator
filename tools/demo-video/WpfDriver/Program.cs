// Drives a generated WPF app for the demo video's "Run" section, over a one-command-per-line
// JSON protocol on stdin/stdout. record.mjs owns every timing/caption/scene decision, exactly as
// it does for the browser scenes; this process is a dumb executor with no pacing logic of its
// own, so the two recordings stay driven from one place.
//
// Commands (one JSON object per line on stdin), each answered with one JSON line on stdout:
//   {"cmd":"attach","pid":1234}            -> {"ok":true,"title":"...","x":0,"y":0,"width":0,"height":0}
//   {"cmd":"wait-for-window"}               -> same shape as attach's reply
//   {"cmd":"click-button","name":"Sign in"}                        -> {"ok":true}
//   {"cmd":"click","name":"Customer"}                              -> {"ok":true,"via":"mouse"}
//   add "direct":true to open it through UI Automation ("via":"invoke" or "select") rather than the mouse
//   {"cmd":"type","automationId":"UserNameBox","value":"admin"}    -> {"ok":true}
//   {"cmd":"type","name":"User name","value":"admin"}              -> {"ok":true}  (WinForms: no ids)
//   add "direct":true to set it through UI Automation rather than the keyboard
//   {"cmd":"dump"}                                                 -> {"ok":true,"elements":[...]}
//   {"cmd":"find-grid"}                                            -> {"ok":true,"columns":[...],"rowCount":N,"totalRows":N}
//   {"cmd":"type-field","label":"OrderNumber","value":"SO-2001"}   -> {"ok":true}
//   {"cmd":"read-status"}                                          -> {"ok":true,"text":"..."}
//   {"cmd":"screenshot","path":"C:\\...\\frame.png"}               -> {"ok":true}
//   {"cmd":"quit"}                                                 -> {"ok":true}
//
// Every response carries "ok": false and an "error" string instead of throwing across the pipe -
// a malformed or timed-out step should tell record.mjs why, not kill the driver mid-recording.

using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

// Per-monitor DPI awareness: without it, Windows hands this process SCALED coordinates while
// gdigrab captures the desktop's real physical pixels, and a window's bounding rect reported here
// would not be where record.mjs's ffmpeg call actually finds it on screen.
SetProcessDpiAwarenessContext(new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

using var automation = new UIA3Automation();
Application? app = null;
Window? window = null;

string? line;
while ((line = Console.ReadLine()) is not null)
{
    if (string.IsNullOrWhiteSpace(line))
        continue;

    JsonObject reply;

    try
    {
        var request = JsonNode.Parse(line)!.AsObject();
        reply = Handle(request);
    }
    catch (Exception ex)
    {
        reply = new JsonObject { ["ok"] = false, ["error"] = ex.Message };
    }

    Console.WriteLine(reply.ToJsonString());
    Console.Out.Flush();

    if (reply["quit"]?.GetValue<bool>() == true)
        break;
}

JsonObject Handle(JsonObject request)
{
    var cmd = request["cmd"]?.GetValue<string>()
        ?? throw new InvalidOperationException("missing \"cmd\"");

    return cmd switch
    {
        "attach" => Attach(request["pid"]!.GetValue<int>()),
        "wait-for-window" => WaitForWindow(),
        "click-button" => ClickButton(request["name"]!.GetValue<string>()),
        "click" => ClickByName(request["name"]!.GetValue<string>(), request["direct"]?.GetValue<bool>() == true),
        "type" => Type(request["automationId"]?.GetValue<string>(), request["name"]?.GetValue<string>(),
            request["value"]!.GetValue<string>(), request["direct"]?.GetValue<bool>() == true),
        "find-grid" => FindGrid(),
        "type-field" => TypeField(request["label"]!.GetValue<string>(), request["value"]!.GetValue<string>()),
        "read-status" => ReadStatus(),
        "dump" => Dump(),
        "screenshot" => Screenshot(request["path"]!.GetValue<string>()),
        "quit" => new JsonObject { ["ok"] = true, ["quit"] = true },
        _ => new JsonObject { ["ok"] = false, ["error"] = $"unknown cmd '{cmd}'" },
    };
}

/// <summary>
/// Attaches to the process the CLI already launched, and waits for ITS top-level window - the
/// login window when a login is on, the shell otherwise. Not GetMainWindow's default timeout: a
/// WPF app's first window can take a couple of seconds to paint after the process exists.
/// </summary>
JsonObject Attach(int pid)
{
    app = Application.Attach(pid);
    window = app.GetMainWindow(automation, TimeSpan.FromSeconds(20))
        ?? throw new InvalidOperationException($"process {pid} never showed a window");

    Foreground(window);

    return WindowInfo(window);
}

/// <summary>
/// The window's identity for the recorder: its title, kept for logging, and its screen rectangle -
/// what record.mjs actually needs, since capturing gdigrab's own "by title" window lookup failed
/// against this generator's WPF apps (error 8, most likely their DirectX-rendered surface, which
/// GDI's BitBlt does not always get on with) and a fixed screen region sidesteps that lookup
/// entirely.
/// </summary>
JsonObject WindowInfo(Window w)
{
    var rect = StableRectangle(w);

    return new JsonObject
    {
        ["ok"] = true,
        ["title"] = w.Title,
        ["x"] = rect.X,
        ["y"] = rect.Y,
        ["width"] = rect.Width,
        ["height"] = rect.Height,
    };
}

/// <summary>
/// A window just launched with <c>WindowStartupLocation="CenterScreen"</c> gets that final
/// position from a layout pass that runs AFTER its handle already exists - reading
/// BoundingRectangle in the instant right after GetMainWindow returns caught it mid-move once,
/// recording a region the login window had already moved on from by its first real frame. Waits
/// for two consecutive reads to agree instead of trusting the first one.
/// </summary>
System.Drawing.Rectangle StableRectangle(Window w)
{
    var previous = w.BoundingRectangle;

    for (var i = 0; i < 20; i++)
    {
        Thread.Sleep(100);

        var current = w.BoundingRectangle;

        if (current == previous && current.Width > 0 && current.Height > 0)
            return current;

        previous = current;
    }

    return previous;
}

/// <summary>
/// Restored and frontmost, never minimized: gdigrab captures whatever rectangle the window
/// currently occupies, taskbar-icon-sized when minimized, and a window UI Automation can click
/// into perfectly well is not necessarily the one on top of the screen a viewer would see.
/// </summary>
void Foreground(Window w)
{
    if (w.Patterns.Window.PatternOrDefault is { } windowPattern
        && windowPattern.WindowVisualState.Value == WindowVisualState.Minimized)
        windowPattern.SetWindowVisualState(WindowVisualState.Normal);

    // SetForeground, not Focus alone: Focus gives the window UI Automation focus but leaves
    // another window on top of it, where a real mouse click into a WinForms box then lands.
    w.SetForeground();
    w.Focus();
}

/// <summary>
/// The login window closes (ShowDialog returning) and the app's own shell opens as a SEPARATE
/// window from the same process - not a repaint of the one we already have a reference to, and
/// not reliably distinguished by title either: both windows carry the app's own display name.
/// Compared by native window handle instead, which is unique per window regardless of what text
/// either one shows. Polls the process's own top-level windows rather than re-calling
/// GetMainWindow, which can still return the closing window in the instant between the two
/// ShowDialog calls.
/// </summary>
JsonObject WaitForWindow()
{
    if (app is null || window is null)
        return new JsonObject { ["ok"] = false, ["error"] = "not attached" };

    var previousHandle = window.Properties.NativeWindowHandle.Value;
    var deadline = DateTime.UtcNow.AddSeconds(20);

    while (DateTime.UtcNow < deadline)
    {
        var candidate = app.GetAllTopLevelWindows(automation)
            .FirstOrDefault(w => w.Properties.NativeWindowHandle.Value != previousHandle);

        if (candidate is not null)
        {
            window = candidate;
            Foreground(window);
            return WindowInfo(window);
        }

        Thread.Sleep(200);
    }

    return new JsonObject { ["ok"] = false, ["error"] = "no new window appeared" };
}

JsonObject ClickButton(string name)
{
    if (window is null)
        return NotAttached();

    var button = window.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName(name)))
        ?.AsButton();

    if (button is null)
        return new JsonObject { ["ok"] = false, ["error"] = $"no button named '{name}'" };

    button.Invoke();

    return new JsonObject { ["ok"] = true };
}

/// <summary>
/// A raw mouse click on whatever has this name, regardless of control type - the sidebar's entity
/// links and the dashboard's own tiles are plain clickable text, not always a real Button, so
/// InvokePattern is not something every one of them supports.
/// </summary>
/// <remarks>
/// "direct" opens it through UI Automation instead, needing no mouse: Invoke where the element
/// has it (a WPF sidebar entry is a Button), else Select (a WinForms sidebar entry is a TreeView
/// node, and the generated shell opens its screen on AfterSelect). A raw click lands on whatever
/// window is on top at that point, and Windows will not let this process bring the app forward -
/// a browser someone was watching took app-check's clicks in a real run, and the list it then
/// looked for was never opened. app-check asks for this; the demo video clicks, because it is
/// meant to look clicked. When neither pattern is there it still clicks, but never into a window
/// that is not the app's.
/// </remarks>
JsonObject ClickByName(string name, bool direct)
{
    if (window is null)
        return NotAttached();

    var element = window.FindFirstDescendant(cf => cf.ByName(name));

    if (element is null)
        return new JsonObject { ["ok"] = false, ["error"] = $"no element named '{name}'" };

    if (direct)
    {
        if (element.Patterns.Invoke.PatternOrDefault is { } invoke)
        {
            invoke.Invoke();
            return new JsonObject { ["ok"] = true, ["via"] = "invoke" };
        }

        if (element.Patterns.SelectionItem.PatternOrDefault is { } selectionItem)
        {
            selectionItem.Select();
            return new JsonObject { ["ok"] = true, ["via"] = "select" };
        }

        if (CoveredBy(element) is { } other)
            return new JsonObject { ["ok"] = false, ["error"] = $"'{name}' is covered by another window: '{other}'" };
    }

    element.Click();

    return new JsonObject { ["ok"] = true, ["via"] = "mouse" };
}

/// <summary>
/// The title of the window on top at <paramref name="element"/>'s clickable point when it is not
/// the app's own window, or null when the app's window is the one a click there would reach.
/// </summary>
string? CoveredBy(AutomationElement element)
{
    var bounds = element.BoundingRectangle;
    var point = element.TryGetClickablePoint(out var clickable)
        ? clickable
        : new System.Drawing.Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

    var top = GetAncestor(WindowFromPoint(new NativePoint { X = point.X, Y = point.Y }), GaRootOwner);
    var own = window!.Properties.NativeWindowHandle.Value;

    if (top == own)
        return null;

    var title = new System.Text.StringBuilder(256);
    GetWindowText(top, title, title.Capacity);
    return title.Length > 0 ? title.ToString() : $"window 0x{top:X}";
}

/// <summary>
/// Types by simulated keystrokes, not the Value pattern: a PasswordBox's automation peer does not
/// expose its text for reading, and typing is the one path that works for both it and an
/// ordinary TextBox alike.
/// </summary>
/// <remarks>
/// By AutomationId when one is given (WPF's x:Name), otherwise by the edit box's accessible name:
/// a generated WinForms control has no Name set, so it carries no AutomationId at all, and UI
/// Automation names its text boxes after the label beside them instead.
/// </remarks>
JsonObject Type(string? automationId, string? name, string value, bool direct)
{
    if (window is null)
        return NotAttached();

    var element = automationId is not null
        ? window.FindFirstDescendant(cf => cf.ByAutomationId(automationId))
        : window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit).And(cf.ByName(name ?? "")));

    // "direct": set the value through UI Automation, needing no keyboard at all. Windows will not
    // let this process take the foreground, so when another window holds the keyboard - Notepad
    // opened over a WinForms login, in testing - simulated keys land in THAT window instead.
    // app-check asks for this; the demo video types, because it is meant to look typed. Falls
    // back to typing for a box whose Value pattern refuses the write.
    if (direct && element?.Patterns.Value.PatternOrDefault is { } valuePattern)
    {
        try
        {
            valuePattern.SetValue(value);
            return new JsonObject { ["ok"] = true, ["direct"] = true };
        }
        catch (Exception)
        {
            // A password box that will not be written this way: type into it as below.
        }
    }

    if (element is null)
        return new JsonObject { ["ok"] = false, ["error"] = $"no edit box '{automationId ?? name}'" };

    // Frontmost first: simulated keystrokes go to whichever window has the keyboard, and a
    // terminal someone was typing in when the app opened took an app-check sign-in's "admin"
    // once - the login then refused a password it never received.
    Foreground(window);

    // A WinForms TextBox ignores UI Automation's Focus() for keyboard purposes - the keystrokes
    // that follow go nowhere - so the by-name path clicks into the box, as a person would.
    if (automationId is null)
        element.Click();
    else
        element.Focus();

    // Replace, not append: End then Shift+Home selects whatever the box holds, so typing into it
    // a second time - a retried sign-in - does not leave "adminadmin" behind. Not Ctrl+A, which a
    // single-line WinForms TextBox has not always honoured.
    Keyboard.Press(VirtualKeyShort.END);
    Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.HOME);
    Keyboard.Type(value);

    return new JsonObject { ["ok"] = true };
}

JsonObject FindGrid()
{
    var grid = FindDataGrid();

    if (grid is null)
        return new JsonObject { ["ok"] = false, ["error"] = "no DataGrid on the current window" };

    var columns = new JsonArray();
    foreach (var header in grid.Header?.Columns ?? [])
        columns.Add(header.Text);

    // FlaUI's Rows reads the WPF DataGrid's row items. A WinForms DataGridView answers none there,
    // with five rows plainly on screen, so its count comes from the grid pattern, and failing
    // that from its own accessible rows, which it names "Row 0", "Row 1", ...
    var rowCount = grid.Rows.Length;

    if (rowCount == 0)
        rowCount = grid.Patterns.Grid.PatternOrDefault?.RowCount.ValueOrDefault ?? 0;

    if (rowCount == 0)
        rowCount = grid.FindAllChildren()
            .Count(c => (c.Properties.Name.ValueOrDefault ?? "").StartsWith("Row ", StringComparison.Ordinal));

    // Every item the grid holds, scrolled into view or not: a WPF DataGrid realizes only the rows
    // on screen, so Rows cannot tell whether a save added one below the fold. app-check compares
    // this before New and after Save.
    var totalRows = grid.Patterns.Grid.PatternOrDefault?.RowCount.ValueOrDefault ?? 0;

    return new JsonObject
    {
        ["ok"] = true,
        ["columns"] = columns,
        ["rowCount"] = rowCount,
        ["totalRows"] = totalRows,
    };
}

/// <summary>
/// Types into the field labelled <paramref name="label"/> on the entry form - the header strip's
/// key fields (OrderNumber, OrderDate, Status) or one of the titled panels below (Customer, Net,
/// CustomerCity, ...).
/// </summary>
/// <remarks>
/// None of these controls carry an AutomationId: <c>WpfEntryControls.Editor</c> builds them by
/// reflection at run time with nothing but a binding. What is stable is the ORDER things are
/// added in - the label immediately followed by its own editor, one pair per field - so the
/// editor is found as the label's next sibling in the control-view tree rather than by name.
/// </remarks>
JsonObject TypeField(string label, string value)
{
    if (window is null)
        return NotAttached();

    var labelElement = window.FindFirstDescendant(cf =>
        cf.ByControlType(ControlType.Text).And(cf.ByName(label)));

    if (labelElement is null)
        return new JsonObject { ["ok"] = false, ["error"] = $"no field labelled '{label}'" };

    var editor = automation.TreeWalkerFactory.GetControlViewWalker().GetNextSibling(labelElement);

    if (editor is null)
        return new JsonObject { ["ok"] = false, ["error"] = $"'{label}' has no sibling control" };

    // A DatePicker's own ControlType is Custom; the part actually holding typable text is a
    // template part named PART_TextBox, which is WPF's own name for it and not something this
    // generator chose - stable across every app this drives. Anything else (a plain TextBox) is
    // typed into directly.
    var target = editor.ControlType == ControlType.Edit
        ? editor
        : editor.FindFirstDescendant(cf => cf.ByAutomationId("PART_TextBox")) ?? editor;

    target.Focus();

    // Select-all first: a DatePicker defaults to today's date and a re-visited TextBox already
    // holds whatever it was bound to, and typing without clearing either would insert into it
    // rather than replace it.
    Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
    Keyboard.Type(value);
    Keyboard.Press(VirtualKeyShort.TAB);

    return new JsonObject { ["ok"] = true };
}

/// <summary>
/// The one status line every layout binds to <c>Status</c> - never named, so found by what it
/// says rather than by structure: polls every Text element until one starts with a phrase the
/// generator actually produces (see ViewModelTemplates.cs), so a still-empty label from a moment
/// ago is never mistaken for the answer.
/// </summary>
JsonObject ReadStatus()
{
    if (window is null)
        return NotAttached();

    string[] prefixes = ["Cannot save", "Added", "Removed", "Saved"];

    var deadline = DateTime.UtcNow.AddSeconds(10);

    while (DateTime.UtcNow < deadline)
    {
        var texts = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));

        foreach (var text in texts)
        {
            var value = text.Name;

            if (!string.IsNullOrEmpty(value) && prefixes.Any(p => value.StartsWith(p, StringComparison.Ordinal)))
                return new JsonObject { ["ok"] = true, ["text"] = value };
        }

        Thread.Sleep(200);
    }

    return new JsonObject { ["ok"] = false, ["error"] = "no status text appeared" };
}

/// <summary>
/// Every named-control-bearing element's control type, name, AutomationId and current value.
/// record.mjs does not call it; tools/app-check does, to ask "is X on screen" of an app whose
/// entity names it only learns at run time, and it is the first thing to look at when a find-by-
/// name command here says an element does not exist.
/// </summary>
JsonObject Dump()
{
    if (window is null)
        return NotAttached();

    var kinds = new[]
    {
        ControlType.Text, ControlType.Edit, ControlType.ComboBox, ControlType.Calendar,
        ControlType.Button, ControlType.CheckBox, ControlType.DataGrid, ControlType.Custom,
        ControlType.Table, ControlType.Tab, ControlType.TabItem,
        ControlType.TreeItem, ControlType.ListItem, ControlType.Hyperlink, ControlType.MenuItem,
    };

    var elements = new JsonArray();

    foreach (var element in window.FindAllDescendants().Where(e => kinds.Contains(e.ControlType)))
    {
        elements.Add(new JsonObject
        {
            ["type"] = element.ControlType.ToString(),
            ["name"] = element.Name,
            ["automationId"] = element.Properties.AutomationId.ValueOrDefault,
            ["value"] = element.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault,
        });
    }

    return new JsonObject { ["ok"] = true, ["elements"] = elements };
}

/// <summary>
/// The window's own contents, drawn by the window into a bitmap of its own - not the screen
/// under its rectangle, which is whatever is on top there: a real run's screenshots of two apps
/// showed a browser video, and nothing of the app that had failed.
/// </summary>
/// <remarks>
/// PW_RENDERFULLCONTENT is what gets a WPF window's DirectX-drawn surface; without it PrintWindow
/// returns the frame and a black client area. Falls back to the screen when PrintWindow refuses.
/// </remarks>
JsonObject Screenshot(string path)
{
    if (window is null)
        return NotAttached();

    var rect = window.BoundingRectangle;
    var handle = window.Properties.NativeWindowHandle.Value;

    using (var bitmap = new System.Drawing.Bitmap(rect.Width, rect.Height))
    {
        bool drawn;

        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            var dc = graphics.GetHdc();
            try { drawn = PrintWindow(handle, dc, PwRenderFullContent); }
            finally { graphics.ReleaseHdc(dc); }
        }

        if (drawn)
        {
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return new JsonObject { ["ok"] = true, ["via"] = "window" };
        }
    }

    FlaUI.Core.Capturing.Capture.Element(window).ToFile(path);

    return new JsonObject { ["ok"] = true, ["via"] = "screen" };
}

// DataGrid for WPF; Table for WinForms, whose DataGridView UI Automation reports as a table.
Grid? FindDataGrid()
    => window?.FindFirstDescendant(cf =>
        cf.ByControlType(ControlType.DataGrid).Or(cf.ByControlType(ControlType.Table)))?.AsGrid();

JsonObject NotAttached() => new() { ["ok"] = false, ["error"] = "not attached" };

[DllImport("user32.dll")]
static extern bool SetProcessDpiAwarenessContext(IntPtr value);

[DllImport("user32.dll")]
static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

[DllImport("user32.dll")]
static extern IntPtr WindowFromPoint(NativePoint point);

[DllImport("user32.dll")]
static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);

partial class Program
{
    const uint PwRenderFullContent = 2;
    const uint GaRootOwner = 3;
}

[StructLayout(LayoutKind.Sequential)]
struct NativePoint
{
    public int X;
    public int Y;
}
