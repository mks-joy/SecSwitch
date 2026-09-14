using SecSwitch.Core;

namespace SecSwitch.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly IReadOnlyList<ModuleManifest> _modules;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _extendItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly System.Windows.Forms.Timer _timer;
    private Task<int>? _sessionTask;

    public TrayApplicationContext()
    {
        _modules = ManifestLoader.LoadDirectory(ResolveModulesPath());

        _startItem = new ToolStripMenuItem("5분 보안 세션 시작");
        _extendItem = new ToolStripMenuItem("+5분 연장");
        _stopItem = new ToolStripMenuItem("지금 종료");
        _statusItem = new ToolStripMenuItem("상태 보기");
        var exitItem = new ToolStripMenuItem("SecSwitch 종료");

        _startItem.Click += async (_, _) => await StartSessionAsync();
        _extendItem.Click += async (_, _) => await ExtendSessionAsync();
        _stopItem.Click += async (_, _) => await StopSessionAsync();
        _statusItem.Click += (_, _) => ShowStatus();
        exitItem.Click += async (_, _) => await ExitAsync();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            _startItem,
            _extendItem,
            _stopItem,
            new ToolStripSeparator(),
            _statusItem,
            new ToolStripSeparator(),
            exitItem
        ]);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "SecSwitch",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += async (_, _) =>
        {
            if (SessionEngine.HasActiveSession)
            {
                ShowStatus();
            }
            else
            {
                await StartSessionAsync();
            }
        };

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 1000,
            Enabled = true
        };
        _timer.Tick += (_, _) => RefreshStatus();

        RefreshStatus();
        _notifyIcon.ShowBalloonTip(
            2500,
            "SecSwitch",
            ProfileStore.Exists
                ? "준비되었습니다. 트레이 아이콘을 더블클릭하면 5분 보안 세션을 시작합니다."
                : "초기 설정이 필요합니다. 현재 버전에서는 CLI의 'setup'을 먼저 실행해 주세요.",
            ToolTipIcon.Info);
    }

    private async Task StartSessionAsync()
    {
        if (SessionEngine.HasActiveSession)
        {
            ShowStatus();
            return;
        }

        if (!ProfileStore.Exists)
        {
            MessageBox.Show(
                "아직 SecSwitch 초기 설정 프로필이 없습니다.\n\n현재 알파 버전에서는 먼저 다음 명령을 실행해 주세요:\n\ndotnet run --project src\\SecSwitch.Cli -- setup",
                "SecSwitch 초기 설정 필요",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var profile = ProfileStore.Load();
        var managedModules = _modules
            .Where(module =>
                string.Equals(ProfileStore.GetMode(profile, module.Id), ProfileModes.OnDemand, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(module.SessionControl, "observeOnly", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (managedModules.Length == 0)
        {
            MessageBox.Show(
                "현재 5분 세션으로 제어할 수 있는 On-demand 모듈이 없습니다.",
                "SecSwitch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        _startItem.Enabled = false;
        _notifyIcon.Text = "SecSwitch - 시작 중";

        _sessionTask = Task.Run(() => SessionEngine.StartAsync(managedModules, 5, TextWriter.Null));

        // Give the worker enough time to create the persistent session marker before the
        // first status refresh. The timer keeps the UI synchronized after this point.
        await Task.Delay(150);
        RefreshStatus();

        var exitCode = await _sessionTask;
        RefreshStatus();

        _notifyIcon.ShowBalloonTip(
            2500,
            "SecSwitch",
            exitCode == 0
                ? "보안 세션이 종료되었고 SecSwitch가 시작한 모듈을 정리했습니다."
                : "보안 세션 종료 중 일부 모듈 복원에 실패했습니다. 상태를 확인해 주세요.",
            exitCode == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    private async Task ExtendSessionAsync()
    {
        if (!SessionEngine.HasActiveSession)
        {
            return;
        }

        var result = await SessionEngine.ExtendAsync(5, TextWriter.Null);
        RefreshStatus();

        if (result == 0)
        {
            _notifyIcon.ShowBalloonTip(1500, "SecSwitch", "보안 세션을 5분 연장했습니다.", ToolTipIcon.Info);
        }
    }

    private async Task StopSessionAsync()
    {
        if (!SessionEngine.HasActiveSession)
        {
            return;
        }

        _stopItem.Enabled = false;
        _notifyIcon.Text = "SecSwitch - 정리 중";
        var result = await Task.Run(() => SessionEngine.StopAsync(TextWriter.Null));
        RefreshStatus();

        _notifyIcon.ShowBalloonTip(
            2000,
            "SecSwitch",
            result == 0 ? "보안 세션을 종료했습니다." : "일부 모듈 정리에 실패했습니다.",
            result == 0 ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    private void ShowStatus()
    {
        var state = SessionEngine.LoadActiveSession();
        if (state is null)
        {
            var profile = ProfileStore.Load();
            var managedCount = _modules.Count(module =>
                string.Equals(ProfileStore.GetMode(profile, module.Id), ProfileModes.OnDemand, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(module.SessionControl, "observeOnly", StringComparison.OrdinalIgnoreCase));

            MessageBox.Show(
                $"현재 활성 보안 세션이 없습니다.\n\n5분 세션 대상: {managedCount}개 모듈",
                "SecSwitch 상태",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var remaining = state.ExpiresAtUtc - DateTimeOffset.UtcNow;
        var remainingText = remaining <= TimeSpan.Zero
            ? "종료 처리 중"
            : $"{Math.Max(0, (int)remaining.TotalMinutes):00}:{Math.Max(0, remaining.Seconds):00}";

        MessageBox.Show(
            $"보안 세션 실행 중\n\n남은 시간: {remainingText}\nSecSwitch가 이번 세션에서 시작한 모듈: {state.Modules.Count}개",
            "SecSwitch 상태",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void RefreshStatus()
    {
        var state = SessionEngine.LoadActiveSession();
        var active = state is not null;

        _startItem.Enabled = !active && (_sessionTask is null || _sessionTask.IsCompleted);
        _extendItem.Enabled = active;
        _stopItem.Enabled = active;

        if (!active)
        {
            _notifyIcon.Text = "SecSwitch - 대기 중";
            _startItem.Text = "5분 보안 세션 시작";
            return;
        }

        var remaining = state!.ExpiresAtUtc - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _notifyIcon.Text = "SecSwitch - 정리 중";
            _startItem.Text = "세션 종료 처리 중";
            return;
        }

        var minutes = Math.Max(0, (int)remaining.TotalMinutes);
        var seconds = Math.Max(0, remaining.Seconds);
        _notifyIcon.Text = $"SecSwitch - {minutes:00}:{seconds:00} 남음";
        _startItem.Text = $"보안 세션 실행 중 ({minutes:00}:{seconds:00})";
    }

    private async Task ExitAsync()
    {
        if (SessionEngine.HasActiveSession)
        {
            var answer = MessageBox.Show(
                "보안 세션이 실행 중입니다. SecSwitch가 시작한 모듈을 정리하고 종료할까요?",
                "SecSwitch 종료",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                return;
            }

            await Task.Run(() => SessionEngine.StopAsync(TextWriter.Null));
        }

        _notifyIcon.Visible = false;
        ExitThread();
    }

    private static string ResolveModulesPath()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "modules");
        if (Directory.Exists(packaged))
        {
            return packaged;
        }

        var working = Path.Combine(Directory.GetCurrentDirectory(), "modules");
        return working;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
