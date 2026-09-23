namespace RamCleaner;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Só uma instância por usuário.
        using var mutex = new Mutex(true, @"Local\RamCleaner_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("O RamCleaner já está rodando (veja o ícone perto do relógio).", "RamCleaner",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Theme.Init(); // escolhe claro/escuro pelo Windows, uma vez

        var cfg = AppConfig.Load();

        // Mantém a entrada do registro em dia caso o .exe tenha mudado de pasta.
        if (cfg.StartWithWindows)
        {
            try { Startup.Apply(true); } catch { }
        }

        bool startHidden = args.Contains("--tray", StringComparer.OrdinalIgnoreCase) && cfg.StartMinimized;

        Application.Run(new MainForm(cfg, startHidden));
    }
}
