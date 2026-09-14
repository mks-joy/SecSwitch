namespace SecSwitch.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\SecSwitch.Tray", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "SecSwitch가 이미 실행 중입니다. 작업 표시줄 알림 영역을 확인해 주세요.",
                "SecSwitch",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
