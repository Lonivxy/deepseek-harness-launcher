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
/// </summary>
public partial class MainForm : Form
{
    private readonly ConfigService _config = new();
    private readonly BackendService _backend;
    private readonly UpdateService _updater = new();

    private readonly TextBox _log;
    private readonly Label _updatePill;
    private readonly Label _enginePill;
    private readonly TextBox _apiKeyBox;
    private readonly Button _apiKeyButton;
    private readonly Label _browserLabel;

    private string? _sessionBrowser; // chosen this session but not remembered
    private bool _closing;

    public MainForm()
    {
        _config.Load();
        var cfg = _config.Config;
        _backend = new BackendService(cfg.HarnessPath, cfg.HarnessUrl);

        Text = "DSH Launcher";
        MinimumSize = new Size(780, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        // ---- Header row: title + update status pill ----
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(2) ?? "?";
        var title = new Label
        {
            Text = $"DSH Launcher  v{version}",
            Font = new Font(Font ?? SystemFonts.DefaultFont, FontStyle.Bold),
            AutoSize = true,
        };
        _updatePill = MakePill("Update: —", Color.SlateGray);
        _updatePill.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(_updatePill, 1, 0);

        // ---- Action buttons ----
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.Add(MakeButton("Check Update", BtnCheckUpdate_Click));
        actions.Controls.Add(MakeButton("Restart Backend", (_, _) => _backend.Restart()));
        actions.Controls.Add(MakeButton("Open DS Harness", (_, _) => OpenHarness()));

        // ---- API key + browser row ----
        _apiKeyBox = new TextBox
        {
            PasswordChar = '*',
            Width = 240,
            ReadOnly = true,
        };
        _apiKeyButton = MakeButton("", BtnApiKey_Click);
        _browserLabel = new Label { AutoSize = true };

        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5 };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var apiKeyLabel = new Label { Text = "API Key:", AutoSize = true, Anchor = AnchorStyles.Left };
        var browserHint = new Label { Text = "Browser:", AutoSize = true, Anchor = AnchorStyles.Left };
        var changeBrowser = MakeButton("Change", (_, _) => ChangeBrowser());

        settings.Controls.Add(apiKeyLabel, 0, 0);
        settings.Controls.Add(_apiKeyBox, 1, 0);
        settings.Controls.Add(_apiKeyButton, 2, 0);
        settings.Controls.Add(browserHint, 3, 0);
        settings.Controls.Add(_browserLabel, 4, 0);

        // ---- Log panel ----
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
        };

        // ---- Footer: engine status ----
        _enginePill = MakePill("Engine: —", Color.SlateGray);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(new Label { Text = "Close this window to stop the engine.", AutoSize = true }, 0, 0);
        footer.Controls.Add(_enginePill, 1, 0);

        // ---- Compose ----
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 5,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(actions, 0, 1);
        root.Controls.Add(settings, 0, 2);
        root.Controls.Add(_log, 0, 3);
        root.Controls.Add(footer, 0, 4);

        Controls.Add(root);

        // ---- Wire up backend + UI state ----
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

        RefreshApiKeyUi();
        RefreshBrowserLabel();
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

        _backend.Start();
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
        var button = new Button { Text = text, Width = 120, AutoSize = false };
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
