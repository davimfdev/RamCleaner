using Microsoft.Win32;

namespace Lysma;

/// <summary>Liga/desliga a inicialização com o Windows (HKCU\...\Run, não precisa de admin).</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Lysma";
    private const string LegacyValueName = "RamCleaner"; // nome antigo do app

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        // Remove a entrada do nome antigo, senão abririam dois apps no boot.
        if (key.GetValue(LegacyValueName) != null)
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);

        if (enabled)
        {
            string exe = Environment.ProcessPath ?? Application.ExecutablePath;
            key.SetValue(ValueName, $"\"{exe}\" --tray");
        }
        else if (key.GetValue(ValueName) != null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
