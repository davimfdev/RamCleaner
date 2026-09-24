namespace Lysma;

/// <summary>Ícone do app (lysma.ico embutido no .exe), no tamanho certo para cada lugar.</summary>
internal static class AppIcon
{
    public static Icon Load(int size)
    {
        try
        {
            using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("Lysma.lysma.ico");
            if (s != null) return new Icon(s, size, size);
        }
        catch { }
        return SystemIcons.Application;
    }

    /// <summary>Janela e barra de tarefas.</summary>
    public static Icon Window => Load(32);

    /// <summary>Bandeja: usa o tamanho pequeno do sistema (16, 20 ou 24 conforme o DPI).</summary>
    public static Icon Tray => Load(SystemInformation.SmallIconSize.Width);
}
