using System.IO;

namespace MouseMapper.Services;

/// <summary>
/// Caminhos usados pelo app. Tudo fica ao lado do .exe para manter o modo portable:
/// nada é gravado no registro do Windows ou em AppData (o log tem fallback se a pasta
/// do executável for somente leitura, por exemplo em um pendrive protegido).
/// </summary>
public static class AppPaths
{
    public static string BaseDirectory { get; } = ResolveBaseDirectory();

    public static string DataFile => Path.Combine(BaseDirectory, "automations.json");

    public static string LogDirectory => Path.Combine(BaseDirectory, "logs");

    private static string ResolveBaseDirectory()
    {
        try
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath))
            {
                string? directory = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    return directory;
                }
            }
        }
        catch
        {
            // Ignorado: usamos o fallback abaixo.
        }

        return AppContext.BaseDirectory;
    }
}
