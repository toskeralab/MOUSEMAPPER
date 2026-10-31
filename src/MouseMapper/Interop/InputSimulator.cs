using System.Runtime.InteropServices;
using MouseMapper.Services;

namespace MouseMapper.Interop;

/// <summary>
/// Reproduz movimentos e cliques usando SendInput com coordenadas absolutas
/// normalizadas para a área de trabalho virtual (funciona com múltiplos monitores).
/// </summary>
internal static class InputSimulator
{
    private const uint AbsoluteMoveFlags =
        NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK;

    public static bool MoveTo(int x, int y) => Send(MoveInput(x, y));

    public static bool LeftDown(int x, int y)
        => Send(MoveInput(x, y), ButtonInput(NativeMethods.MOUSEEVENTF_LEFTDOWN));

    public static bool LeftUp(int x, int y)
        => Send(MoveInput(x, y), ButtonInput(NativeMethods.MOUSEEVENTF_LEFTUP));

    public static bool RightDown(int x, int y)
        => Send(MoveInput(x, y), ButtonInput(NativeMethods.MOUSEEVENTF_RIGHTDOWN));

    public static bool RightUp(int x, int y)
        => Send(MoveInput(x, y), ButtonInput(NativeMethods.MOUSEEVENTF_RIGHTUP));

    public static bool MiddleDown(int x, int y)
        => Send(MoveInput(x, y), ButtonInput(NativeMethods.MOUSEEVENTF_MIDDLEDOWN));

    public static bool MiddleUp(int x, int y)
        => Send(MoveInput(x, y), ButtonInput(NativeMethods.MOUSEEVENTF_MIDDLEUP));

    public static bool Wheel(int delta, int x, int y)
    {
        var input = MoveInput(x, y);
        var wheel = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.INPUTUNION
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    mouseData = unchecked((uint)delta),
                    dwFlags = NativeMethods.MOUSEEVENTF_WHEEL
                }
            }
        };
        return Send(input, wheel);
    }

    /// <summary>
    /// Dois cliques em sequência imediata. Enviados em um único SendInput para que o
    /// Windows reconheça o duplo clique (o intervalo entre eles fica abaixo do limite).
    /// </summary>
    public static bool DoubleClick(int x, int y)
        => Send(
            MoveInput(x, y),
            ButtonInput(NativeMethods.MOUSEEVENTF_LEFTDOWN),
            ButtonInput(NativeMethods.MOUSEEVENTF_LEFTUP),
            ButtonInput(NativeMethods.MOUSEEVENTF_LEFTDOWN),
            ButtonInput(NativeMethods.MOUSEEVENTF_LEFTUP));

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------
    private static NativeMethods.INPUT MoveInput(int x, int y)
    {
        var (dx, dy) = Normalize(x, y);
        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.INPUTUNION
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = AbsoluteMoveFlags
                }
            }
        };
    }

    private static NativeMethods.INPUT ButtonInput(uint flags) => new()
    {
        type = NativeMethods.INPUT_MOUSE,
        u = new NativeMethods.INPUTUNION
        {
            mi = new NativeMethods.MOUSEINPUT { dwFlags = flags }
        }
    };

    /// <summary>Converte pixels de tela em coordenadas absolutas 0..65535 do desktop virtual.</summary>
    private static (int dx, int dy) Normalize(int x, int y)
    {
        int left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int width = Math.Max(2, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN));
        int height = Math.Max(2, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));

        int dx = (int)Math.Round((x - left) * 65535.0 / (width - 1));
        int dy = (int)Math.Round((y - top) * 65535.0 / (height - 1));
        return (Math.Clamp(dx, 0, 65535), Math.Clamp(dy, 0, 65535));
    }

    private static bool Send(params NativeMethods.INPUT[] inputs)
    {
        if (inputs.Length == 0)
        {
            return true;
        }

        try
        {
            uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
            if (sent != inputs.Length)
            {
                FileLogger.Instance.Warn(
                    $"SendInput enviou {sent}/{inputs.Length} eventos (último erro: " +
                    $"{Marshal.GetLastWin32Error()}). Verifique UIPI/elevação do processo.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            FileLogger.Instance.Error("Falha ao executar SendInput.", ex);
            return false;
        }
    }
}
