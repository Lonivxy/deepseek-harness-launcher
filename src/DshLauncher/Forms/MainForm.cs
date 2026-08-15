using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
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
    private Label _browserLabel = null!;
    private Button _installButton = null!;
    private FlowLayoutPanel _actionsPanel = null!;
    private CheckBox _autoOpenBox = null!;

    private string? _sessionBrowser; // chosen this session but not remembered
    private bool _closing;
    private CancellationTokenSource? _installCts;
    private bool _wasReady;

    public MainForm()
    {
        _config.Load();
        var cfg = _config.Config;
        _backend = new BackendService(cfg.HarnessPath, cfg.HarnessUrl);

        Text = "DeepSeek Harness Launcher";
        // Fixed, non-resizable window so the layout always renders as designed.
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(900, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        BuildLayout();
        WireBackendEvents();

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
            Text = $"DeepSeek Harness Launcher  v{version}",
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

        var header = new Panel { Dock = DockStyle.Top, Height = 32 };
        header.Controls.Add(headerInner);

        // 2) Action buttons strip.
        _actionsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(0, 3, 0, 3),
        };
        _actionsPanel.Controls.Add(MakeButton("Check Update", BtnCheckUpdate_Click));
        _actionsPanel.Controls.Add(MakeButton("Restart Backend", (_, _) => _backend.Restart()));
        _actionsPanel.Controls.Add(MakeButton("Open DS Harness", (_, _) => OpenHarness()));
        _installButton = MakeButton("Install Harness", async (_, _) => await RunInstallAsync());
        _installButton.Visible = !HarnessInstallerService.IsInstalled(_config.Config.HarnessPath);
        _actionsPanel.Controls.Add(_installButton);

        var actionStrip = new Panel { Dock = DockStyle.Top, Height = 42 };
        actionStrip.Controls.Add(_actionsPanel);

        // 3) Settings strip: browser picker, right-aligned.
        _browserLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        var browserHint = new Label { Text = "Browser:", AutoSize = true, Anchor = AnchorStyles.Left };
        var changeBrowser = MakeButton("Change", (_, _) => ChangeBrowser());

        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5 };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _autoOpenBox = new CheckBox
        {
            Text = "Auto-open interface when ready",
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Checked = _config.Config.AutoOpenHarness,
        };
        var autoOpenTip = new ToolTip();
        autoOpenTip.SetToolTip(_autoOpenBox,
            "DeepSeek Harness will automatically open after the engine starts.");
        _autoOpenBox.CheckedChanged += (_, _) =>
        {
            _config.Config.AutoOpenHarness = _autoOpenBox.Checked;
            _config.Save();
        };

        settings.Controls.Add(new Label { AutoSize = true }, 0, 0); // zero-width spacer
        settings.Controls.Add(browserHint, 1, 0);
        settings.Controls.Add(_browserLabel, 2, 0);
        settings.Controls.Add(changeBrowser, 3, 0);
        settings.Controls.Add(_autoOpenBox, 4, 0);

        var settingsStrip = new Panel { Dock = DockStyle.Top, Height = 38 };
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

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 30 };
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
                BeginInvoke(() =>
                {
                    if (ready && !_wasReady)
                    {
                        TryAutoOpenOnReady();
                    }
                    _wasReady = ready;
                });
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
        _ = RunUpdateCheckAsync(silent: !HarnessInstallerService.IsInstalled(_config.Config.HarnessPath));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_closing)
        {
            _closing = true;
            _installCts?.Cancel();
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

        if (!result.GitInstalled)
        {
            AppendLog("Git not found — it is needed to download the harness.");
            var answer = MessageBox.Show(
                this,
                "Git is required to download DeepSeek Harness, but it was not found on this computer.\n\n" +
                "Open the Git download page?",
                "Missing requirement",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes)
            {
                BrowserService.OpenApp("https://git-scm.com/downloads");
            }
            return;
        }

        if (!result.PnpmInstalled)
        {
            AppendLog("pnpm not found — it can be installed automatically via npm.");
            var answer = MessageBox.Show(
                this,
                "pnpm (the package manager used by DeepSeek Harness) was not found.\n\n" +
                "Install it automatically now?",
                "Missing requirement",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer == DialogResult.Yes)
            {
                var installer = new HarnessInstallerService();
                installer.LogLine += SafeAppendLog;
                if (!await installer.EnsurePnpmAsync())
                {
                    AppendLog("pnpm installation did not complete.");
                    return;
                }
            }
            else
            {
                return;
            }
        }

        if (!HarnessInstallerService.IsInstalled(_config.Config.HarnessPath))
        {
            AppendLog("DeepSeek Harness is not installed at " + _config.Config.HarnessPath);
            var answer = MessageBox.Show(
                this,
                "DeepSeek Harness is not installed yet.\n\n" +
                "The launcher can download and set it up for you automatically " +
                $"(downloads ~1 GB of packages; saved to {_config.Config.HarnessPath}).\n\n" +
                "First-time setup typically takes 5-10 minutes depending on your internet speed.\n\n" +
                "Install it now?",
                "One-time setup",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (answer == DialogResult.Yes)
            {
                await RunInstallAsync();
                return; // RunInstallAsync starts the engine when it succeeds.
            }

            AppendLog("Setup skipped — the engine will not start until the harness is installed.");
            return;
        }

        AppendLog("Prerequisites OK (Node.js, pnpm, git found). Starting engine...");
        if (_autoOpenBox.Checked)
        {
            AppendLog("Note: DeepSeek Harness will automatically open after the engine starts.");
        }
        _backend.Start();
    }

    /// <summary>Opens the DS Harness interface automatically when the engine comes online.</summary>
    private void TryAutoOpenOnReady()
    {
        if (!_autoOpenBox.Checked
            || !HarnessInstallerService.IsInstalled(_config.Config.HarnessPath))
        {
            return;
        }

        var browser = _sessionBrowser ?? _config.Config.BrowserChoice;
        BrowserService.OpenApp(_config.Config.HarnessUrl, browser);
        AppendLog("Engine ready — opened DS Harness interface automatically.");
    }

    /// <summary>Runs the one-click harness installer, streaming progress to the log.</summary>
    private async Task RunInstallAsync()
    {
        SetControlsEnabled(false);
        _installCts = new CancellationTokenSource();
        try
        {
            var installer = new HarnessInstallerService();
            installer.LogLine += SafeAppendLog;

            var ok = await installer.InstallAsync(_config.Config.HarnessPath, _installCts.Token);
            if (ok)
            {
                AppendLog("DeepSeek Harness is ready. Starting the engine...");
                _backend.Start();
            }
            else
            {
                MessageBox.Show(this,
                    "Installation did not complete. Check the log above for details.",
                    "Install failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            _installCts?.Dispose();
            _installCts = null;
            SetControlsEnabled(true);
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        foreach (Control control in _actionsPanel.Controls)
        {
            control.Enabled = enabled;
        }
    }

    private void SafeAppendLog(string line)
    {
        if (!IsDisposed && IsHandleCreated)
        {
            BeginInvoke(() => AppendLog(line));
        }
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
                AppendLog("Update check failed — could not reach the GitHub API or the CDN mirror.");
                if (!silent)
                {
                    MessageBox.Show(this,
                        "Could not check for updates. Check your internet connection and try again.",
                        "Update check", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                break;
        }
    }

    private void OpenHarness()
    {
        if (!HarnessInstallerService.IsInstalled(_config.Config.HarnessPath))
        {
            AppendLog("Cannot open the interface — the harness is not installed yet.");
            return;
        }

        var browser = _sessionBrowser ?? _config.Config.BrowserChoice;
        BrowserService.OpenApp(_config.Config.HarnessUrl, browser);
        AppendLog("Opened DS Harness interface.");
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

    private void RefreshBrowserLabel()
        => _browserLabel.Text = (_sessionBrowser ?? _config.Config.BrowserChoice) + " ▾";

    private static Button MakeButton(string text, EventHandler onClick)
    {
        // Explicit height so text is never clipped on high-DPI displays.
        var button = new Button
        {
            Text = text,
            Width = 116,
            Height = 28,
            AutoSize = false,
            Margin = new Padding(0, 0, 8, 0),
        };
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
