using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DshLauncher.Services;

namespace DshLauncher.Forms;

/// <summary>
/// Asks which browser should open the DS Harness interface.
/// Used both on first run (with "remember my choice") and from settings.
/// </summary>
public partial class FirstRunWizard : Form
{
    private readonly RadioButton _chrome = new() { Text = "Chrome", AutoSize = true };
    private readonly RadioButton _edge = new() { Text = "Edge", AutoSize = true };
    private readonly CheckBox _remember = new() { Text = "Remember my choice", AutoSize = true };

    /// <summary>Browser chosen by the user (set when the dialog closes with OK).</summary>
    public string BrowserChoice { get; private set; } = "Chrome";

    /// <summary>Whether the user asked to remember the choice.</summary>
    public bool Remember { get; private set; }

    public FirstRunWizard(string currentChoice, bool showRemember)
    {
        Text = showRemember ? "Welcome to DSH Launcher" : "Browser Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);
        ClientSize = new Size(380, showRemember ? 230 : 180);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 4,
        };

        var heading = new Label
        {
            Text = "Which browser should open the DS Harness interface?",
            Dock = DockStyle.Fill,
            AutoSize = false,
        };

        var installed = BrowserService.InstalledNames();
        var hasChrome = installed.Contains("Chrome", StringComparer.OrdinalIgnoreCase);
        var hasEdge = installed.Contains("Edge", StringComparer.OrdinalIgnoreCase);

        _chrome.Checked = string.Equals(currentChoice, "Chrome", StringComparison.OrdinalIgnoreCase);
        _edge.Checked = !_chrome.Checked;

        // Grey out browsers that are not installed.
        _chrome.Enabled = hasChrome;
        _edge.Enabled = hasEdge;
        if (!hasChrome) _chrome.Text += " (not installed)";
        if (!hasEdge) _edge.Text += " (not installed)";

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        options.Controls.Add(_chrome);
        options.Controls.Add(_edge);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(options, 0, 1);
        layout.Controls.Add(_remember, 0, 2);
        layout.Controls.Add(buttons, 0, 3);

        _remember.Visible = showRemember;
        _remember.Checked = true;

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;

        ok.Click += (_, _) => Accept();
    }

    private void Accept()
    {
        BrowserChoice = _chrome.Checked ? "Chrome" : "Edge";
        Remember = _remember.Checked;
    }
}
