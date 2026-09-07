using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace NightInjection.Setup;

internal sealed class SetupWizard : Form
{
    private static readonly Color Surface = Color.FromArgb(27, 23, 37);
    private static readonly Color SurfaceRaised = Color.FromArgb(39, 35, 49);
    private static readonly Color TextPrimary = Color.FromArgb(247, 243, 249);
    private static readonly Color TextMuted = Color.FromArgb(181, 174, 190);
    private static readonly Color Accent = Color.FromArgb(218, 139, 229);
    private static readonly Color Success = Color.FromArgb(93, 207, 143);
    private static readonly Color Danger = Color.FromArgb(239, 113, 122);

    private readonly Action<bool> _installAction;
    private readonly Panel _content = new();
    private readonly Button _backButton = new();
    private readonly Button _nextButton = new();
    private readonly Button _cancelButton = new();
    private readonly Label[] _stepLabels = new Label[4];
    private readonly Panel[] _pages = new Panel[4];
    private readonly CheckBox _desktopShortcut = new();
    private readonly CheckBox _launchAfterInstall = new();
    private readonly Label _progressHeading = new();
    private readonly Label _progressDetail = new();
    private readonly ProgressBar _progressBar = new();
    private int _pageIndex;
    private bool _installing;
    private bool _installationFailed;

    public SetupWizard(string installRoot, Action<bool> installAction)
    {
        _installAction = installAction;
        Text = "Night Injection Setup";
        ClientSize = new Size(820, 520);
        MinimumSize = new Size(820, 520);
        MaximumSize = new Size(820, 520);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Surface;
        ForeColor = TextPrimary;
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        var executable = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executable))
        {
            Icon = Icon.ExtractAssociatedIcon(executable);
        }

        BuildLayout(installRoot);
        ShowPage(0);
        FormClosing += OnFormClosing;
    }

    public bool InstallSucceeded { get; private set; }
    public bool LaunchAfterInstall => InstallSucceeded && _launchAfterInstall.Checked;
    public int ExitCode => _installationFailed && !InstallSucceeded ? 1 : 0;

    private void BuildLayout(string installRoot)
    {
        var brand = new BrandPanel
        {
            Dock = DockStyle.Left,
            Width = 232,
            Padding = new Padding(28, 34, 24, 28)
        };
        var mark = new Label
        {
            Text = "\u26A1",
            Font = new Font("Segoe UI Symbol", 27, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 108, 0),
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(25, 27)
        };
        brand.Controls.Add(mark);

        brand.Controls.Add(new Label
        {
            Text = "NIGHT\nINJECTION",
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(70, 31)
        });

        var stepNames = new[] { "Welcome", "Options", "Installing", "Finish" };
        for (var index = 0; index < stepNames.Length; index++)
        {
            var label = new Label
            {
                Text = $"{index + 1:00}    {stepNames[index]}",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(176, 32),
                Location = new Point(28, 142 + (index * 49)),
                TextAlign = ContentAlignment.MiddleLeft
            };
            _stepLabels[index] = label;
            brand.Controls.Add(label);
        }

        brand.Controls.Add(new Label
        {
            Text = "STEAM CONFIGURATION TOOLKIT",
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(161, 149, 174),
            BackColor = Color.Transparent,
            AutoSize = true,
            Location = new Point(28, 458)
        });

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
        Controls.Add(body);
        Controls.Add(brand);

        var accentLine = new Panel
        {
            Dock = DockStyle.Top,
            Height = 3,
            BackColor = Accent
        };
        body.Controls.Add(accentLine);

        var footer = BuildFooter();
        body.Controls.Add(footer);

        _content.Dock = DockStyle.Fill;
        _content.Padding = new Padding(44, 38, 44, 24);
        _content.BackColor = Surface;
        body.Controls.Add(_content);
        _content.BringToFront();

        _pages[0] = BuildWelcomePage();
        _pages[1] = BuildOptionsPage(installRoot);
        _pages[2] = BuildProgressPage();
        _pages[3] = BuildFinishPage();
        foreach (var page in _pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _content.Controls.Add(page);
        }
    }

    private Panel BuildFooter()
    {
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 76,
            BackColor = Color.FromArgb(22, 19, 31),
            Padding = new Padding(28, 18, 28, 18)
        };

        ConfigureButton(_cancelButton, "Cancel", false);
        _cancelButton.Location = new Point(28, 20);
        _cancelButton.Click += (_, _) => Close();
        footer.Controls.Add(_cancelButton);

        ConfigureButton(_nextButton, "Next", true);
        _nextButton.Click += OnNext;
        footer.Controls.Add(_nextButton);

        ConfigureButton(_backButton, "Back", false);
        _backButton.Click += (_, _) => ShowPage(Math.Max(0, _pageIndex - 1));
        footer.Controls.Add(_backButton);
        footer.SizeChanged += (_, _) => PositionFooterButtons(footer, _nextButton, _backButton);
        PositionFooterButtons(footer, _nextButton, _backButton);
        return footer;
    }

    internal static bool VerifyLayout(string installRoot)
    {
        using var wizard = new SetupWizard(installRoot, _ => { })
        {
            Opacity = 0,
            ShowInTaskbar = false
        };
        wizard.Show();
        try
        {
            wizard.PerformLayout();
            var brand = wizard.Controls.OfType<BrandPanel>().Single();
            var body = wizard.Controls.Cast<Control>().Single(control => control is not BrandPanel);
            var footer = wizard._nextButton.Parent!;

            wizard.ShowPage(0);
            var welcomeIsValid = wizard._nextButton.Visible
                && wizard._nextButton.Text == "Next"
                && !wizard._backButton.Visible;

            wizard.ShowPage(1);
            var optionsAreValid = wizard._nextButton.Visible
                && wizard._nextButton.Text == "Install"
                && wizard._backButton.Visible;

            wizard.ShowPage(3);
            var finishIsValid = wizard._nextButton.Visible
                && wizard._nextButton.Text == "Finish";

            return body.Left >= brand.Right
                && IsInside(wizard._cancelButton, footer)
                && IsInside(wizard._backButton, footer)
                && IsInside(wizard._nextButton, footer)
                && welcomeIsValid
                && optionsAreValid
                && finishIsValid;
        }
        finally
        {
            wizard.Close();
        }
    }

    private static Panel BuildWelcomePage()
    {
        var page = NewPage();
        page.Controls.Add(NewHeading("Install Night Injection", 0, 8));
        page.Controls.Add(NewParagraph(
            "This wizard will install Night Injection on your computer and register secure website import links.",
            0,
            72,
            470,
            60));

        var card = NewCard(new Point(0, 164), new Size(470, 134));
        card.Controls.Add(new Label
        {
            Text = "WHAT WILL BE INSTALLED",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Accent,
            AutoSize = true,
            Location = new Point(22, 18)
        });
        card.Controls.Add(NewParagraph(
            "Night Injection application\nStart menu shortcut\nnight-injection:// website integration",
            22,
            47,
            410,
            72));
        page.Controls.Add(card);

        page.Controls.Add(NewParagraph(
            "No administrator permission is required.",
            0,
            326,
            470,
            28));
        return page;
    }

    private Panel BuildOptionsPage(string installRoot)
    {
        var page = NewPage();
        page.Controls.Add(NewHeading("Installation options", 0, 8));
        page.Controls.Add(NewParagraph(
            "Review the destination and choose which shortcuts to create.",
            0,
            58,
            470,
            30));

        page.Controls.Add(new Label
        {
            Text = "INSTALL LOCATION",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = TextMuted,
            AutoSize = true,
            Location = new Point(0, 121)
        });

        var location = new TextBox
        {
            Text = installRoot,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SurfaceRaised,
            ForeColor = TextPrimary,
            Font = new Font("Segoe UI", 9.5f),
            Location = new Point(0, 146),
            Size = new Size(470, 36)
        };
        page.Controls.Add(location);

        _desktopShortcut.Text = "Create a shortcut on the desktop";
        _desktopShortcut.Checked = true;
        _desktopShortcut.AutoSize = true;
        _desktopShortcut.Font = new Font("Segoe UI", 10);
        _desktopShortcut.ForeColor = TextPrimary;
        _desktopShortcut.Location = new Point(0, 218);
        page.Controls.Add(_desktopShortcut);

        var note = NewCard(new Point(0, 278), new Size(470, 76));
        note.Controls.Add(NewParagraph(
            "Your existing installation is safely backed up while the update is applied.",
            18,
            16,
            430,
            44));
        page.Controls.Add(note);
        return page;
    }

    private Panel BuildProgressPage()
    {
        var page = NewPage();
        _progressHeading.Text = "Installing Night Injection";
        _progressHeading.Font = new Font("Segoe UI", 23, FontStyle.Bold);
        _progressHeading.ForeColor = TextPrimary;
        _progressHeading.AutoSize = true;
        _progressHeading.Location = new Point(0, 8);
        page.Controls.Add(_progressHeading);

        _progressDetail.Text = "Preparing the installation...";
        _progressDetail.Font = new Font("Segoe UI", 10);
        _progressDetail.ForeColor = TextMuted;
        _progressDetail.AutoSize = false;
        _progressDetail.Size = new Size(470, 54);
        _progressDetail.Location = new Point(0, 72);
        page.Controls.Add(_progressDetail);

        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 28;
        _progressBar.Location = new Point(0, 158);
        _progressBar.Size = new Size(470, 8);
        page.Controls.Add(_progressBar);

        var card = NewCard(new Point(0, 205), new Size(470, 112));
        card.Controls.Add(new Label
        {
            Text = "PLEASE WAIT",
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            ForeColor = Accent,
            AutoSize = true,
            Location = new Point(20, 18)
        });
        card.Controls.Add(NewParagraph(
            "Setup is copying verified files and configuring the website connection.",
            20,
            46,
            420,
            50));
        page.Controls.Add(card);
        return page;
    }

    private Panel BuildFinishPage()
    {
        var page = NewPage();
        var successMark = new Label
        {
            Text = "\u2713",
            Font = new Font("Segoe UI Symbol", 25, FontStyle.Bold),
            ForeColor = Success,
            BackColor = Color.FromArgb(30, 70, 49),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 8),
            Size = new Size(56, 56)
        };
        page.Controls.Add(successMark);
        page.Controls.Add(NewHeading("Installation complete", 0, 89));
        page.Controls.Add(NewParagraph(
            "Night Injection is ready. You can launch it now or open it later from the Start menu.",
            0,
            142,
            470,
            58));

        _launchAfterInstall.Text = "Launch Night Injection when Setup closes";
        _launchAfterInstall.Checked = true;
        _launchAfterInstall.AutoSize = true;
        _launchAfterInstall.Font = new Font("Segoe UI", 10);
        _launchAfterInstall.ForeColor = TextPrimary;
        _launchAfterInstall.Location = new Point(0, 236);
        page.Controls.Add(_launchAfterInstall);

        var card = NewCard(new Point(0, 288), new Size(470, 72));
        card.Controls.Add(NewParagraph(
            "Website imports are now connected to this installation.",
            18,
            15,
            430,
            42));
        page.Controls.Add(card);
        return page;
    }

    private async void OnNext(object? sender, EventArgs args)
    {
        if (_pageIndex == 0)
        {
            ShowPage(1);
            return;
        }

        if (_pageIndex == 1 || (_pageIndex == 2 && _installationFailed))
        {
            await RunInstallationAsync();
            return;
        }

        if (_pageIndex == 3)
        {
            Close();
        }
    }

    private async Task RunInstallationAsync()
    {
        var createDesktopShortcut = _desktopShortcut.Checked;
        _installationFailed = false;
        _installing = true;
        ShowPage(2);
        _progressHeading.Text = "Installing Night Injection";
        _progressHeading.ForeColor = TextPrimary;
        _progressDetail.Text = "Copying verified files and registering the application...";
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 28;
        UpdateButtons();

        try
        {
            await Task.Run(() =>
            {
                var result = NativeMethods.CoInitializeEx(IntPtr.Zero, 0);
                Marshal.ThrowExceptionForHR(result);
                try
                {
                    _installAction(createDesktopShortcut);
                }
                finally
                {
                    NativeMethods.CoUninitialize();
                }
            });
            InstallSucceeded = true;
            ShowPage(3);
        }
        catch (Exception exception)
        {
            _installationFailed = true;
            _progressHeading.Text = "Setup could not continue";
            _progressHeading.ForeColor = Danger;
            _progressDetail.Text = exception.Message;
            _progressBar.Style = ProgressBarStyle.Blocks;
            _progressBar.Value = 0;
        }
        finally
        {
            _installing = false;
            UpdateButtons();
        }
    }

    private void ShowPage(int index)
    {
        _pageIndex = index;
        for (var page = 0; page < _pages.Length; page++)
        {
            _pages[page].Visible = page == index;
            _stepLabels[page].ForeColor = page == index
                ? Color.White
                : page < index ? Accent : TextMuted;
        }

        _pages[index].BringToFront();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _backButton.Visible = _pageIndex is 1 or 2 && !_installing && !InstallSucceeded;
        _backButton.Enabled = !_installing;
        _cancelButton.Visible = _pageIndex != 3;
        _cancelButton.Enabled = !_installing;
        _nextButton.Enabled = !_installing;
        _nextButton.Visible = !_installing;
        _nextButton.Text = _pageIndex switch
        {
            0 => "Next",
            1 => "Install",
            2 when _installationFailed => "Try again",
            3 => "Finish",
            _ => "Next"
        };
        AcceptButton = _nextButton.Visible ? _nextButton : null;
        CancelButton = _cancelButton.Enabled ? _cancelButton : null;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs args)
    {
        if (_installing)
        {
            args.Cancel = true;
        }
    }

    private static void PositionFooterButtons(Control footer, Control next, Control back)
    {
        const int rightMargin = 28;
        const int gap = 12;
        const int top = 20;
        next.Location = new Point(footer.ClientSize.Width - rightMargin - next.Width, top);
        back.Location = new Point(next.Left - gap - back.Width, top);
    }

    private static bool IsInside(Control child, Control parent) =>
        child.Left >= 0
        && child.Top >= 0
        && child.Right <= parent.ClientSize.Width
        && child.Bottom <= parent.ClientSize.Height;

    private static Panel NewPage() => new() { BackColor = Surface };

    private static Panel NewCard(Point location, Size size) => new()
    {
        Location = location,
        Size = size,
        BackColor = SurfaceRaised
    };

    private static Label NewHeading(string text, int x, int y) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 23, FontStyle.Bold),
        ForeColor = TextPrimary,
        AutoSize = true,
        Location = new Point(x, y)
    };

    private static Label NewParagraph(string text, int x, int y, int width, int height) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 9.5f),
        ForeColor = TextMuted,
        AutoSize = false,
        Location = new Point(x, y),
        Size = new Size(width, height)
    };

    private static void ConfigureButton(Button button, string text, bool primary)
    {
        button.Text = text;
        button.Size = new Size(96, 38);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(83, 76, 94);
        button.BackColor = primary ? Accent : SurfaceRaised;
        button.ForeColor = primary ? Color.FromArgb(34, 17, 37) : TextPrimary;
        button.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private sealed class BrandPanel : Panel
    {
        protected override void OnPaintBackground(PaintEventArgs args)
        {
            using var brush = new LinearGradientBrush(
                ClientRectangle,
                Color.FromArgb(16, 0, 48),
                Color.FromArgb(49, 22, 39),
                120f);
            args.Graphics.FillRectangle(brush, ClientRectangle);
        }
    }
}
