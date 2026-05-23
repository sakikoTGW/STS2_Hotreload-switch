namespace ModHotReload.Runtime;

internal static class ModFileUtil
{
    internal static bool WaitForStableFile(string path, int maxAttempts = 25, int delayMs = 80)
    {
        long lastSize = -1;
        for (int i = 0; i < maxAttempts; i++)
        {
            if (!File.Exists(path))
            {
                Thread.Sleep(delayMs);
                continue;
            }

            try
            {
                long size = new FileInfo(path).Length;
                if (size > 0 && size == lastSize)
                    return true;

                lastSize = size;
            }
            catch
            {
                // 仍被编译器占用
            }

            Thread.Sleep(delayMs);
        }

        return File.Exists(path);
    }

    internal static string ShadowCopyDll(string sourceDll, string modId)
    {
        string dir = Path.Combine(Path.GetTempPath(), "STS2_ModHotReload", modId);
        Directory.CreateDirectory(dir);

        string dest = Path.Combine(dir, $"{modId}_{DateTime.UtcNow.Ticks}.dll");
        Exception? last = null;

        for (int i = 0; i < 12; i++)
        {
            try
            {
                File.Copy(sourceDll, dest, overwrite: true);
                return dest;
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(100);
            }
        }

        throw new IOException($"无法复制 DLL 到影子路径（文件可能被占用）: {sourceDll}", last);
    }
}
