using System.Runtime.InteropServices;

namespace Lysma;

/// <summary>Chamadas diretas à API do Windows.</summary>
internal static class Native
{
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint PROCESS_SET_QUOTA = 0x0100;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// Remove o máximo possível de páginas do working set do processo
    /// (é o mesmo efeito que o Firemin usa). As páginas vão para a lista
    /// "standby"/arquivo de paginação e voltam sob demanda quando o app precisar.
    /// </summary>
    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EmptyWorkingSet(IntPtr processHandle);

    /// <summary>Tenta esvaziar o working set de um PID. Retorna false se sem permissão.</summary>
    public static bool TrimProcess(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SET_QUOTA, false, pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            return EmptyWorkingSet(h);
        }
        finally
        {
            CloseHandle(h);
        }
    }
}
