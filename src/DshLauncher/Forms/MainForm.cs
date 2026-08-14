using System;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using DshLauncher.Services;

namespace DshLauncher.Forms;

/// <summary>
/// Main window: live engine log, update status, API-key management,
/// and the three primary actions (Check Update / Restart Backend / Open DS Harness).
///
/// Layout is deliberately simple and deterministic: fixed-height docked strips
/// (header / actions / settings / footer) around a fill-size log panel, and a
/// locked window size, so it renders correctly regardless of DPI or screen size.
/// </summary>
public partial class MainForm : Form
{
    private readonly ConfigService _config = new();
    private readonly BackendService _backend;
    private readonly UpdateService _updater = new();
    private readonly PrerequisitesService _prereq = new();

    private TextBox _log = null!;
    private Label _updatePill = null!;
    private Label _enginePill = null!;
    private TextBox _apiKeyBox = null!;
    private Button _apiKeyButton = null!;
    private Label _browserLabel = null!;

    private string? _sessionBrowser; // chosen this session but not remembered
    private bool _closing;

    public MainForm()
    {
        _config.Load();
        var cfg = _config.Config;
        _backend = new BackendService(cfg.HarnessPath, cfg.HarnessUrl);

        Text = "DSH Launcher";
        // Fixed, non-resizable window so the layout always renders as designed.
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(900, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        BuildLayout();
        WireBackendEvents();

        RefreshApiKeyUi();
        RefreshBrowserLabel();
    }

    // ------------------------------------------------------------------
    // Layout (docked strips, fill-size log)
    // ------------------------------------------------------------------

    private void BuildLayout()
    {
        // 1) Header strip: title left, update pill right.
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(2) ?? "?";
        var title = new Label
        {
            Text = $"DSH Launcher  v{version}",
            Font = new Font(Font ?? SystemFonts.DefaultFont, FontStyle.Bold),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };
        _updatePill = MakePill("Update: —", Color.SlateGray);
        _updatePill.Anchor = AnchorStyles.Right;

        var headerInner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        headerInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerInner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        headerInner.Controls.Add(title, 0, 0);
        headerInner.Controls.Add(_updatePill, 1, 0);

        var header = new Panel { Dock = DockStyle.Top, Height = 30 };
        header.Controls.Add(headerInner);

        // 2) Action buttons strip.
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 3),
        };
        actions.Controls.Add(MakeButton("Check Update", BtnCheckUpdate_Click));
        actions.Controls.Add(MakeButton("Restart Backend", (_, _) => _backend.Restart()));
        actions.Controls.Add(MakeButton("Open DS Harness", (_, _) => OpenHarness()));

        var actionStrip = new Panel { Dock = DockStyle.Top, Height = 34 };
        actionStrip.Controls.Add(actions);

        // 3) Settings strip: API key row + browser picker, right-aligned.
        _apiKeyBox = new TextBox
        {
            PasswordChar = '*',
            Width = 220,
            ReadOnly = true,
            Anchor = AnchorStyles.Left,
        };
        _apiKeyButton = MakeButton("", BtnApiKey_Click);
        _browserLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        var apiKeyLabel = new Label { Text = "API Key:", AutoSize = true, Anchor = AnchorStyles.Left };
        var browserHint = new Label { Text = "Browser:", AutoSize = true, Anchor = AnchorStyles.Left };
        var changeBrowser = MakeButton("Change", (_, _) => ChangeBrowser());

        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7 };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        settings.Controls.Add(apiKeyLabel, 0, 0);
        settings.Controls.Add(_apiKeyBox, 1, 0);
        settings.Controls.Add(_apiKeyButton, 2, 0);
        settings.Controls.Add(new Label(), 3, 0); // spacer
        settings.Controls.Add(browserHint, 4, 0);
        settings.Controls.Add(_browserLabel, 5, 0);
        settings.Controls.Add(changeBrowser, 6, 0);

        var settingsStrip = new Panel { Dock = DockStyle.Top, Height = 32 };
        settingsStrip.Controls.Add(settings);

        // 4) Footer strip: engine status.
        _enginePill = MakePill("Engine: —", Color.SlateGray);
        _enginePill.Anchor = AnchorStyles.Right;
        var footerHint = new Label
        {
            Text = "Close this window to stop the engine.",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };
        var footerInner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        footerInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footerInner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footerInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        footerInner.Controls.Add(footerHint, 0, 0);
        footerInner.Controls.Add(_enginePill, 1, 0);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 28 };
        footer.Controls.Add(footerInner);

        // 5) Log panel fills everything between the strips.
        _log = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BackColor = Color.FromArgb(18, 18, 24),
            ForeColor = Color.Gainsboro,
            Font = new Font("Cascadia Mono", 9F),
            BorderStyle = BorderStyle.FixedSingle,
        };

        // Dock order matters: add the Fill control first, then the strips,
        // so each strip takes its edge and the log gets the remaining space.
        Controls.Add(_log);
        Controls.Add(footer);
        Controls.Add(settingsStrip);
        Controls.Add(actionStrip);
        Controls.Add(header);
    }

    private void WireBackendEvents()
    {
        _backend.LogLine += line =>
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(() => AppendLog(line));
            }
        };
        _backend.ReadinessChanged += ready =>
        {
            if (!IsDisposed && IsHandleCreated)
            {
                BeginInvoke(() => SetPill(_enginePill, ready ? "Engine: Online" : "Engine: Offline",
                    ready ? Color.FromArgb(46, 125, 50) : Color.FromArgb(211, 47, 47)));
            }
        };
    }

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // First-run wizard before anything starts.
        if (!_config.Config.WizardCompleted)
        {
            RunWizard();
        }

        _ = CheckPrerequisitesAndStartAsync();
        _ = RunUpdateCheckAsync(silent: false);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closing)
        {
            _closing = true;
            _backend.Stop();
            _config.Save();
        }
        base.OnFormClosing(e);
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    private void BtnCheckUpdate_Click(object? sender, EventArgs e)
        => _ = RunUpdateCheckAsync(silent: true);

    /// <summary>
    /// New-user safety net: the engine cannot start without Node.js + pnpm,
    /// so check first and point missing tools at their install pages.
    /// </summary>
    private async Task CheckPrerequisitesAndStartAsync()
    {
        var result = await Task.Run(_prereq.Check);

        if (!result.NodeInstalled)
        {
            AppendLog("Node.js not found — the engine cannot start without it.");
            var answer = MessageBox.Show(
                this,
                "Node.js is required to run DeepSeek Harness, but it was not found on this computer.\n\n" +
                "Open the Node.js download page?",
                "Missing requirement",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes)
            {
                BrowserService.OpenApp("https://nodejs.org");
            }
            return;
        }

        if (!result.PnpmInstalled)
        {
            AppendLog("pnpm not found — the engine cannot start without it.");
            var answer = MessageBox.Show(
                this,
                "pnpm is required to run DeepSeek Harness, but it was not found.\n\n" +
                "Install it with:  npm install -g pnpm\n\n" +
                "Open the pnpm installation page?",
                "Missing requirement",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes)
            {
                BrowserService.OpenApp("https://pnpm.io/installation");
            }
            return;
        }

        AppendLog("Prerequisites OK (Node.js + pnpm found). Starting engine...");
        _backend.Start();
    }

    private async Task RunUpdateCheckAsync(bool silent)
    {
        SetPill(_updatePill, "Update: checking...", Color.FromArgb(21, 101, 192));

        var (status, latest, local) = await _updater.CheckAsync(_config.Config.HarnessPath);

        switch (status)
        {
            case UpdateStatus.UpToDate:
                SetPill(_updatePill, $"Update: Up to date ({local})", Color.FromArgb(46, 125, 50));
                if (!silent)
                {
                    MessageBox.Show(this, $"DeepSeek Harness is up to date (v{local}).", "Update check",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                break;

            case UpdateStatus.UpdateAvailable:
                SetPill(_updatePill, $"Update: v{latest} available", Color.FromArgb(230, 81, 0));
                var answer = MessageBox.Show(
                    this,
                    $"A newer version (v{latest}) is available. Open the GitHub releases page?",
                    "Update available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (answer == DialogResult.Yes)
                {
                    BrowserService.OpenApp($"https://github.com/{UpdateService.HarnessRepo}/releases");
                }
                break;

            default:
                SetPill(_updatePill, "Update: Unknown", Color.FromArgb(117, 117, 117));
                if (!silent)
                {
                    MessageBox.Show(this,
                        "Could not check for updates (offline, or the harness was not found).",
                        "Update check", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                break;
        }
    }

    private void OpenHarness()
    {
        var browser = _sessionBrowser ?? _config.Config.BrowserChoice;
        BrowserService.OpenApp(_config.Config.HarnessUrl, browser);
        AppendLog("Opened DS Harness interface.");
    }

    private void BtnApiKey_Click(object? sender, EventArgs e)
    {
        using var dialog = new ApiKeyDialog(_config.HasApiKey());
        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.EnteredKey))
        {
            _config.WriteApiKey(dialog.EnteredKey!);
            AppendLog("API key saved to " + _config.Config.ApiKeyPath);
            RefreshApiKeyUi();
        }
    }

    private void ChangeBrowser()
    {
        using var wizard = new FirstRunWizard(_config.Config.BrowserChoice, showRemember: false);
        if (wizard.ShowDialog(this) == DialogResult.OK)
        {
            _config.Config.BrowserChoice = wizard.BrowserChoice;
            _config.Save();
            _sessionBrowser = null;
            RefreshBrowserLabel();
        }
    }

    private void RunWizard()
    {
        using var wizard = new FirstRunWizard(_config.Config.BrowserChoice, showRemember: true);
        if (wizard.ShowDialog(this) == DialogResult.OK)
        {
            _config.Config.WizardCompleted = true;
            if (wizard.Remember)
            {
                _config.Config.BrowserChoice = wizard.BrowserChoice;
            }
            else
            {
                _sessionBrowser = wizard.BrowserChoice;
            }
            _config.Save();
            RefreshBrowserLabel();
        }
        else
        {
            // Cancelled the wizard: still mark it done so it does not nag every launch.
            _config.Config.WizardCompleted = true;
            _config.Save();
        }
    }

    // ------------------------------------------------------------------
    // UI helpers
    // ------------------------------------------------------------------

    private void AppendLog(string line)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        if (_log.TextLength > 80_000)
        {
            // Keep the log panel light by trimming the oldest half.
            _log.Text = _log.Text[^40_000..];
        }
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private void RefreshApiKeyUi()
    {
        if (_config.HasApiKey())
        {
            _apiKeyBox.Text = "•••••••••••• (saved)";
            _apiKeyButton.Text = "Change Key";
        }
        else
        {
            _apiKeyBox.Text = "";
            _apiKeyBox.PlaceholderText = "No key saved yet (sk-...)";
            _apiKeyButton.Text = "Save Key";
        }
    }

    private void RefreshBrowserLabel()
        => _browserLabel.Text = (_sessionBrowser ?? _config.Config.BrowserChoice) + " ▾";

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, Width = 116, AutoSize = false };
        button.Click += onClick;
        return button;
    }

    private static Label MakePill(string text, Color backColor)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(8, 3, 8, 3),
            BackColor = backColor,
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
        };
    }

    private static void SetPill(Label pill, string text, Color backColor)
    {
        pill.Text = text;
        pill.BackColor = backColor;
    }
}
