using System;
using System.Drawing;
using System.Windows.Forms;

namespace DshLauncher.Forms;

/// <summary>
/// Secure API-key input. The field masks every character, and replacing an
/// existing key requires an explicit confirmation to prevent accidental
/// overwrites.
/// </summary>
public partial class ApiKeyDialog : Form
{
    private readonly TextBox _keyBox = new() { PasswordChar = '*', Width = 280 };
    private readonly bool _hasExisting;

    /// <summary>The key the user typed, or null when cancelled.</summary>
    public string? EnteredKey { get; private set; }

    public ApiKeyDialog(bool hasExisting)
    {
        _hasExisting = hasExisting;
        Text = hasExisting ? "Change API Key" : "Save API Key";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);
        ClientSize = new Size(380, 170);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 3,
        };

        var hint = new Label
        {
            Text = hasExisting
                ? "Enter the new key. The existing key will be replaced."
                : "Enter your DeepSeek API key (sk-...).",
            Dock = DockStyle.Fill,
        };

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 90, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var note = new Label
        {
            Text = "Stored locally in the harness .env file, masked in this app.",
            ForeColor = Color.Gray,
            AutoSize = true,
        };

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(note, 0, 0);
        bottom.Controls.Add(buttons, 1, 0);

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        layout.Controls.Add(hint, 0, 0);
        layout.Controls.Add(_keyBox, 0, 1);
        layout.Controls.Add(bottom, 0, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
        _keyBox.Focus();

        ok.Click += (_, _) => EnteredKey = _keyBox.Text.Trim();
    }

    /// <summary>
    /// Guards the "Save" path: replacing an existing key needs an explicit Yes.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && _hasExisting)
        {
            var answer = MessageBox.Show(
                this,
                "Do you really want to change the API key?",
                "Confirm change",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                DialogResult = DialogResult.None;
                return;
            }
        }

        base.OnFormClosing(e);
    }
}
