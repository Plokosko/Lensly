using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Lensly.Services;

public static class TrashService
{
    public static void MoveToTrash(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var script = $"-e \"tell application \\\"Finder\\\" to delete POSIX file \\\"{filePath}\\\"\"";
                Process.Start("osascript", script);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var script = $"Add-Type -AssemblyName Microsoft.VisualBasic; [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile('{filePath}', 'UIOption.OnlyErrorDialogs', 'RecycleOption.SendToRecycleBin')";
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("gio", $"trash \"{filePath}\"");
            }
        }
        catch
        {
            Debug.WriteLine("Failed to move to OS trash.");
        }
    }
}