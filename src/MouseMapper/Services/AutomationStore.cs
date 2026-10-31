using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MouseMapper.Models;

namespace MouseMapper.Services;

/// <summary>
/// Persistência portable: automations.json na mesma pasta do executável.
/// A escrita é atômica (arquivo temporário + move) para não corromper os dados
/// caso o app seja encerrado no meio do salvamento.
/// </summary>
public sealed class AutomationStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string FilePath => AppPaths.DataFile;

    /// <summary>
    /// Carrega as automações. Nunca lança: arquivos inválidos são renomeados para
    /// .corrupt-&lt;timestamp&gt; e uma mensagem é devolvida em <paramref name="warning"/>.
    /// </summary>
    public List<Automation> Load(out string? warning)
    {
        warning = null;

        try
        {
            if (!File.Exists(FilePath))
            {
                return new List<Automation>();
            }

            string json = File.ReadAllText(FilePath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<Automation>();
            }

            var file = JsonSerializer.Deserialize<AutomationFile>(json, Options);
            if (file?.Automations is null)
            {
                return new List<Automation>();
            }

            foreach (var automation in file.Automations)
            {
                automation.Events ??= new List<MouseEventRecord>();

                // Recupera o texto legível caso o JSON tenha sido editado à mão.
                if (string.IsNullOrWhiteSpace(automation.Hotkey.Display) && automation.Hotkey.VirtualKey != 0)
                {
                    automation.Hotkey.Display = HotkeyFormatter.Describe(
                        automation.Hotkey.Modifiers,
                        automation.Hotkey.VirtualKey);
                }

                if (automation.Id == Guid.Empty)
                {
                    automation.Id = Guid.NewGuid();
                }
            }

            FileLogger.Instance.Info($"Carregadas {file.Automations.Count} automação(ões) de {FilePath}.");
            return file.Automations;
        }
        catch (Exception ex)
        {
            warning = $"O arquivo de automações está inválido e foi preservado como backup. Detalhes: {ex.Message}";
            FileLogger.Instance.Error("Falha ao carregar automations.json.", ex);

            try
            {
                string backup = $"{FilePath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                if (File.Exists(FilePath))
                {
                    File.Move(FilePath, backup, overwrite: true);
                }
            }
            catch (Exception backupError)
            {
                FileLogger.Instance.Error("Falha ao criar backup do arquivo corrompido.", backupError);
            }

            return new List<Automation>();
        }
    }

    /// <summary>Salva de forma síncrona (chamado na saída do app e após edições).</summary>
    public void Save(IEnumerable<Automation> automations)
    {
        try
        {
            var file = new AutomationFile { Version = 1, Automations = automations.ToList() };
            string json = JsonSerializer.Serialize(file, Options);
            string directory = Path.GetDirectoryName(FilePath) ?? AppPaths.BaseDirectory;
            Directory.CreateDirectory(directory);

            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Falha ao salvar automations.json.", ex);
            throw new IOException(
                $"Não foi possível salvar as automações em:\n{FilePath}\n\n" +
                $"Verifique se a pasta tem permissão de escrita.\n\nDetalhe: {ex.Message}",
                ex);
        }
    }

    public Task SaveAsync(IEnumerable<Automation> automations) => Task.Run(() => Save(automations));
}
