using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace InstallerLauncher;

internal static class Program
{
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("ISG2CFG!");

    public static async Task<int> Main()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            Console.Error.WriteLine("Unable to locate launcher executable.");
            return 1;
        }

        string tempScript = Path.Combine(Path.GetTempPath(),
            "isg-" + Guid.NewGuid().ToString("N") + ".ps1");
        try
        {
            await using var exe = new FileStream(exePath, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite, bufferSize: 65536, useAsync: true);

            if (exe.Length < 16)
                throw new InvalidDataException("No installation script found.");
            exe.Seek(-16, SeekOrigin.End);
            var footer = new byte[16];
            await exe.ReadExactlyAsync(footer);
            long scriptLength = BinaryPrimitives.ReadInt64LittleEndian(footer.AsSpan(0, 8));
            if (!footer.AsSpan(8, 8).SequenceEqual(Marker) ||
                scriptLength <= 0 || scriptLength > exe.Length - 16)
                throw new InvalidDataException("Invalid or missing installer configuration.");

            exe.Seek(-16 - scriptLength, SeekOrigin.End);
            await using (var target = new FileStream(tempScript, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, bufferSize: 65536, useAsync: true))
            {
                byte[] buffer = new byte[65536];
                long remaining = scriptLength;
                while (remaining > 0)
                {
                    int count = await exe.ReadAsync(buffer.AsMemory(0,
                        (int)Math.Min(buffer.Length, remaining)));
                    if (count == 0) throw new EndOfStreamException();
                    await target.WriteAsync(buffer.AsMemory(0, count));
                    remaining -= count;
                }
            }

            var processInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            };
            processInfo.ArgumentList.Add("-NoProfile");
            processInfo.ArgumentList.Add("-ExecutionPolicy");
            processInfo.ArgumentList.Add("Bypass");
            processInfo.ArgumentList.Add("-File");
            processInfo.ArgumentList.Add(tempScript);
            processInfo.Environment["ISG_LAUNCH_DIR"] = AppContext.BaseDirectory;

            using var process = Process.Start(processInfo) ??
                throw new InvalidOperationException("Unable to start PowerShell.");
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Install Script Generator launcher: " + ex.Message);
            return 1;
        }
        finally
        {
            try { if (File.Exists(tempScript)) File.Delete(tempScript); }
            catch (IOException) { /* Windows may still release the file handle. */ }
        }
    }
}
