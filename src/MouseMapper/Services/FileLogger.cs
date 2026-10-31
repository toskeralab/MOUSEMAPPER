using System.IO;
using System.Text;

namespace MouseMapper.Services;

/// <summary>
/// Logger simples e à prova de falhas: nunca lança exceção para não derrubar o app.
/// Grava em "&lt;pasta do exe&gt;\logs\mousemapper.log" e usa %LOCALAPPDATA% como fallback.
/// </summary>
public sealed class FileLogger
{
    private const long MaxLogBytes = 2 * 1024 * 1024; // 2 MB

    public static FileLogger Instance { get; } = new();

    private readonly object _gate = new();
    private string? _logPath;

    private FileLogger()
    {
    }

    public string? LogPath
    {
        get
        {
            lock (_gate)
            {
                return _logPath ??= ResolveLogPath();
            }
        }
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            string? path = LogPath;
            if (path is null)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append(" [").Append(level).Append("] ")
                .Append(message);

            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            lock (_gate)
            {
                RotateIfNeeded(path);
                File.AppendAllText(path, builder.ToString() + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging nunca deve interromper o aplicativo.
        }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxLogBytes)
            {
                return;
            }

            string previous = path + ".1";
            if (File.Exists(previous))
            {
                File.Delete(previous);
            }

            File.Move(path, previous);
        }
        catch
        {
            // Ignorado.
        }
    }

    private static string? ResolveLogPath()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            return Path.Combine(AppPaths.LogDirectory, "mousemapper.log");
        }
        catch
        {
            // Pasta do executável sem permissão de escrita (ex.: pendrive somente leitura).
        }

        try
        {
            string fallbackDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MouseMapper",
                "logs");
            Directory.CreateDirectory(fallbackDir);
            return Path.Combine(fallbackDir, "mousemapper.log");
        }
        catch
        {
            return null;
        }
    }
}
