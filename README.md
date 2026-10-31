# MOUSEMAPPER

Gravador de macros de mouse para Windows com atalhos globais de teclado.
Aplicativo **portable**: um único `MouseMapper.exe`, sem instalador, sem dependências externas
(o runtime .NET vai embutido) e com os dados salvos ao lado do executável.

- **Stack:** C# / .NET 8 / WPF
- **Hooks:** `SetWindowsHookEx` (`WH_MOUSE_LL`, `WH_KEYBOARD_LL`) via P/Invoke
- **Atalhos globais:** `RegisterHotKey` + janela *message-only*
- **Reprodução:** `SendInput` com coordenadas absolutas normalizadas (multi-monitor)
- **Persistência:** `System.Text.Json` → `automations.json`
- **Bandeja:** `NotifyIcon` (WinForms, já incluído no Windows Desktop Runtime)
- **Empacotamento:** publicação *self-contained* em arquivo único (`win-x64`)

---

## Status de validação

| Verificação | Resultado |
| --- | --- |
| `dotnet build -c Release` | ✅ 0 erros, 0 avisos |
| `dotnet publish` single-file `win-x64` | ✅ `dist/MouseMapper.exe` (PE32+ GUI x64) |
| Compilação de todo o XAML (janelas, estilos, handlers) | ✅ validada pelo compilador WPF |
| Recursos visuais referenciados (`StaticResource`) | ✅ todos definidos |
| Execução real (hooks, bandeja, gravação/reprodução) | ⚠️ **precisa ser testada no Windows** — o ambiente de build é Linux e não executa WPF/hooks |

O projeto compila e empacota. Como hooks de baixo nível, `RegisterHotKey`, bandeja e `SendInput`
são APIs exclusivas do Windows, o teste funcional precisa ser feito em uma máquina Windows.

---

## Requisitos

**Para usar o executável pronto**
- Windows 10/11 (x64)
- Nada mais: o .NET 8 vai embutido no `.exe`

**Para compilar**
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (8.0.4xx ou superior)
- Windows (recomendado) **ou** Linux/macOS com `EnableWindowsTargeting` (já configurado no `.csproj`)

Verifique a instalação:

```bash
dotnet --version    # deve imprimir 8.x
```

---

## Estrutura do projeto

```
MouseMapper.sln
scripts/
  build-portable.ps1        # publicação portable (Windows)
  build-portable.cmd        # atalho com duplo clique
  build-portable.sh         # publicação a partir de Linux/macOS
  generate-icon.py          # regenera o Assets/mousemapper.ico

src/MouseMapper/
  MouseMapper.csproj        # net8.0-windows, WPF, single-file, ícone, manifesto
  app.manifest              # UAC (requireAdministrator) + DPI PerMonitorV2
  App.xaml / App.xaml.cs    # tema escuro, instância única, tratamento global de erros
  MainWindow.xaml / .cs     # lista, editor inline, gravação, reprodução, bandeja
  Assets/mousemapper.ico    # ícone do exe, da janela e do tray

  Models/
    Automation.cs           # macro: id, nome, atalho, data, eventos
    MouseEventRecord.cs     # tipo, X, Y, timestamp (ms), data (scroll)
    MouseEventType.cs       # enum serializado como texto
    HotkeyDefinition.cs     # display + modifiers + virtualKey
    AutomationFile.cs       # envelope { version, automations[] }

  Interop/
    NativeMethods.cs        # todo o P/Invoke (hooks, hotkeys, SendInput, DWM)
    InputSimulator.cs       # SendInput com coordenadas absolutas

  Services/
    HookManager.cs          # base LowLevelHook + MouseHook + KeyboardHook
    AutomationRecorder.cs   # captura + normalização (duplo clique, tempo relativo)
    AutomationPlayer.cs     # reprodução assíncrona com delays e cancelamento
    HotkeyService.cs        # RegisterHotKey, conflito 1409, suspend/resume
    HotkeyFormatter.cs      # VK → texto ("Ctrl+Shift+1"), leitura de modificadores
    AutomationStore.cs      # JSON atômico ao lado do exe
    TrayIconService.cs      # NotifyIcon + menu escuro
    FileLogger.cs           # log com rotação e fallback
    AppPaths.cs             # caminhos portables

  Views/
    HotkeyCaptureWindow.xaml / .cs   # captura da combinação via WH_KEYBOARD_LL
```

---

## Do zero: comandos de instalação

```bash
# 1) Instalar o .NET 8 SDK (link acima) e conferir
dotnet --version

# 2) (Opcional) criar um projeto equivalente do zero
dotnet new wpf -n MouseMapper
dotnet new sln -n MouseMapper
dotnet sln MouseMapper.sln add MouseMapper/MouseMapper.csproj

# 3) Restaurar dependências deste repositório
dotnet restore MouseMapper.sln

# 4) Compilar
dotnet build MouseMapper.sln -c Release

# 5) Executar (somente Windows)
dotnet run --project src/MouseMapper/MouseMapper.csproj
```

---

## Gerar o `.exe` PORTABLE

```bash
dotnet publish src/MouseMapper/MouseMapper.csproj -c Release -r win-x64 --self-contained true ^
  /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

Ou use o script (já inclui `DebugType=None` e compressão):

```powershell
.\scripts\build-portable.ps1        # Windows
./scripts/build-portable.sh         # Linux/macOS (cross-compile)
```

Saída: `dist/MouseMapper.exe` (arquivo único, ~66 MB comprimido).

### O que cada flag faz

| Flag | Efeito |
| --- | --- |
| `-c Release` | Build otimizado, sem código de depuração. |
| `-r win-x64` | Runtime ID alvo: Windows 64 bits. Use `win-arm64` para ARM. |
| `--self-contained true` | Embute o runtime .NET no executável; o PC não precisa ter .NET instalado. |
| `/p:PublishSingleFile=true` | Empacota IL, recursos e dependências em um único `.exe`. |
| `/p:IncludeNativeLibrariesForSelfExtract=true` | Inclui as DLLs nativas (WPF/DirectX) e as extrai em tempo de execução — necessário para WPF single-file. |
| `-p:DebugType=None` | Não gera `.pdb` ao lado do executável. |
| `-p:EnableCompressionInSingleFile=true` | Comprime o payload (já ativo no `.csproj`); reduz o tamanho, com partida levemente mais lenta. |

> `PublishTrimmed` fica **desligado**: o WPF não é compatível com trimming.

### Ícone personalizado

O ícone já está configurado:

```xml
<ApplicationIcon>Assets/mousemapper.ico</ApplicationIcon>
...
<Resource Include="Assets/mousemapper.ico" />
```

- `ApplicationIcon` → ícone do `.exe` no Explorer.
- `Resource` → ícone da janela e da bandeja.

Para trocar: substitua `src/MouseMapper/Assets/mousemapper.ico` por um `.ico` com frames de
16, 32, 48, 256 px e rode `dotnet publish` novamente. O `scripts/generate-icon.py` regenera o
ícone atual (seta do cursor + ponto azul) sem dependências externas. Se o Windows continuar
mostrando o ícone antigo, limpe o cache: `ie4uinit.exe -show` (ou reinicie o Explorer).

---

## Como usar

1. Execute `MouseMapper.exe` e aceite o UAC (o manifesto pede administrador).
2. Clique em **“+ Mapear nova automação”**.
3. Digite um **nome** (ex.: `Preencher formulário`).
4. Clique na caixa de atalho → pressione a combinação (ex.: `Ctrl+Shift+1`).
   - Preview em tempo real das teclas.
   - **Verde:** atalho registrado com sucesso.
   - **Vermelho:** “Atalho em uso, escolha outro” (Windows devolveu 1409) ou já usado por outra macro.
   - `ESC` cancela a captura.
5. O botão **Gravar automação** só habilita com nome + atalho válido.
6. Ao gravar, a janela vai para a bandeja e aparece o balão
   “Gravação iniciada. Pressione `Ctrl+Shift+1` para parar”.
7. Faça os cliques/arrastos que deseja registrar.
8. Pressione **o mesmo atalho** para encerrar. A automação é salva, a janela volta e a lista atualiza.
9. Em qualquer aplicativo, pressione o atalho para reproduzir. Pressione-o novamente durante a
   reprodução para **cancelar** (ou use a bandeja → “Cancelar reprodução”).
10. O **X** minimiza para a bandeja. Para encerrar de verdade: ícone da bandeja → **Sair**.
    Hooks e hotkeys são liberados nesse momento.

Ações da lista: **Reproduzir** (executa na hora), **Editar** (renomear/alterar atalho ou
**Regravar**), **Excluir**.

### O que é gravado

- Clique esquerdo e direito (pressionar/soltar), botão do meio
- Duplo clique (detectado pelo intervalo e proximidade; consolidado como `doubleClick`)
- Arrastar e soltar (down + todos os `mouseMove` com botão pressionado + up)
- Scroll do mouse (`wheel`, delta em múltiplos de 120)
- Para cada evento: tipo, `x`, `y` (pixels absolutos do desktop virtual) e `timestamp` (ms)

Movimentos sem botão pressionado não são gravados (o clique já reposiciona o cursor), e os
movimentos durante o arrasto são limitados a ~125/s para o JSON não crescer demais.

---

## Formato do JSON

Arquivo: `automations.json`, na mesma pasta do `.exe`.

```json
{
  "version": 1,
  "automations": [
    {
      "id": "4d1f4e58-2a58-4c55-9b4e-5b7f1f3a1f10",
      "name": "Minha Macro 1",
      "hotkey": {
        "display": "Ctrl+Shift+1",
        "modifiers": 6,
        "virtualKey": 49
      },
      "createdAt": "2026-10-31T10:30:00+00:00",
      "events": [
        { "type": "leftDown", "x": 500, "y": 300, "timestamp": 0, "data": 0 },
        { "type": "leftUp", "x": 500, "y": 300, "timestamp": 84, "data": 0 },
        { "type": "mouseMove", "x": 640, "y": 410, "timestamp": 320, "data": 0 },
        { "type": "doubleClick", "x": 700, "y": 420, "timestamp": 900, "data": 0 }
      ]
    }
  ]
}
```

- `modifiers`: bitmask `Alt=1`, `Ctrl=2`, `Shift=4`, `Win=8` (mesma da API do Windows).
- `virtualKey`: código `VK_*` da tecla principal.
- `timestamp`: milissegundos desde o início da gravação; o primeiro evento é sempre `0`,
  o que torna os delays uma subtração simples.
- Escrita atômica (`.tmp` + move). Um JSON inválido é preservado como
  `automations.json.corrupt-<data>` e o app inicia vazio.

---

## Decisões de arquitetura (e por quê)

1. **Atalhos via `RegisterHotKey`, não apenas hook.** É a única forma de detectar conflito
   real (erro `1409`) e de reservar a combinação para o app, evitando disparo duplicado
   (hook + `WM_HOTKEY`) e evitando que o programa em foco também reaja à tecla.
2. **Hook de teclado na captura.** `WH_KEYBOARD_LL` é usado na janela de captura para preview
   em tempo real, funcionar mesmo sem foco e bloquear o vazamento das teclas (`return 1`).
3. **Bandeja com `NotifyIcon`.** O WPF não tem API de bandeja; o WinForms já faz parte do
   Windows Desktop Runtime, então não entra nenhum pacote NuGet — o `.exe` continua único.
4. **Duplo clique detectado por tempo/distância.** Hooks de baixo nível não entregam
   `WM_LBUTTONDBLCLK` de forma confiável; o gravador captura as primitivas e consolida o par
   em `doubleClick` usando `GetDoubleClickTime()` e tolerância de 4 px.
5. **Coordenadas absolutas do desktop virtual.** `SendInput` com `MOUSEEVENTF_ABSOLUTE |
   VIRTUALDESK` normaliza pelo *virtual screen*, o que funciona com múltiplos monitores.
6. **Delays relativos a um cronômetro absoluto.** O player calcula
   `alvo - tempoDecorrido`, então o erro não se acumula. A resolução do timer do Windows
   (~15 ms) limita a precisão de delays curtos.
7. **Instância única.** Duas cópias competiriam pelas mesmas hotkeys e pelos hooks; a segunda
   apenas avisa que o app já está na bandeja.
8. **Tudo ao lado do `.exe`.** `automations.json` e `logs/` ficam na pasta do executável.
   Se ela for somente leitura (pendrive protegido), o log cai em `%LOCALAPPDATA%\MouseMapper`.

---

## Permissões (UAC)

`app.manifest` usa `requireAdministrator`. Isso permite que hooks e `SendInput` alcancem
janelas elevadas (Gerenciador de Tarefas, instaladores, apps abertos como admin).

Se preferir **não** ter o prompt do UAC em cada abertura, troque no manifesto:

```xml
<requestedExecutionLevel level="asInvoker" uiAccess="false" />
```

Hooks e hotkeys continuam funcionando, mas **não** interagem com janelas de integridade mais
alta (proteção UIPI). O Windows também pode exigir que o app esteja em primeiro plano para
enviar input a alguns processos.

---

## Troubleshooting

### Antivírus / Windows Defender acusam o `.exe`

Falso positivo comum em executáveis single-file self-contained (o payload é extraído em
tempo de execução e o binário não é assinado).

1. **Windows Security** → *Virus & threat protection* → *Manage settings* →
   *Exclusions* → *Add or remove exclusions* → **File** → selecione `MouseMapper.exe`.
2. No Defender, se necessário: *Protection history* → encontre a detecção →
   *Actions* → **Allow on device**.
3. Para distribuição: assine o executável com um certificado de code signing e envie o
   binário em https://www.microsoft.com/wdsi/filesubmission para revisão.

### “Atalho em uso, escolha outro”

`RegisterHotKey` retornou `1409`. Outro programa já reservou a combinação (Discord, ShareX,
NVIDIA/GeForce Experience, OBS, Teams, driver de teclado etc.) ou ela está duplicada em outra
macro. Feche o outro app ou escolha outra combinação, por exemplo `Ctrl+Alt+F9`.

### A macro não reproduz

- Confira no rodapé/lista se o atalho está registrado (o app avisa no início quando algum falha).
- Veja o log: `logs/mousemapper.log` ao lado do `.exe`.
- A macro precisa ter eventos (a lista mostra “sem gravação”).
- Jogos com anti-cheat podem bloquear `SendInput` e hooks — não há suporte.

### Gravação não captura nada

- A janela precisa estar na bandeja durante a gravação (é o comportamento normal).
- O hook global pode ser bloqueado por segurança corporativa/anti-cheat.
- Verifique `logs/mousemapper.log` por “Hook WH_MOUSE_LL instalado” e erros.
- A gravação só termina com o atalho da própria macro (ou bandeja → “Parar gravação”).

### Coordenadas erradas / clique em lugar diferente

- O app é DPI-aware (`PerMonitorV2`) e grava pixels físicos. Se você mover a janela de destino
  ou mudar a resolução/escala dos monitores, **regrave** a macro.
- Evite misturar escalas muito diferentes entre monitores ao gravar arrastos longos.

### O ícone sumiu da bandeja

Se o Explorer reiniciar, ícones de bandeja podem desaparecer. Encerre pelo Gerenciador de
Tarefas e abra novamente (a segunda instância apenas avisa que já existe uma).

### “Não foi possível salvar as automações”

A pasta do executável não tem permissão de escrita. Mova o `.exe` para uma pasta do usuário
(ex.: `C:\Users\SeuNome\Tools\MouseMapper`) ou um pendrive com escrita liberada.

### Macro reproduz com pequeno atraso/variação

`Task.Delay` tem resolução prática de ~15 ms no Windows. Para cliques encadeados, isso é
normal e não deve afetar formulários; delays muito curtos (< 20 ms) podem variar.

---

## Limitações conhecidas

- Grava apenas **mouse** (a especificação não inclui teclado).
- Não há editor visual de eventos — para mudar a sequência, use **Editar → Regravar**.
- Atalhos são exclusivos enquanto o app roda (a tecla não chega ao app em foco).
- Coordenadas são absolutas: mudanças de layout/resolução exigem nova gravação.
- Durante a gravação, atalhos de outras macros são ignorados (a tecla ainda fica reservada).
- O build é validado em Linux (compilação/publicação); a execução real precisa de Windows.

---

## Licença

MIT — veja [LICENSE](LICENSE).
