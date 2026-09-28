// Drives a generated WPF app for the demo video's "Run" section, over a one-command-per-line
// JSON protocol on stdin/stdout. record.mjs owns every timing/caption/scene decision, exactly as
// it does for the browser scenes; this process is a dumb executor with no pacing logic of its
// own, so the two recordings stay driven from one place.
//
// Commands (one JSON object per line on stdin), each answered with one JSON line on stdout:
//   {"cmd":"attach","pid":1234}            -> {"ok":true,"title":"...","x":0,"y":0,"width":0,"height":0}
//   {"cmd":"wait-for-window"}               -> same shape as attach's reply
//   {"cmd":"click-button","name":"Sign in"}                        -> {"ok":true}
//   {"cmd":"type","automationId":"UserNameBox","value":"admin"}    -> {"ok":true}
//   {"cmd":"find-grid"}                                            -> {"ok":true,"columns":[...],"rowCount":N}
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
        "click" => ClickByName(request["name"]!.GetValue<string>()),
        "type" => Type(request["automationId"]!.GetValue<string>(), request["value"]!.GetValue<string>()),
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
JsonObject ClickByName(string name)
{
    if (window is null)
        return NotAttached();

    var element = window.FindFirstDescendant(cf => cf.ByName(name));

    if (element is null)
        return new JsonObject { ["ok"] = false, ["error"] = $"no element named '{name}'" };

    element.Click();

    return new JsonObject { ["ok"] = true };
}

/// <summary>
/// Types by simulated keystrokes, not the Value pattern: a PasswordBox's automation peer does not
/// expose its text for reading, and typing is the one path that works for both it and an
/// ordinary TextBox alike.
/// </summary>
JsonObject Type(string automationId, string value)
{
    if (window is null)
        return NotAttached();

    var element = window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));

    if (element is null)
        return new JsonObject { ["ok"] = false, ["error"] = $"no control with AutomationId '{automationId}'" };

    element.Focus();
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

    return new JsonObject
    {
        ["ok"] = true,
        ["columns"] = columns,
        ["rowCount"] = grid.Rows.Length,
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
/// A debugging aid, not something record.mjs calls: every Text/Edit/ComboBox/DatePicker/CheckBox
/// element's control type and name, so the driver's field-finding logic can be built against what
/// is actually in the tree rather than guessed from the generator's source.
/// </summary>
JsonObject Dump()
{
    if (window is null)
        return NotAttached();

    var kinds = new[]
    {
        ControlType.Text, ControlType.Edit, ControlType.ComboBox, ControlType.Calendar,
        ControlType.Button, ControlType.CheckBox, ControlType.DataGrid, ControlType.Custom,
    };

    var elements = new JsonArray();

    foreach (var element in window.FindAllDescendants(cf => cf.ByControlType(kinds[0]).Or(
                 cf.ByControlType(kinds[1])).Or(cf.ByControlType(kinds[2])).Or(
                 cf.ByControlType(kinds[3])).Or(cf.ByControlType(kinds[4])).Or(
                 cf.ByControlType(kinds[5])).Or(cf.ByControlType(kinds[6])).Or(
                 cf.ByControlType(kinds[7]))))
    {
        elements.Add(new JsonObject
        {
            ["type"] = element.ControlType.ToString(),
            ["name"] = element.Name,
            ["automationId"] = element.Properties.AutomationId.ValueOrDefault,
        });
    }

    return new JsonObject { ["ok"] = true, ["elements"] = elements };
}

JsonObject Screenshot(string path)
{
    if (window is null)
        return NotAttached();

    var image = FlaUI.Core.Capturing.Capture.Element(window);
    image.ToFile(path);

    return new JsonObject { ["ok"] = true };
}

Grid? FindDataGrid()
    => window?.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataGrid))?.AsGrid();

JsonObject NotAttached() => new() { ["ok"] = false, ["error"] = "not attached" };

[DllImport("user32.dll")]
static extern bool SetProcessDpiAwarenessContext(IntPtr value);
