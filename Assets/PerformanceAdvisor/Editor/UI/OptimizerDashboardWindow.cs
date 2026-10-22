using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Editor window that wires the three core modules to a 3-tab UI:
    /// Monitor (live telemetry + analyze button), Configuração (API key),
    /// Laudo (the IA report). Follows Hick's-law: at most 3 buttons visible
    /// at a time, no raw millisecond numbers shown — semaphore widget only.
    /// </summary>
    public class OptimizerDashboardWindow : EditorWindow
    {
        private enum Tab { Monitor, Build, Snapshots, Configuration, Report, Chat }

        // MOTIVO: 6 abas — quebra deliberada da regra Hick's-law de 3, justificada pelo
        // feedback do orientador (snapshots selecionáveis), pelo chat de dúvidas e
        // pela aba Build (seleção de hardware real no lugar dos presets abstratos).
        // MOTIVO: calculado a cada desenho, não estático — o idioma pode mudar com a janela aberta.
        private static string[] TabNames() => new[]
        {
            "Monitor", "Build", "Snapshots",
            Loc.T("Configuração", "Settings", "Configuración"),
            Loc.T("Laudo", "Report", "Informe"),
            "Chat"
        };

        private Tab _currentTab = Tab.Monitor;

        // ─── Monitor state ────────────────────────────────────────────────
        // MOTIVO: sem isso os campos ficam em 0 antes da primeira amostra e o semáforo
        // pinta VERDE com "OK · 0% do budget". Zero medição não é zero consumo — é
        // ausência de dado, e verde é a leitura mais errada possível dela. Mesmo defeito
        // do "0 snapshots", só que pintado de aprovação.
        private bool  _hasSample;
        private float _lastCpuMs;
        private long  _lastGcBytes;
        private int   _lastDrawCalls;
        private float _lastGpuMs;
        private int   _snapshotCount;

        // MOTIVO: o status guarda o ESTADO, não o texto. Texto guardado ficaria no idioma
        // de quando o evento aconteceu, mesmo depois de o dev trocar o idioma.
        private enum Status { Waiting, Monitoring, Stopped, Analyzing, Retrying, ReportReceived, Error }
        private Status _status = Status.Waiting;

        private string StatusText()
        {
            switch (_status)
            {
                case Status.Monitoring:     return Loc.T("Monitorando...", "Monitoring...", "Monitoreando...");
                case Status.Stopped:
                    return _snapshotCount > 0
                        ? Loc.T($"Parado — {_snapshotCount} snapshot(s) prontos para análise",
                                $"Stopped — {_snapshotCount} snapshot(s) ready for analysis",
                                $"Detenido — {_snapshotCount} snapshot(s) listos para analizar")
                        : Loc.T("Parado — sem anomalias detectadas", "Stopped — no anomalies detected",
                                "Detenido — sin anomalías detectadas");
                case Status.Analyzing:      return Loc.T("Analisando...", "Analyzing...", "Analizando...");
                case Status.Retrying:       return Loc.T("Tentando de novo...", "Retrying...", "Reintentando...");
                case Status.ReportReceived: return Loc.T("Laudo recebido", "Report received", "Informe recibido");
                case Status.Error:          return Loc.T("Erro na análise", "Analysis error", "Error en el análisis");
                default:                    return Loc.T("Aguardando Play Mode ou build", "Waiting for Play Mode or a build",
                                                         "Esperando el Play Mode o una build");
            }
        }

        // ─── Snapshots tab state ──────────────────────────────────────────
        // MOTIVO: armazenamos os Ids DESmarcados (não os marcados). Snapshots novos
        // entram marcados por default sem precisar tocar no set — alinhado com a
        // expectativa "tudo selecionado a princípio".
        private readonly System.Collections.Generic.HashSet<int> _deselectedIds = new System.Collections.Generic.HashSet<int>();
        // MOTIVO: o inverso do _deselectedIds — aqui guardamos os EXPANDIDOS, porque o
        // padrão é recolhido. Snapshot novo entra recolhido sem tocar no set.
        private readonly System.Collections.Generic.HashSet<int> _expandedIds = new System.Collections.Generic.HashSet<int>();
        private Vector2 _snapshotsScroll;
        // Aba de tipo ativa dentro da aba Snapshots.
        private int _kindTab;

        // ─── Configuration state ──────────────────────────────────────────
        private string _apiKeyInput = "";

        // ─── Report state ─────────────────────────────────────────────────
        private GeminiAdvisoryReport _lastReport;
        private string _lastError;
        // MOTIVO: feedback visual de requisição em voo — sem isso o clique em
        // "Analisar" parece não fazer nada até o laudo chegar (até 30s). Também
        // desabilita o botão pra evitar requisição duplicada.
        private bool _isAnalyzing;
        // Mensagem do fallback automático (preview 503 → estável), exibida enquanto
        // a re-tentativa está em voo. Null quando não houve fallback.
        private string _fallbackNotice;
        // Scrolls por finding, indexados. MOTIVO do 16 e não 8: com um finding por CAUSA
        // em vez de um por tipo, o laudo pode trazer bem mais entradas.
        // Acessos acima do tamanho já são guardados no DrawFinding.
        private readonly Vector2[] _codeScrolls = new Vector2[16];
        private readonly Vector2[] _explanationScrolls = new Vector2[16];
        private Vector2 _reportScroll;

        // ─── Chat state ───────────────────────────────────────────────────
        // MOTIVO: SerializeField pra o histórico do chat sobreviver a recompilações
        // dentro da sessão do Editor (zera ao fechar — decisão de design).
        [SerializeField] private ChatTab _chatTab = new ChatTab();

        // ─── Build state ──────────────────────────────────────────────────
        [SerializeField] private BuildTab _buildTab = new BuildTab();
        private long _lastMemBytes;

        // MOTIVO: priority=0 + separador depois (priority 100 nos builders) faz com que
        // "Open Dashboard" apareça no topo do submenu, separado dos builders abaixo.
        [MenuItem("Tools/Performance Advisor/Open Dashboard", false, 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<OptimizerDashboardWindow>("Performance Advisor");
            window.minSize = new Vector2(440, 520);
        }
        /// <summary>Brings the dashboard forward on the Monitor tab.</summary>
        /// <remarks>
        /// MOTIVO: a espera pela build saiu daqui para o PlayerAttachWatcher. Ela era um
        /// EditorApplication.update estático, e trocar de plataforma no build recarrega o
        /// domínio C# — o que apaga toda a estática do Editor e matava a espera em
        /// silêncio. No teste em Windows o player conectou com 1386 frames chegando e não
        /// havia mais ninguém olhando.
        ///
        /// Sobrou aqui só o que é da janela: trazer a aba certa para a frente.
        /// </remarks>
        public static void FocusMonitorTab()
        {
            var w = GetWindow<OptimizerDashboardWindow>("Performance Advisor");
            w._currentTab = Tab.Monitor;
            w.Repaint();
        }

        private void OnEnable()
        {
            if (_chatTab == null) _chatTab = new ChatTab();
            if (_buildTab == null) _buildTab = new BuildTab();
            _apiKeyInput = AiSettings.ApiKey;
            HardwareBuildSettings.OnBuildChanged += Repaint;
            Loc.Changed += Repaint;

            TelemetryMonitorCore.OnMonitoringStarted   += HandleMonitoringStarted;
            TelemetryMonitorCore.OnMonitoringStopped   += HandleMonitoringStopped;
            TelemetryMonitorCore.OnFrameSampled        += HandleFrameSampled;
            SnapshotHeuristicsController.OnSnapshotCaptured += HandleSnapshotCaptured;
            GeminiRestBridge.OnRequestStarted          += HandleAnalysisStarted;
            GeminiRestBridge.OnFallbackRetry           += HandleFallbackRetry;
            GeminiRestBridge.OnReportReceived          += HandleReportReceived;
            GeminiRestBridge.OnError                   += HandleAnalysisError;
        }

        private void OnDisable()
        {
            HardwareBuildSettings.OnBuildChanged       -= Repaint;
            Loc.Changed                                -= Repaint;
            TelemetryMonitorCore.OnMonitoringStarted   -= HandleMonitoringStarted;
            TelemetryMonitorCore.OnMonitoringStopped   -= HandleMonitoringStopped;
            TelemetryMonitorCore.OnFrameSampled        -= HandleFrameSampled;
            SnapshotHeuristicsController.OnSnapshotCaptured -= HandleSnapshotCaptured;
            GeminiRestBridge.OnRequestStarted          -= HandleAnalysisStarted;
            GeminiRestBridge.OnFallbackRetry           -= HandleFallbackRetry;
            GeminiRestBridge.OnReportReceived          -= HandleReportReceived;
            GeminiRestBridge.OnError                   -= HandleAnalysisError;
        }

        // ─── Event handlers ───────────────────────────────────────────────
        private void HandleMonitoringStarted()
        {
            _status = Status.Monitoring;
            // MOTIVO: sem zerar aqui, os valores da sessão ANTERIOR seguem na tela com
            // semáforo pintado enquanto a nova sessão ainda não produziu amostra — e o
            // dev leria o veredito de uma medição que não é mais a atual.
            _hasSample = false;
            HardwareBuildSettings.ClearAutoCeilingNotice();
            _snapshotCount = 0;
            _lastReport = null;
            _lastError = null;
            Repaint();
        }

        private void HandleMonitoringStopped()
        {
            _status = Status.Stopped;
            Repaint();
        }

        private void HandleFrameSampled(float cpuMs, long gcBytes, int drawCalls, long memBytes, float gpuMs)
        {
            _hasSample        = true;
            _lastCpuMs        = cpuMs;
            _lastGcBytes      = gcBytes;
            _lastDrawCalls    = drawCalls;
            _lastMemBytes     = memBytes;
            _lastGpuMs        = gpuMs;
            // No Repaint here — would spam the editor. The dashboard repaints
            // via `Update()` below at a comfortable rate.
        }

        private void HandleSnapshotCaptured(int total) { _snapshotCount = total; Repaint(); }

        private void HandleAnalysisStarted()
        {
            _status = Status.Analyzing;
            _lastError = null;
            _isAnalyzing = true;
            _fallbackNotice = null;
            Repaint();
        }

        private void HandleFallbackRetry(string msg)
        {
            _status = Status.Retrying;
            _fallbackNotice = msg;
            Repaint();
        }

        private void HandleReportReceived(GeminiAdvisoryReport report)
        {
            _lastReport = report;
            _status = Status.ReportReceived;
            _isAnalyzing = false;
            _currentTab = Tab.Report;
            Repaint();
        }

        private void HandleAnalysisError(string err)
        {
            _lastError = err;
            _status = Status.Error;
            _isAnalyzing = false;
            _currentTab = Tab.Report;
            Repaint();
        }

        // MOTIVO: Repaint at ~10Hz while in Play Mode so the semaphore bars
        // animate without saturating the Editor with redraws.
        private double _nextRepaintTime;
        private void Update()
        {
            // MOTIVO: a análise e o chat rodam fora do Play Mode — repinta também
            // enquanto há requisição em voo, senão os "..." animados congelam.
            // MOTIVO: monitorando na build o Editor NÃO está em Play, e sem este caso o
            // painel congelaria justamente durante a medição que interessa.
            if (!EditorApplication.isPlaying && !TelemetryMonitorCore.IsMonitoring
                && !_isAnalyzing && !_chatTab.IsBusy) return;
            if (EditorApplication.timeSinceStartup < _nextRepaintTime) return;
            _nextRepaintTime = EditorApplication.timeSinceStartup + 0.1;
            Repaint();
        }

        // ─── GUI ──────────────────────────────────────────────────────────
        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _currentTab = (Tab)GUILayout.Toolbar((int)_currentTab, TabNames());
                // MOTIVO: a ajuda fica ao lado das abas, sempre no mesmo lugar, e explica a
                // aba aberta — o dev não precisa procurar onde está a explicação de cada tela.
                if (GUILayout.Button(new GUIContent("ⓘ", Loc.T("O que esta tela faz", "What this screen does",
                                                              "Qué hace esta pantalla")),
                                     GUILayout.Width(28)))
                    TabHelp.Show((int)_currentTab);
            }
            EditorGUILayout.Space(8);

            switch (_currentTab)
            {
                case Tab.Monitor:       DrawMonitorTab();       break;
                case Tab.Build:         _buildTab.Draw();       break;
                case Tab.Snapshots:     DrawSnapshotsTab();     break;
                case Tab.Configuration: DrawConfigurationTab(); break;
                case Tab.Report:        DrawReportTab();        break;
                case Tab.Chat:          _chatTab.Draw(_lastReport, Repaint); break;
            }
        }

        private void DrawMonitorTab()
        {
            EditorGUILayout.LabelField(Loc.T("Status", "Status", "Estado"), StatusText(), EditorStyles.boldLabel);

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(Loc.T("Build alvo", "Target build", "Build objetivo"),
                                           HardwareBuildSettings.Description, EditorStyles.miniLabel);
                if (GUILayout.Button(Loc.T("Alterar →", "Change →", "Cambiar →"), GUILayout.Width(80)))
                {
                    _currentTab = Tab.Build;
                }
            }

            EditorGUILayout.Space(10);

            // Botão de toggle Monitorar — substitui o auto-start no EnteredPlayMode.
            // MOTIVO: feedback do orientador — o dev decide quando capturar telemetria.
            //
            // MOTIVO da condição: exigir Play Mode travava justamente o caso que a
            // medição em build existe para atender. No modo build o Editor NÃO está em
            // Play — quem roda é a build, numa janela separada ou no aparelho. O botão
            // libera quando há Play OU quando há player conectado ao Profiler.
            bool isPlaying    = EditorApplication.isPlaying;
            bool hasPlayer    = TelemetryMonitorCore.HasConnectedPlayer();
            bool isMonitoring = TelemetryMonitorCore.IsMonitoring;
            bool podeMonitorar = isPlaying || hasPlayer;

            using (new EditorGUI.DisabledScope(!podeMonitorar))
            {
                string alvo = hasPlayer && !isPlaying ? $" — {TelemetryMonitorCore.RemoteTargetName}" : "";
                string label = isMonitoring
                    ? Loc.T("⏸ Parar monitoramento", "⏸ Stop monitoring", "⏸ Detener monitoreo")
                    : Loc.T($"▶ Monitorar{alvo}", $"▶ Monitor{alvo}", $"▶ Monitorear{alvo}");
                if (GUILayout.Button(label, GUILayout.Height(26)))
                {
                    if (isMonitoring) TelemetryMonitorCore.StopMonitoring();
                    else              TelemetryMonitorCore.StartMonitoring();
                }
            }

            // MOTIVO: sem isso, "botão cinza" era a única informação, e as duas causas
            // (não está em Play / não há build conectada) pedem ações opostas.
            if (!podeMonitorar)
            {
                var b = HardwareBuildSettings.GetActiveCopy();
                EditorGUILayout.HelpBox(
                    b.Test == TestMode.NaBuild
                        ? Loc.T("Nenhuma build conectada. Vá em Build e clique em \"Compilar e executar\" — " +
                                "o Monitor liga sozinho quando ela aparecer.",
                                "No build connected. Go to Build and click \"Build and run\" — " +
                                "the Monitor starts on its own when the build shows up.",
                                "No hay ninguna build conectada. Ve a Build y haz clic en \"Compilar y ejecutar\": " +
                                "el Monitor se activa solo cuando aparezca.")
                        : Loc.T("Entre em Play para monitorar, ou troque para \"Na build\" na aba Build.",
                                "Enter Play Mode to monitor, or switch to \"On the build\" in the Build tab.",
                                "Entra en Play Mode para monitorear, o cambia a \"En la build\" en la pestaña Build."),
                    MessageType.Info);

                // MOTIVO: escape manual. A conexão automática já falhou de dois jeitos
                // diferentes no teste real — domain reload matando a espera, e a Unity
                // não trocando o alvo do Profiler no Android. Os dois foram corrigidos,
                // mas "o automático falhou" não pode significar "não há mais o que
                // fazer": se a build ESTÁ rodando e o plugin não a vê, este botão procura
                // de novo, agora.
                if (b.Test == TestMode.NaBuild)
                {
                    if (GUILayout.Button(Loc.T("Procurar build já em execução", "Look for a build already running",
                                               "Buscar una build ya en ejecución"), GUILayout.Height(22)))
                    {
                        if (PlayerAttachWatcher.TrySelectPlayerTarget())
                        {
                            TelemetryMonitorCore.StartMonitoring();
                            Repaint();
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("Performance Advisor",
                                Loc.T("Nenhuma build encontrada no Profiler.\n\nConexões que o plugin está vendo agora:\n",
                                      "No build found in the Profiler.\n\nConnections the plugin can see right now:\n",
                                      "No se encontró ninguna build en el Profiler.\n\nConexiones que el plugin ve ahora:\n") +
                                PlayerAttachWatcher.DescribeAvailableTargets() + "\n" +
                                Loc.T("Se a sua build aparece aí, o problema é a regra de detecção. " +
                                      "Se não aparece, confirme que ela está executando; em Android, " +
                                      "confira também 'adb devices'.",
                                      "If your build is listed, the problem is the detection rule. " +
                                      "If it is not, make sure it is running; on Android, also check 'adb devices'.",
                                      "Si tu build aparece en la lista, el problema es la regla de detección. " +
                                      "Si no aparece, confirma que se está ejecutando; en Android, revisa también 'adb devices'."),
                                "Ok");
                        }
                    }
                }
            }

            EditorGUILayout.Space(8);

            float cpuBudget  = HardwareBuildSettings.CpuBudgetMs;
            int   drawBudget = HardwareBuildSettings.DrawCallBudget;
            // ── VEREDITO ─────────────────────────────────────────────────
            // MOTIVO: só entram aqui grandezas em TEMPO ou BYTES, projetadas para o alvo.
            // É a pergunta que o plugin existe para responder — "cabe no orçamento?" —
            // e ela não se responde com contagem.
            float cpuFactor = HardwareBuildSettings.CpuProjectionFactor;
            float gpuFactor = HardwareBuildSettings.GpuProjectionFactor;

            // MOTIVO: descartar frames em silêncio seria trocar um número errado por uma
            // ausência inexplicada — o dev veria "0 snapshots" nos primeiros segundos e
            // não saberia se está quebrado. O aquecimento aparece enquanto acontece.
            if (TelemetryMonitorCore.IsMonitoring && SnapshotHeuristicsController.IsWarmingUp)
            {
                float left = SnapshotHeuristicsController.WarmupSecondsRemaining;
                NoticeBox.Draw(Loc.T(
                    $"Aquecendo — ignorando os primeiros 2 s ({left:F1} s restantes). Carga de cena e " +
                    "warm-up de shader produzem picos que são custo de ABRIR o jogo, não de rodá-lo; " +
                    "capturá-los faria o laudo diagnosticar o loading.",
                    $"Warming up — ignoring the first 2 s ({left:F1} s left). Scene loading and shader " +
                    "warm-up cause spikes that are the cost of OPENING the game, not of running it; " +
                    "capturing them would make the report diagnose the loading screen.",
                    $"Calentando — ignorando los primeros 2 s ({left:F1} s restantes). La carga de escena y " +
                    "el warm-up de shaders producen picos que son el costo de ABRIR el juego, no de jugarlo; " +
                    "capturarlos haría que el informe diagnostique la pantalla de carga."),
                    NoticeLevel.Info);
            }

            // MOTIVO: o teto automático é definido no Stop. Uma linha, uma vez, dizendo que
            // aconteceu e o que significa — senão o semáforo de memória aparece na próxima
            // sessão com um teto que o dev não lembra de ter posto.
            if (!string.IsNullOrEmpty(HardwareBuildSettings.AutoCeilingNotice))
                NoticeBox.Draw(HardwareBuildSettings.AutoCeilingNotice, NoticeLevel.Success);

            DrawCollectionGuard(_hasSample);
            EditorGUILayout.Space(4);

            EditorGUILayout.LabelField(Loc.T("Veredito no hardware alvo", "Verdict on the target hardware",
                                             "Veredicto en el hardware objetivo"), EditorStyles.boldLabel);
            string semMedicao = Loc.T("sem medição ainda", "no measurement yet", "sin medición todavía");
            string semIndice  = Loc.T("sem índice que projete para este alvo", "no index to project onto this target",
                                      "sin índice que proyecte a este objetivo");
            string memoria    = Loc.T("Memória", "Memory", "Memoria");

            // MOTIVO: sem NENHUMA amostra, os campos ficavam em 0 e o semáforo pintava
            // verde com "OK · 0% do budget". Zero medição não é zero consumo — é ausência
            // de dado, e verde é a leitura mais errada possível dela: passa por aprovação
            // exatamente como o "0 snapshots" fazia. Enquanto não houver amostra, os
            // slots dizem que não houve.
            if (!_hasSample)
            {
                DrawNoVerdict("CPU", semMedicao);
                DrawNoVerdict("GPU", semMedicao);
                DrawNoVerdict(memoria, semMedicao);
            }
            else
            {
                if (cpuFactor > 0f) DrawSemaphore("CPU", _lastCpuMs * cpuFactor, cpuBudget);
                else                DrawNoVerdict("CPU", semIndice);

                if (gpuFactor > 0f) DrawSemaphore("GPU", _lastGpuMs * gpuFactor, cpuBudget);
                else                DrawNoVerdict("GPU", TelemetryMonitorCore.RemoteGpuResolved
                                        ? semIndice
                                        : Loc.T("contador de GPU ausente neste aparelho",
                                                "GPU counter not available on this device",
                                                "contador de GPU ausente en este dispositivo"));

                // MOTIVO: memória só recebe semáforo quando há teto com procedência. E o
                // motivo da ausência NÃO é o mesmo de CPU/GPU — memória não se projeta,
                // ela se compara a um teto. Dizer "sem projeção" aqui explicava a coisa
                // errada e mandava o dev procurar um índice que não resolveria nada.
                long memBudget = HardwareBuildSettings.MemoryBudgetBytes;
                if (memBudget > 0)
                    DrawSemaphore(memoria, _lastMemBytes, memBudget);
                else
                    DrawNoVerdict(memoria, MemoryNoVerdictReason());
            }

            // ── DIAGNÓSTICO ──────────────────────────────────────────────
            // Explica DE ONDE vem o tempo que o veredito julga. Quem não tem limiar com
            // procedência aparece sem cor, em vez de exibir um teto inventado.
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(Loc.T("Diagnóstico", "Diagnostics", "Diagnóstico"), EditorStyles.boldLabel);

            if (!_hasSample)
            {
                DrawReadingOnly("Draw Calls", "—", semMedicao);
                DrawReadingOnly("GC", "—", semMedicao);
            }
            else
            {
                if (drawBudget > 0) DrawSemaphore("Draw Calls", _lastDrawCalls, drawBudget);
                else                DrawReadingOnly("Draw Calls", _lastDrawCalls.ToString(),
                                                    Loc.T("sem calibração", "not calibrated", "sin calibración"));

                // MOTIVO: GC aparece como leitura, sem cor. O custo de uma coleta já está
                // dentro do tempo de CPU, que é portão — alocação é indicador antecedente,
                // não veredito. Ver o gatilho de captura no Detetive.
                // MOTIVO: contador de GC ausente no aparelho aparecia como "0 KB/frame", que
                // se lê como "não aloca nada". Ausência é "—", não zero.
                if (TelemetryMonitorCore.RemoteGcResolved)
                    DrawReadingOnly("GC",
                        Loc.T($"{_lastGcBytes / 1024f:F0} KB/frame · coleta {TelemetryMonitorCore.LastGcCollectMs:F2} ms",
                              $"{_lastGcBytes / 1024f:F0} KB/frame · collection {TelemetryMonitorCore.LastGcCollectMs:F2} ms",
                              $"{_lastGcBytes / 1024f:F0} KB/frame · recolección {TelemetryMonitorCore.LastGcCollectMs:F2} ms"),
                        Loc.T("explica picos de CPU", "explains CPU spikes", "explica picos de CPU"));
                else
                    DrawReadingOnly("GC", "—", Loc.T("contador ausente neste aparelho", "counter not available on this device",
                                                     "contador ausente en este dispositivo"));
            }

            // MOTIVO: o contador de draw calls varia por render pipeline e o plugin
            // escolhe um em tempo de execução. Sem declarar qual foi escolhido, o dev
            // não tem como saber se o número na tela é draw call, batch ou outra coisa —
            // que era exatamente o defeito apontado na revisão do orientador.
            if (isMonitoring)
            {
                EditorGUILayout.LabelField(
                    " ",
                    $"draw calls: {TelemetryMonitorCore.DrawCallsCounterName} · " +
                    Loc.T("caminho dominante: ", "dominant path: ", "ruta dominante: ") +
                    TelemetryMonitorCore.DominantDrawCallSource,
                    EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    " ",
                    // MOTIVO: o rótulo era fixo em "processo do Editor" e passaria a
                    // mentir assim que a leitura remota ligasse. Ele diz a fonte REAL.
                    Loc.T("memória: ", "memory: ", "memoria: ") + TelemetryMonitorCore.MemoryCounterName + " " +
                    (TelemetryMonitorCore.IsRemote
                        ? Loc.T($"(processo do jogo em {TelemetryMonitorCore.SessionTargetName})",
                                $"(game process on {TelemetryMonitorCore.SessionTargetName})",
                                $"(proceso del juego en {TelemetryMonitorCore.SessionTargetName})")
                        : Loc.T("(processo do Editor, não da build)", "(Editor process, not the build)",
                                "(proceso del Editor, no de la build)")),
                    EditorStyles.miniLabel);
            }

            // MOTIVO: no caminho remoto o plugin NÃO controla quais contadores existem —
            // eles mudam por plataforma e pipeline. Os 11 de draw call foram descobertos
            // no Windows/DX12; no Android com Vulkan a lista pode ser outra, e aí a soma
            // dá 0 sem nenhum erro aparecer. "0 draw calls" se lê como cena vazia, não
            // como falha de leitura — e o aparelho não está aqui para conferir a olho.
            //
            // Este bloco é o que impede esse silêncio: diz quantos contadores o aparelho
            // de fato expôs e manda rodar a sonda quando nenhum respondeu.
            if (TelemetryMonitorCore.IsRemote)
            {
                int resolved = TelemetryMonitorCore.RemoteDrawCountersResolved;

                if (resolved == 0)
                {
                    NoticeBox.Draw(Loc.T(
                        "Nenhum contador de draw call foi reconhecido neste aparelho — os nomes mudam " +
                        "por plataforma e pipeline. O valor mostrado NÃO é uma contagem real. " +
                        "Rode Tools → Performance Advisor → Diagnostics → Probe connected device counters " +
                        "para descobrir os nomes corretos.",
                        "No draw-call counter was recognized on this device — names change by platform " +
                        "and pipeline. The value shown is NOT a real count. Run Tools → Performance Advisor → " +
                        "Diagnostics → Probe connected device counters to find the correct names.",
                        "No se reconoció ningún contador de draw calls en este dispositivo: los nombres cambian " +
                        "según la plataforma y el pipeline. El valor mostrado NO es un conteo real. Ejecuta " +
                        "Tools → Performance Advisor → Diagnostics → Probe connected device counters para " +
                        "descubrir los nombres correctos."),
                        NoticeLevel.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        " ",
                        Loc.T($"contadores de draw call reconhecidos neste aparelho: {resolved} de {TelemetryMonitorCore.DrawCallCounterCount}",
                              $"draw-call counters recognized on this device: {resolved} of {TelemetryMonitorCore.DrawCallCounterCount}",
                              $"contadores de draw calls reconocidos en este dispositivo: {resolved} de {TelemetryMonitorCore.DrawCallCounterCount}"),
                        EditorStyles.miniLabel);
                }

                // MOTIVO: um resumo do que o aparelho NÃO expôs, numa linha só. Sem ele o dev
                // teria que cruzar três lugares da tela para descobrir que um "—" é ausência
                // de contador e não defeito da medição.
                string ausentes = "";
                if (!TelemetryMonitorCore.RemoteGpuResolved) ausentes += "GPU  ";
                if (!TelemetryMonitorCore.RemoteGcResolved)  ausentes += "GC  ";
                if (!TelemetryMonitorCore.RemoteMemResolved) ausentes += memoria.ToLowerInvariant() + "  ";
                if (ausentes.Length > 0)
                    EditorGUILayout.LabelField(
                        " ",
                        Loc.T($"contadores ausentes neste aparelho: {ausentes.Trim()} — sem veredito para eles",
                              $"counters missing on this device: {ausentes.Trim()} — no verdict for them",
                              $"contadores ausentes en este dispositivo: {ausentes.Trim()} — sin veredicto para ellos"),
                        EditorStyles.miniLabel);

                // MOTIVO: o desconto das esperas muda o número de CPU, e muito num celular
                // limitado a 30 FPS. Mostrar quanto foi descontado deixa o dev conferir
                // contra a Timeline do Profiler (WaitForTargetFPS / Gfx.WaitForPresent).
                if (TelemetryMonitorCore.RemoteCpuFromRoot)
                    EditorGUILayout.LabelField(
                        " ",
                        Loc.T($"CPU: duração do frame menos as esperas de limite de FPS e de tela (último frame: " +
                              $"{TelemetryMonitorCore.LastWaitMs:F1} ms de espera descontados). O contador \"Main Thread\" não existe neste alvo.",
                              $"CPU: frame duration minus the waits for the FPS cap and for presenting (last frame: " +
                              $"{TelemetryMonitorCore.LastWaitMs:F1} ms of waiting removed). The \"Main Thread\" counter does not exist on this target.",
                              $"CPU: duración del frame menos las esperas del límite de FPS y de presentación (último frame: " +
                              $"{TelemetryMonitorCore.LastWaitMs:F1} ms de espera descontados). El contador \"Main Thread\" no existe en este objetivo."),
                        EditorStyles.wordWrappedMiniLabel);

                // MOTIVO: frames que o Profiler descartou antes de o plugin ler. Se houver,
                // a contagem de violações é um piso, não o total — e o dev precisa saber.
                if (TelemetryMonitorCore.RemoteFramesLost > 0)
                    EditorGUILayout.LabelField(
                        " ",
                        Loc.T($"{TelemetryMonitorCore.RemoteFramesLost} frame(s) perdidos antes da leitura (Editor travado por " +
                              "mais tempo que o histórico do Profiler) — as contagens são mínimas.",
                              $"{TelemetryMonitorCore.RemoteFramesLost} frame(s) lost before being read (the Editor was blocked " +
                              "longer than the Profiler history) — the counts are a minimum.",
                              $"{TelemetryMonitorCore.RemoteFramesLost} frame(s) perdidos antes de la lectura (el Editor estuvo " +
                              "bloqueado más tiempo que el historial del Profiler) — los conteos son mínimos."),
                        EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(
                Loc.T("Snapshots capturados", "Captured snapshots", "Snapshots capturados"),
                $"{_snapshotCount}  (CPU: {SnapshotHeuristicsController.CpuCount}  •  " +
                $"GC: {SnapshotHeuristicsController.GcCount}  •  " +
                $"Draw: {SnapshotHeuristicsController.DrawCount}  •  " +
                $"GPU: {SnapshotHeuristicsController.GpuCount}  •  " +
                $"Mem: {SnapshotHeuristicsController.MemCount})");

            EditorGUILayout.Space(8);
            // MOTIVO: o CTA "Analisar" vive agora exclusivamente na aba Snapshots —
            // força o dev a revisar a lista antes de gastar quota da API (item 4 do
            // feedback do orientador).
            if (_snapshotCount > 0)
            {
                NoticeBox.Draw(Loc.T("Vá para a aba Snapshots para revisar e analisar.",
                                     "Go to the Snapshots tab to review and analyze.",
                                     "Ve a la pestaña Snapshots para revisar y analizar."), NoticeLevel.Success);
                if (GUILayout.Button(Loc.T("Ir para Snapshots →", "Go to Snapshots →", "Ir a Snapshots →"), GUILayout.Height(26)))
                {
                    _currentTab = Tab.Snapshots;
                }
            }
            else if (!isPlaying && !TelemetryMonitorCore.IsMonitoring)
                // MOTIVO: "Entre em Play" era a única instrução, e está errada no modo
                // build — lá o Editor não entra em Play nunca. A dica segue o modo.
                NoticeBox.Draw(HardwareBuildSettings.ActiveTestMode == TestMode.NaBuild
                    ? Loc.T("Compile e execute pela aba Build — o Monitor liga sozinho quando a build conectar.",
                            "Build and run from the Build tab — the Monitor starts on its own when the build connects.",
                            "Compila y ejecuta desde la pestaña Build: el Monitor se activa solo cuando la build se conecte.")
                    : Loc.T("Entre em Play e clique em Monitorar para capturar.",
                            "Enter Play Mode and click Monitor to capture.",
                            "Entra en Play Mode y haz clic en Monitorear para capturar."));
        }

        // ─── Snapshots tab ────────────────────────────────────────────────
        private void DrawSnapshotsTab()
        {
            var all = SnapshotHeuristicsController.CopyAllSamples();

            EditorGUILayout.LabelField(Loc.T($"{all.Length} snapshot(s) capturado(s)", $"{all.Length} snapshot(s) captured",
                                             $"{all.Length} snapshot(s) capturado(s)"), EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            if (all.Length == 0)
            {
                EditorGUILayout.HelpBox(Loc.T(
                    "Sem snapshots ainda. Monitore uma sessão (no Editor ou na build) para capturar anomalias.",
                    "No snapshots yet. Monitor a session (in the Editor or on a build) to capture anomalies.",
                    "Todavía no hay snapshots. Monitorea una sesión (en el Editor o en una build) para capturar anomalías."),
                    MessageType.Info);
                return;
            }

            // Header: Marcar todos / Desmarcar todos
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Loc.T("Marcar todos", "Select all", "Marcar todos"), GUILayout.Height(22)))
                {
                    _deselectedIds.Clear();
                }
                if (GUILayout.Button(Loc.T("Desmarcar todos", "Deselect all", "Desmarcar todos"), GUILayout.Height(22)))
                {
                    _deselectedIds.Clear();
                    for (int i = 0; i < all.Length; i++) _deselectedIds.Add(all[i].Id);
                }
            }

            EditorGUILayout.Space(6);

            // ── Abas por tipo, espelhando os semáforos do Monitor ────────
            // MOTIVO: uma lista única misturando os 6 tipos obriga o dev a garimpar. A
            // divisão por tipo é a mesma que ele já vê no Monitor, então não há conceito
            // novo a aprender — só o mesmo recorte, aplicado à lista.
            _kindTab = GUILayout.Toolbar(_kindTab, BuildKindTabLabels());
            SnapshotKind selectedKind = KindTabs[_kindTab];

            EditorGUILayout.Space(4);

            // ── Lista agrupada por causa raiz ────────────────────────────
            // MOTIVO: os 10 snapshots de um tipo costumam ser o MESMO problema repetido.
            // Agrupar por culpado mostra quantas causas distintas existem de verdade, e
            // é esse agrupamento que depois define quantos findings o laudo deve trazer.
            var ofKind = FilterByKind(all, selectedKind);
            var groups = SnapshotGrouping.Group(ofKind);

            _snapshotsScroll = EditorGUILayout.BeginScrollView(_snapshotsScroll);
            if (groups.Count == 0)
            {
                EditorGUILayout.LabelField(Loc.T("Nenhum snapshot deste tipo nesta sessão.", "No snapshots of this type in this session.",
                                                 "No hay snapshots de este tipo en esta sesión."), EditorStyles.miniLabel);
            }
            else
            {
                for (int i = 0; i < groups.Count; i++) DrawGroupRow(groups[i]);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);

            // MOTIVO: contar frames crus aqui mentia — a lista mostra GRUPOS e é o
            // representante de cada grupo que vai à IA. Dizer "50 / 50" quando o envio
            // leva 6 causas dá ao dev a impressão errada do tamanho da análise.
            int totalCauses    = CountAllCauses(all);
            int selectedCount  = CountSelectedCauses(all);
            EditorGUILayout.LabelField(
                Loc.T($"Causas selecionadas: {selectedCount} / {totalCauses}", $"Selected causes: {selectedCount} / {totalCauses}",
                      $"Causas seleccionadas: {selectedCount} / {totalCauses}"),
                Loc.T($"({all.Length} frames capturados)", $"({all.Length} frames captured)", $"({all.Length} frames capturados)"));

            // MOTIVO: a condição era "não está em Play". Medindo na build o Editor NUNCA
            // está em Play, então analisar ficava liberado NO MEIO da sessão — o laudo
            // sairia sobre uma coleta ainda em curso. O que precisa ter parado é a
            // MEDIÇÃO, não o Play Mode.
            bool canAnalyze = selectedCount > 0 && !EditorApplication.isPlaying
                              && !TelemetryMonitorCore.IsMonitoring
                              && AiSettings.IsReady && !_isAnalyzing;
            using (new EditorGUI.DisabledScope(!canAnalyze))
            {
                string cta = _isAnalyzing
                    ? Loc.T("Analisando", "Analyzing", "Analizando") + AnimatedDots()
                    : Loc.T("Analisar selecionados com IA", "Analyze selected with AI", "Analizar seleccionados con IA");
                if (GUILayout.Button(cta, GUILayout.Height(32)))
                {
                    SendSelectedSnapshots(all);
                }
            }

            if (_isAnalyzing)
                NoticeBox.Draw(
                    _fallbackNotice ?? Loc.T(
                    $"Snapshots enviados para {AiSettings.DisplayName(AiSettings.Provider)} — aguarde a resposta da IA. " +
                    "O laudo abre automaticamente na aba Laudo.",
                    $"Snapshots sent to {AiSettings.DisplayName(AiSettings.Provider)} — waiting for the AI's answer. " +
                    "The report opens automatically in the Report tab.",
                    $"Snapshots enviados a {AiSettings.DisplayName(AiSettings.Provider)} — espera la respuesta de la IA. " +
                    "El informe se abre automáticamente en la pestaña Informe."),
                    _fallbackNotice != null ? NoticeLevel.Warning : NoticeLevel.Info);
            else if (!AiSettings.IsReady)
                NoticeBox.Draw(Loc.T("Configure o provedor de IA e o modelo na aba Configuração.",
                                     "Set up the AI provider and model in the Settings tab.",
                                     "Configura el proveedor de IA y el modelo en la pestaña Configuración."), NoticeLevel.Warning);
            else if (EditorApplication.isPlaying)
                NoticeBox.Draw(Loc.T("Aperte Stop antes de analisar.", "Press Stop before analyzing.",
                                     "Pulsa Stop antes de analizar."), NoticeLevel.Warning);
            else if (TelemetryMonitorCore.IsMonitoring)
                NoticeBox.Draw(Loc.T("Pare o monitoramento antes de analisar — a coleta ainda está em curso.",
                                     "Stop monitoring before analyzing — data is still being collected.",
                                     "Detén el monitoreo antes de analizar: la recolección todavía está en curso."),
                    NoticeLevel.Warning);
            else if (selectedCount == 0)
                NoticeBox.Draw(Loc.T("Selecione pelo menos um snapshot.", "Select at least one snapshot.",
                                     "Selecciona al menos un snapshot."), NoticeLevel.Warning);
        }

        // Ordem das abas = ordem dos semáforos no Monitor, para não inventar um segundo
        // arranjo mental.
        private static readonly SnapshotKind[] KindTabs =
        {
            SnapshotKind.Cpu, SnapshotKind.Gpu, SnapshotKind.Memory,
            SnapshotKind.DrawCalls, SnapshotKind.Gc
        };

        private string[] BuildKindTabLabels()
        {
            var labels = new string[KindTabs.Length];
            for (int i = 0; i < KindTabs.Length; i++)
                labels[i] = $"{ShortKindName(KindTabs[i])} ({CountOfKind(KindTabs[i])})";
            return labels;
        }

        private static string ShortKindName(SnapshotKind k)
        {
            switch (k)
            {
                case SnapshotKind.Cpu:       return "CPU";
                case SnapshotKind.Gpu:       return "GPU";
                case SnapshotKind.Memory:    return "Mem";
                case SnapshotKind.DrawCalls: return "Draw";
                case SnapshotKind.Gc:        return "GC";
                default:                      return k.ToString();
            }
        }

        private static int CountOfKind(SnapshotKind k)
        {
            switch (k)
            {
                case SnapshotKind.Cpu:       return SnapshotHeuristicsController.CpuCount;
                case SnapshotKind.Gpu:       return SnapshotHeuristicsController.GpuCount;
                case SnapshotKind.Memory:    return SnapshotHeuristicsController.MemCount;
                case SnapshotKind.DrawCalls: return SnapshotHeuristicsController.DrawCount;
                case SnapshotKind.Gc:        return SnapshotHeuristicsController.GcCount;
                default:                      return 0;
            }
        }

        private static FrameSnapshot[] FilterByKind(FrameSnapshot[] all, SnapshotKind kind)
        {
            int n = 0;
            for (int i = 0; i < all.Length; i++) if (all[i].Kind == kind) n++;

            var result = new FrameSnapshot[n];
            int w = 0;
            for (int i = 0; i < all.Length; i++) if (all[i].Kind == kind) result[w++] = all[i];
            return result;
        }

        /// <summary>
        /// One distinct root cause: the worst frame of the group, plus how many frames
        /// showed the same thing.
        /// </summary>
        /// <remarks>
        /// MOTIVO: a seleção continua sendo pelo Id do representante, que é quem vai ao
        /// LLM. Marcar o grupo marca a causa inteira — é a unidade que o dev raciocina,
        /// e não o frame individual.
        /// </remarks>
        private void DrawGroupRow(SnapshotGroup g)
        {
            DrawSnapshotRow(g.Representative, g);
        }

        private void DrawSnapshotRow(FrameSnapshot s, SnapshotGroup? group = null)
        {
            bool expanded = _expandedIds.Contains(s.Id);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool wasSelected = !_deselectedIds.Contains(s.Id);
                    bool nowSelected = EditorGUILayout.ToggleLeft(GUIContent.none, wasSelected, GUILayout.Width(18));
                    if (nowSelected != wasSelected)
                    {
                        // MOTIVO: marca/desmarca o GRUPO INTEIRO, não só o representante.
                        // Antes, desmarcar um grupo tirava um Id e deixava os outros 9
                        // frames selecionados — o agrupamento rodava de novo no envio,
                        // elegia um novo representante entre eles, e o tipo era analisado
                        // mesmo tendo sido desmarcado na tela.
                        var ids = (group.HasValue && group.Value.MemberIds != null)
                            ? group.Value.MemberIds
                            : null;

                        if (ids == null)
                        {
                            if (nowSelected) _deselectedIds.Remove(s.Id);
                            else             _deselectedIds.Add(s.Id);
                        }
                        else
                        {
                            for (int i = 0; i < ids.Count; i++)
                            {
                                if (nowSelected) _deselectedIds.Remove(ids[i]);
                                else             _deselectedIds.Add(ids[i]);
                            }
                        }
                    }

                    string icon   = GetKindIcon(s.Kind);
                    string metric = FormatMetric(s);
                    string stack  = FirstLine(s.CulpritCallStack);

                    using (new EditorGUILayout.VerticalScope())
                    {
                        string occurrences = (group.HasValue && group.Value.Occurrences > 1)
                            ? $"  —  {group.Value.Occurrences} frames ({group.Value.FirstFrame}–{group.Value.LastFrame})"
                            : "";
                        EditorGUILayout.LabelField($"{icon}  frame {s.FrameIndex}  •  {metric}{occurrences}", EditorStyles.miniBoldLabel);
                        // Recolhido mostra só a 1ª linha do stack; o resto vai no detalhe.
                        if (!expanded && !string.IsNullOrEmpty(stack))
                            EditorGUILayout.LabelField(stack, EditorStyles.miniLabel);
                    }

                    if (GUILayout.Button(expanded ? "▾" : "▸", EditorStyles.miniButton, GUILayout.Width(24)))
                    {
                        if (expanded) _expandedIds.Remove(s.Id);
                        else          _expandedIds.Add(s.Id);
                    }
                }

                if (expanded) DrawSnapshotDetails(s);
            }
        }

        /// <summary>
        /// Drill-down of a single snapshot: the whole frame, not just the metric that tripped.
        /// </summary>
        /// <remarks>
        /// MOTIVO: o struct já carregava as 5 métricas do frame e o stack completo dos 3
        /// métodos mais caros, mas a lista só exibia a métrica violada e a 1ª linha do
        /// stack — dois terços do que o Profiler coletou eram descartados na exibição.
        ///
        /// Aqui números crus são o objetivo, ao contrário dos semáforos do Monitor: a
        /// regra de "não mostrar ms cru" existe pra não sobrecarregar a leitura rápida,
        /// e este painel é justamente o passo seguinte, quando o dev já decidiu investigar.
        /// </remarks>
        private static void DrawSnapshotDetails(FrameSnapshot s)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(Loc.T("Todas as métricas deste frame", "All metrics of this frame",
                                             "Todas las métricas de este frame"), EditorStyles.miniBoldLabel);

            // MOTIVO: CPU projetada no alvo, igual à GPU logo abaixo e ao que vai no laudo.
            // A linha mostrava o tempo cru desta máquina contra o orçamento do alvo.
            DrawMetricLine("CPU",        GeminiRestBridge.OnTarget(s.CpuLatencyMs, HardwareBuildSettings.CpuProjectionFactor),
                                         HardwareBuildSettings.CpuProjectionFactor > 0f
                                             ? HardwareBuildSettings.CpuBudgetMs : 0.0,
                                         "ms", s.Kind == SnapshotKind.Cpu);
            // Budget 0 = sem limiar: o GC não julga, explica. Ver o Detetive.
            DrawMetricLine("GC",         s.GcAllocatedBytes / 1024.0, 0.0, "KB", s.Kind == SnapshotKind.Gc,
                           FormatBytes(s.GcAllocatedBytes));
            // MOTIVO: o custo real do GC é o tempo de coleta, não os bytes alocados. Sem
            // esta linha o snapshot de GC mostrava só bytes, e 24 B num frame de 33 ms
            // pareciam a causa.
            EditorGUILayout.LabelField(Loc.T("     coleta de lixo", "     garbage collection", "     recolección de basura"),
                Loc.T($"{s.GcCollectMs:F2} ms   (tempo medido da coleta neste frame)",
                      $"{s.GcCollectMs:F2} ms   (measured collection time in this frame)",
                      $"{s.GcCollectMs:F2} ms   (tiempo medido de recolección en este frame)"), EditorStyles.miniLabel);
            DrawMetricLine("Draw Calls", s.DrawCallsCount,    HardwareBuildSettings.DrawCallBudget,  "",   s.Kind == SnapshotKind.DrawCalls);
            // GPU projetada no alvo; fator 0 = sem projeção, e aí não há budget a comparar.
            DrawMetricLine("GPU",        s.GpuMs * HardwareBuildSettings.GpuProjectionFactor,
                                         HardwareBuildSettings.GpuProjectionFactor > 0f
                                             ? HardwareBuildSettings.CpuBudgetMs : 0.0,
                                         "ms", s.Kind == SnapshotKind.Gpu);
            DrawMetricLine(Loc.T("Memória", "Memory", "Memoria"), s.MemoryUsedBytes / (1024.0 * 1024.0),
                                         HardwareBuildSettings.MemoryBudgetBytes / (1024.0 * 1024.0), "MB", s.Kind == SnapshotKind.Memory);

            EditorGUILayout.Space(4);

            if (!string.IsNullOrEmpty(s.CulpritCallStack))
            {
                EditorGUILayout.LabelField(Loc.T("Responsáveis lidos do Profiler", "Culprits read from the Profiler",
                                                 "Responsables leídos del Profiler"), EditorStyles.miniBoldLabel);
                // TextArea em vez de LabelField: o dev consegue selecionar e copiar o
                // nome do método pra ir direto no código.
                EditorGUILayout.TextArea(s.CulpritCallStack, EditorStyles.miniLabel);
            }
            else
            {
                NoticeBox.Draw(ExplainMissingCulprit(s.Kind));
            }

            EditorGUILayout.Space(2);
            float segundos = s.TimestampMs / 1000f;
            EditorGUILayout.LabelField(Loc.T("Capturado em", "Captured at", "Capturado en"),
                Loc.T($"{segundos:F1} s de sessão   •   build \"{s.BuildId}\"",
                      $"{segundos:F1} s into the session   •   build \"{s.BuildId}\"",
                      $"{segundos:F1} s de sesión   •   build \"{s.BuildId}\""), EditorStyles.miniLabel);

            // MOTIVO: os budgets acima vêm da build ATIVA agora, não da que valia na
            // captura. Se o dev trocou de build no meio, as porcentagens não batem com
            // o que ele viu no semáforo — avisar é mais barato que versionar budgets.
            if (!string.IsNullOrEmpty(s.BuildId) && s.BuildId != HardwareBuildSettings.ActiveBuildId)
            {
                NoticeBox.Draw(Loc.T(
                    $"Este snapshot foi capturado na build \"{s.BuildId}\", mas as porcentagens acima usam a build ativa agora. " +
                    "Volte para a build original se quiser comparar com o que o Monitor mostrou.",
                    $"This snapshot was captured with build \"{s.BuildId}\", but the percentages above use the build active now. " +
                    "Switch back to the original build to compare with what the Monitor showed.",
                    $"Este snapshot se capturó con la build \"{s.BuildId}\", pero los porcentajes de arriba usan la build activa ahora. " +
                    "Vuelve a la build original si quieres comparar con lo que mostró el Monitor."),
                    NoticeLevel.Warning);
            }
        }

        private static void DrawMetricLine(string label, double value, double budget, string unit, bool isTrigger,
                                           string valueOverride = null)
        {
            double ratio = budget > 0 ? value / budget : 0.0;

            // MOTIVO: sem teto não há veredito, então não pode haver COR. A bolinha amarela
            // marcava "foi isto que disparou a captura", mas cor ao lado de "sem budget
            // definido" se lê como alerta — o GC aparecia amarelo com 24 bytes. O gatilho
            // sem teto é marcado por seta e negrito, que dizem "motivo da captura" sem
            // dizer "problema".
            string icon;
            if (budget <= 0)       icon = isTrigger ? "▶" : "  ";
            else if (ratio >= 1.0) icon = "🔴";
            else if (isTrigger)    icon = "🟡";
            else                   icon = "  ";

            string valueText = valueOverride ?? (unit.Length > 0
                ? $"{value:F1} {unit}"
                : $"{value:F0}");
            string budgetValue = unit.Length > 0 ? $"{budget:F1} {unit}" : $"{budget:F0}";
            string pct = $"{System.Math.Floor(ratio * 100):F0}%";
            string budgetText = budget > 0
                ? Loc.T($"de {budgetValue}  ({pct})", $"of {budgetValue}  ({pct})", $"de {budgetValue}  ({pct})")
                : Loc.T("sem budget definido", "no budget set", "sin presupuesto definido");

            // O que disparou a captura leva destaque; os outros são contexto.
            GUIStyle style = isTrigger ? EditorStyles.boldLabel : EditorStyles.label;
            EditorGUILayout.LabelField($"{icon}  {label}", $"{valueText}   {budgetText}", style);
        }

        /// <summary>Formats a byte count with a unit that keeps small values visible.</summary>
        /// <remarks>
        /// MOTIVO: o GC era sempre mostrado em KB com uma casa decimal, e 24 bytes viravam
        /// "0,0 KB" — o snapshot dizia no título "24 B" e no corpo "0,0 KB", como se nada
        /// tivesse sido alocado.
        /// </remarks>
        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)        return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
        // MOTIVO: campo vazio sem explicação parece bug. Cada tipo tem um porquê diferente.
        private static string ExplainMissingCulprit(SnapshotKind kind)
        {
            switch (kind)
            {
                case SnapshotKind.DrawCalls:
                    return Loc.T(
                        "Draw calls não têm um método C# responsável — o custo está na quantidade de objetos " +
                        "submetidos, não numa linha de código. O caminho é batching, culling e menos objetos distintos em cena.",
                        "Draw calls have no single C# method to blame — the cost is the number of objects submitted, " +
                        "not a line of code. The way out is batching, culling and fewer distinct objects in the scene.",
                        "Los draw calls no tienen un método C# responsable: el costo está en la cantidad de objetos " +
                        "enviados, no en una línea de código. El camino es batching, culling y menos objetos distintos en la escena.");
                case SnapshotKind.Gpu:
                    return Loc.T(
                        "Tempo de GPU não tem um método C# responsável — quem paga é o preenchimento de pixels. Olhe " +
                        "resolução, overdraw de transparências, complexidade de shader, pós-processamento e sombras.",
                        "GPU time has no single C# method to blame — it is paid by pixel fill. Look at resolution, " +
                        "transparency overdraw, shader complexity, post-processing and shadows.",
                        "El tiempo de GPU no tiene un método C# responsable: lo paga el relleno de píxeles. Revisa " +
                        "resolución, overdraw de transparencias, complejidad de shaders, post-procesado y sombras.");
                case SnapshotKind.Memory:
                    return Loc.T(
                        "Atribuir consumo de memória a um responsável exigiria um snapshot completo de memória do " +
                        "Profiler, que congela o Editor por segundos e alteraria o próprio frame medido.",
                        "Blaming memory use on something would need a full Profiler memory snapshot, which freezes " +
                        "the Editor for seconds and would change the very frame being measured.",
                        "Atribuir el uso de memoria a un responsable requeriría un snapshot completo de memoria del " +
                        "Profiler, que congela el Editor durante segundos y alteraría el propio frame medido.");
                default:
                    return Loc.T(
                        "O Profiler não tinha frame disponível no momento da captura — a identificação dos " +
                        "responsáveis é espaçada para não segurar o Editor.",
                        "The Profiler had no frame available at capture time — culprit lookup is spaced out " +
                        "so it does not hold up the Editor.",
                        "El Profiler no tenía un frame disponible en el momento de la captura: la búsqueda de " +
                        "responsables se espacia para no frenar el Editor.");
            }
        }

        private static string GetKindIcon(SnapshotKind k)
        {
            switch (k)
            {
                case SnapshotKind.Cpu:       return "⚙️ CPU";
                case SnapshotKind.Gc:        return "🗑️ GC";
                case SnapshotKind.DrawCalls: return "🎨 Draw";
                case SnapshotKind.Gpu:       return "🖥️ GPU";
                case SnapshotKind.Memory:    return "💾 Mem";
                default:                      return "🔍";
            }
        }

        private static string FormatMetric(FrameSnapshot s)
        {
            switch (s.Kind)
            {
                case SnapshotKind.Cpu:       return $"{s.CpuLatencyMs:F1} ms";
                case SnapshotKind.Gc:        return $"{s.GcAllocatedBytes} B";
                case SnapshotKind.DrawCalls: return $"{s.DrawCallsCount} draws";
                case SnapshotKind.Gpu:       return $"{s.GpuMs:F1} ms GPU";
                case SnapshotKind.Memory:    return $"{s.MemoryUsedBytes / (1024f * 1024f):F0} MB";
                default:                      return "";
            }
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            int nl = s.IndexOf('\n');
            return nl >= 0 ? s.Substring(0, nl) : s;
        }

        /// <summary>Distinct root causes across every kind — what the LLM actually receives.</summary>
        private static int CountAllCauses(FrameSnapshot[] all)
        {
            int n = 0;
            for (int k = 0; k < KindTabs.Length; k++)
                n += SnapshotGrouping.Group(FilterByKind(all, KindTabs[k])).Count;
            return n;
        }

        /// <summary>Causes whose representative is still checked.</summary>
        private int CountSelectedCauses(FrameSnapshot[] all)
        {
            int n = 0;
            for (int k = 0; k < KindTabs.Length; k++)
            {
                var groups = SnapshotGrouping.Group(FilterByKind(all, KindTabs[k]));
                for (int i = 0; i < groups.Count; i++)
                    if (!_deselectedIds.Contains(groups[i].Representative.Id)) n++;
            }
            return n;
        }

        private int CountSelected(FrameSnapshot[] all)
        {
            int n = 0;
            for (int i = 0; i < all.Length; i++)
                if (!_deselectedIds.Contains(all[i].Id)) n++;
            return n;
        }

        private void SendSelectedSnapshots(FrameSnapshot[] all)
        {
            // Particiona por Kind, respeitando a seleção.
            var cpu     = new System.Collections.Generic.List<FrameSnapshot>();
            var gc      = new System.Collections.Generic.List<FrameSnapshot>();
            var draw    = new System.Collections.Generic.List<FrameSnapshot>();
            var mem     = new System.Collections.Generic.List<FrameSnapshot>();
            var gpu     = new System.Collections.Generic.List<FrameSnapshot>();
            for (int i = 0; i < all.Length; i++)
            {
                var s = all[i];
                if (_deselectedIds.Contains(s.Id)) continue;
                switch (s.Kind)
                {
                    case SnapshotKind.Cpu:       cpu.Add(s);     break;
                    case SnapshotKind.Gc:        gc.Add(s);      break;
                    case SnapshotKind.DrawCalls: draw.Add(s);    break;
                    case SnapshotKind.Memory:    mem.Add(s);     break;
                    case SnapshotKind.Gpu:       gpu.Add(s);     break;
                }
            }
            // MOTIVO: envia UM representante por causa raiz, não todos os frames.
            // Os 10 snapshots de um tipo costumam ser o mesmo problema repetido — mandar
            // os 10 gastava payload e devolvia dez vezes o mesmo codigo_refatorado. Depois
            // do agrupamento, cada amostra enviada é um problema DIFERENTE, e é por isso
            // que o prompt agora pede um finding por amostra em vez de um por tipo: duas
            // causas distintas de CPU passam a receber duas respostas, não uma.
            GeminiRestBridge.SendSnapshots(
                SnapshotGrouping.Representatives(SnapshotGrouping.Group(cpu.ToArray())),
                SnapshotGrouping.Representatives(SnapshotGrouping.Group(gc.ToArray())),
                SnapshotGrouping.Representatives(SnapshotGrouping.Group(draw.ToArray())),
                SnapshotGrouping.Representatives(SnapshotGrouping.Group(mem.ToArray())),
                SnapshotGrouping.Representatives(SnapshotGrouping.Group(gpu.ToArray())));
        }

        /// <summary>
        /// Indicator that is measured but deliberately not judged — no colour, no bar.
        /// </summary>
        /// <remarks>
        /// MOTIVO: quando não existe limiar com procedência, exibir um semáforo seria
        /// afirmar um julgamento sem lastro. O número continua visível e continua indo
        /// para o laudo; o que não acontece é o plugin fingir que sabe o limite.
        /// </remarks>
        private static void DrawReadingOnly(string label, string value, string why)
        {
            EditorGUILayout.LabelField($"     {label}", $"{value}   ({why})", EditorStyles.miniLabel);
        }


        /// <summary>Why memory has no verdict right now, in words the dev can act on.</summary>
        /// <remarks>
        /// MOTIVO: a mensagem dizia "sem teto publicado para este alvo" em tudo que não
        /// fosse PC. Desde que o Android passou a aceitar teto DECLARADO, isso ficou
        /// errado — e errado no pior sentido: dizia ao dev que não havia nada a fazer,
        /// quando bastava declarar o teto. O autor perguntou por que memória tinha ficado
        /// sem laudo, e a tela tinha respondido a coisa errada.
        ///
        /// Cada motivo aponta a ação que resolve, porque a ação é diferente em cada caso.
        /// </remarks>
        private static string MemoryNoVerdictReason()
        {
            if (!TelemetryMonitorCore.RemoteMemResolved)
                return Loc.T("contador de memória ausente neste aparelho", "memory counter not available on this device",
                             "contador de memoria ausente en este dispositivo");

            var b = HardwareBuildSettings.GetActiveCopy();

            if (b.Test == TestMode.NoEditor)
                return Loc.T("no Editor a memória é da Unity inteira — meça na build",
                             "in the Editor the memory is the whole of Unity — measure on a build",
                             "en el Editor la memoria es la de toda Unity: mide en una build");

            // VR tem limite publicado pela Meta; se chegou aqui, o headset não publica.
            if (b.Platform == TargetPlatform.VR)
                return Loc.T("sem teto publicado para este headset", "no published limit for this headset",
                             "sin límite publicado para este visor");

            // PC e Android: o teto é declarado — ou definido sozinho ao fim da primeira
            // sessão medida na build. Na primeira sessão ainda não existe, e isso é esperado.
            return b.Test == TestMode.NaBuild
                ? Loc.T("sem teto ainda — ele se define sozinho ao fim desta sessão (pico + 20%)",
                        "no ceiling yet — it is set automatically at the end of this session (peak + 20%)",
                        "todavía sin techo: se define solo al final de esta sesión (pico + 20%)")
                : Loc.T("sem teto declarado", "no ceiling declared", "sin techo declarado");
        }

        /// <summary>Verdict slot for which no projection is possible.</summary>
        private static void DrawNoVerdict(string label, string why)
        {
            EditorGUILayout.LabelField($"⚪  {label}", why, EditorStyles.miniLabel);
        }

        // Hick's-law semáforo: no millisecond numbers, traffic-light only.

        /// <summary>
        /// Warns when the declared test mode and the actual data source disagree, or when
        /// no gate can fire at all.
        /// </summary>
        /// <remarks>
        /// MOTIVO (1) — o modo promete o que a coleta ainda não entrega. O painel de Build
        /// deixa escolher "Na build", mas o Porteiro lê por <c>ProfilerRecorder</c>, que é
        /// local por design e só enxerga o processo do Editor. Selecionar o modo e dar
        /// Play devolve números do EDITOR rotulados como medição no aparelho. A migração
        /// para <c>ProfilerDriver</c> é a etapa 2; até lá, quem tem que avisar é a tela.
        ///
        /// MOTIVO (2) — "Snapshots capturados: 0" se lê como "nenhum problema encontrado",
        /// e pode significar o oposto: que NENHUM portão conseguiu disparar. Com alvo
        /// Android, por exemplo, CPU e GPU exigem fator de projeção (que é 0), e draw
        /// calls e memória exigem teto (que é 0) — então não há o que violar, e o plugin
        /// roda coletando nada enquanto parece saudável. Medido: 12.623 draw calls e
        /// 2900 KB/frame de GC, com 0 snapshots.
        ///
        /// Um zero ambíguo é pior que um alerta: ele passa por aprovação.
        /// </remarks>
        private static void DrawCollectionGuard(bool hasSample)
        {
            var b = HardwareBuildSettings.GetActiveCopy();

            // (1) Modo declarado × origem REAL dos dados.
            // MOTIVO: a leitura remota existe agora, mas ela depende de haver player
            // conectado. Declarar "na build" sem build rodando cai na leitura local, e aí
            // os números são do Editor com rótulo de aparelho — a confusão exata que esta
            // etapa existe pra eliminar. O aviso passou a comparar o modo com a FONTE que
            // está de fato em uso, em vez de avisar sempre.
            // MOTIVO: a condição exigia monitoramento ATIVO, então o aviso sumia no Stop —
            // justamente quando o dev vai olhar o resultado. Agora vale enquanto houver
            // números da sessão na tela.
            if (b.Test == TestMode.NaBuild && !TelemetryMonitorCore.IsRemote
                && (TelemetryMonitorCore.IsMonitoring || hasSample))
            {
                // MOTIVO: o caso real foi o Unity Remote. Ele parece "rodar no celular" —
                // a imagem aparece lá e o toque funciona — mas o jogo executa NO PC, e o
                // aparelho é só tela e input. Quem vê o jogo na tela do celular conclui,
                // razoavelmente, que está medindo o celular. Dizer isso pelo nome é o que
                // desfaz a confusão.
                NoticeBox.Draw(
                    b.Platform == TargetPlatform.PC
                        ? Loc.T("Estes números vêm do processo do Editor, não da build. Compile e execute " +
                                "pela aba Build para medir o jogo.",
                                "These numbers come from the Editor process, not the build. Build and run " +
                                "from the Build tab to measure the game.",
                                "Estos números vienen del proceso del Editor, no de la build. Compila y ejecuta " +
                                "desde la pestaña Build para medir el juego.")
                        : Loc.T("Estes números vêm do PC, não do aparelho — sem veredito. O Unity Remote " +
                                "roda o jogo NO PC e usa o celular só como tela e touch. Para medir o " +
                                "aparelho, use \"Compilar e executar\" e deixe o APK aberto no celular.",
                                "These numbers come from the PC, not the device — no verdict. Unity Remote " +
                                "runs the game ON THE PC and uses the phone only as screen and touch. To measure " +
                                "the device, use \"Build and run\" and keep the APK open on the phone.",
                                "Estos números vienen del PC, no del dispositivo: sin veredicto. Unity Remote " +
                                "ejecuta el juego EN EL PC y usa el teléfono solo como pantalla y táctil. Para medir " +
                                "el dispositivo, usa \"Compilar y ejecutar\" y deja el APK abierto en el teléfono."),
                    NoticeLevel.Warning);
            }
            else if (TelemetryMonitorCore.IsRemote)
            {
                string alvo = TelemetryMonitorCore.SessionTargetName;
                NoticeBox.Draw(Loc.T(
                    $"Medido em {alvo} — os números são do processo do jogo, sem Editor no meio.",
                    $"Measured on {alvo} — the numbers come from the game process, with no Editor in between.",
                    $"Medido en {alvo}: los números son del proceso del juego, sin el Editor de por medio."),
                    NoticeLevel.Success);
            }

            // (2) Nenhum portão pode disparar.
            bool temAlgumPortao =
                HardwareBuildSettings.CpuProjectionFactor > 0f ||
                HardwareBuildSettings.GpuProjectionFactor > 0f ||
                HardwareBuildSettings.DrawCallBudget      > 0  ||
                HardwareBuildSettings.MemoryBudgetBytes   > 0;

            if (!temAlgumPortao)
            {
                NoticeBox.Draw(Loc.T(
                    "Nenhum limiar com procedência existe para esta build, então NADA pode ser " +
                    "capturado — 0 snapshots aqui significa \"não havia o que julgar\", e não " +
                    "\"está tudo bem\". Os valores do diagnóstico continuam válidos para comparar " +
                    "uma versão da cena com outra.",
                    "No threshold with a known source exists for this build, so NOTHING can be captured — " +
                    "0 snapshots here means \"there was nothing to judge\", not \"everything is fine\". " +
                    "The diagnostic values are still valid for comparing one version of the scene with another.",
                    "No existe ningún umbral con fuente conocida para esta build, así que NO se puede capturar " +
                    "nada: 0 snapshots aquí significa \"no había nada que juzgar\", no \"todo está bien\". " +
                    "Los valores del diagnóstico siguen siendo válidos para comparar una versión de la escena con otra."),
                    NoticeLevel.Warning);
            }
        }
        private static void DrawSemaphore(string label, double value, double budget)
        {
            float ratio = budget > 0 ? (float)(value / budget) : 0f;
            // MOTIVO da faixa amarela a partir de 80%: é ESCOLHA DE INTERFACE, sem fonte — não
            // é limite de hardware e não decide nada. O que julga (captura de snapshot,
            // contagem de violações) é só "passou do orçamento", que tem procedência. O
            // amarelo só avisa que está perto. Mantido por decisão do autor (22/09/2026),
            // e declarado assim também no TIC. No mobile ele se soma à folga térmica de 35%
            // que já está dentro do orçamento — ou seja, é margem sobre margem.
            string icon  = ratio < 0.8f ? "🟢" : ratio < 1.0f ? "🟡" : "🔴";
            string state = ratio < 0.8f ? "OK"
                         : ratio < 1.0f ? Loc.T("Atenção", "Warning", "Atención")
                         : Loc.T("Crítico", "Critical", "Crítico");

            // MOTIVO: o número exibido era ARREDONDADO e a cor usa o valor exato. Perto de
            // uma fronteira a tela se contradizia: 79,6% aparecia como "80%" verde e 80,4%
            // como "80%" amarelo — visto no tablet, o mesmo "80%" com duas cores. Truncar
            // mantém o número do mesmo lado da fronteira que a cor: 79,6% vira 79%.
            int shown = Mathf.FloorToInt(ratio * 100f);

            EditorGUILayout.LabelField($"{icon}  {label}", state);
            Rect r = EditorGUILayout.GetControlRect(false, 16);
            EditorGUI.ProgressBar(r, Mathf.Clamp01(ratio),
                Loc.T($"{shown}% do orçamento", $"{shown}% of budget", $"{shown}% del presupuesto"));
            EditorGUILayout.Space(4);
        }

        // ─── Configuração: provedor, chave e modelo ───────────────────────
        // MOTIVO do estado local: a lista de modelos vem de uma chamada de rede, então
        // precisa sobreviver entre repaints do IMGUI. Fica na janela, não em EditorPrefs
        // — é cache de sessão, e forçar o dev a rebuscar quando reabre o Unity é melhor
        // que servir uma lista velha como se fosse atual.
        private string[] _fetchedModels;
        private bool     _fetchingModels;
        private string   _modelFetchError;
        private string   _probingModel;   // modelo em teste agora; null quando nenhum
        private AiProviderKind _lastDrawnProvider = (AiProviderKind)(-1);

        private void DrawConfigurationTab()
        {
            // ── Idioma ───────────────────────────────────────────────────
            // MOTIVO: fica no topo porque muda tudo que vem abaixo — e o laudo e o chat da IA.
            EditorGUILayout.LabelField(Loc.T("Idioma", "Language", "Idioma"), EditorStyles.boldLabel);
            int langIdx = (int)Loc.Current;
            int newLang = EditorGUILayout.Popup(Loc.T("Interface e IA", "Interface and AI", "Interfaz e IA"),
                                                langIdx, Loc.Names);
            if (newLang != langIdx) Loc.Current = (UiLanguage)newLang;
            EditorGUILayout.LabelField(Loc.T(
                "Vale para toda a ferramenta, inclusive o laudo e o chat da IA. O padrão é o idioma do sistema.",
                "Applies to the whole tool, including the AI report and chat. The default is the system language.",
                "Se aplica a toda la herramienta, incluidos el informe y el chat de la IA. Por defecto, el idioma del sistema."),
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(12);

            // ── Provedor ─────────────────────────────────────────────────
            EditorGUILayout.LabelField(Loc.T("Provedor de IA", "AI provider", "Proveedor de IA"), EditorStyles.boldLabel);

            var kinds = (AiProviderKind[])System.Enum.GetValues(typeof(AiProviderKind));
            var kindLabels = new string[kinds.Length];
            int kindIdx = 0;
            for (int i = 0; i < kinds.Length; i++)
            {
                kindLabels[i] = AiSettings.DisplayName(kinds[i]);
                if (kinds[i] == AiSettings.Provider) kindIdx = i;
            }

            int newKind = EditorGUILayout.Popup(Loc.T("Serviço", "Service", "Servicio"), kindIdx, kindLabels);
            if (newKind != kindIdx)
            {
                AiSettings.Provider = kinds[newKind];
                // MOTIVO: a lista de modelos é de OUTRO provedor agora. Mantê-la na tela
                // ofereceria "gpt-4o" a quem acabou de escolher Gemini.
                _fetchedModels = null;
                _modelFetchError = null;
                _apiKeyInput = AiSettings.ApiKey;
            }

            // MOTIVO: sincroniza o campo de chave quando o provedor muda por fora
            // (primeira abertura da aba, ou troca via outro caminho).
            if (_lastDrawnProvider != AiSettings.Provider)
            {
                _lastDrawnProvider = AiSettings.Provider;
                _apiKeyInput = AiSettings.ApiKey;
            }

            AiProviderKind p = AiSettings.Provider;

            // MOTIVO: o plugin manda TRECHOS DE CÓDIGO do jogo no payload. Onde isso vai
            // parar é decisão informada, não detalhe de implementação — e foi apontamento
            // de privacidade do avaliador.
            NoticeBox.Draw(
                AiSettings.IsLocal(p)
                    ? Loc.T("Roda na sua máquina. Nenhum trecho de código sai daqui.",
                            "Runs on your machine. No code leaves this computer.",
                            "Se ejecuta en tu máquina. Ningún fragmento de código sale de aquí.")
                    : Loc.T($"Os snapshots e trechos de código são enviados para {AiSettings.DisplayName(p)}.",
                            $"Snapshots and code excerpts are sent to {AiSettings.DisplayName(p)}.",
                            $"Los snapshots y fragmentos de código se envían a {AiSettings.DisplayName(p)}."),
                AiSettings.IsLocal(p) ? NoticeLevel.Success : NoticeLevel.Warning);

            // MOTIVO: a chave lista modelos mesmo sem crédito na conta, e a assinatura do
            // chat do provedor não paga a API. Sem isto na tela, o dev só descobre pelo erro.
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(Loc.T("💰 Custos e requisitos", "💰 Costs and requirements", "💰 Costos y requisitos"),
                                     GUILayout.Height(22)))
                    ProviderHelp.Show(p);
                if (GUILayout.Button(Loc.T("Página de preços", "Pricing page", "Página de precios"), GUILayout.Height(22)))
                    Application.OpenURL(ProviderHelp.PricingUrl(p));
            }
            if (!AiSettings.IsLocal(p))
                EditorGUILayout.LabelField(Loc.T(
                    "A API é cobrada à parte da assinatura do chat do provedor.",
                    "The API is billed separately from the provider's chat subscription.",
                    "La API se cobra aparte de la suscripción al chat del proveedor."),
                    EditorStyles.miniLabel);

            // MOTIVO: sem o código do script, a IA só sabe o NOME do método culpado — e não tem
            // como sugerir uma correção sem inventar o corpo dele. A escolha é do dev, porque o
            // código do projeto vai ao provedor (com o Ollama, não sai da máquina).
            EditorGUILayout.Space(6);
            bool src = EditorGUILayout.ToggleLeft(Loc.T(
                "Enviar o código dos scripts da pilha (permite sugerir a correção no seu código)",
                "Send the code of the scripts in the call stack (lets the AI suggest a fix to your code)",
                "Enviar el código de los scripts de la pila (permite sugerir la corrección en tu código)"),
                AiSettings.SendScriptSources);
            if (src != AiSettings.SendScriptSources) AiSettings.SendScriptSources = src;
            EditorGUILayout.LabelField(src
                ? Loc.T("Só os arquivos .cs do projeto citados na pilha, até 100 KB. Eles vão ao provedor junto com a telemetria.",
                        "Only the project's .cs files named in the stack, up to 100 KB. They go to the provider with the telemetry.",
                        "Solo los archivos .cs del proyecto citados en la pila, hasta 100 KB. Van al proveedor junto con la telemetría.")
                : Loc.T("Desligado: a IA recebe só a telemetria e o nome dos métodos, e não terá como sugerir código.",
                        "Off: the AI gets only the telemetry and method names, and will not be able to suggest code.",
                        "Desactivado: la IA recibe solo la telemetría y el nombre de los métodos, y no podrá sugerir código."),
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(10);

            // ── Credencial (ou endereço, no caso local) ──────────────────
            if (AiSettings.IsLocal(p))
            {
                EditorGUILayout.LabelField(Loc.T("Servidor Ollama", "Ollama server", "Servidor Ollama"), EditorStyles.boldLabel);
                string url = EditorGUILayout.TextField(Loc.T("Endereço", "Address", "Dirección"), AiSettings.OllamaUrl);
                if (url != AiSettings.OllamaUrl) AiSettings.OllamaUrl = url;

                if (GUILayout.Button(Loc.T("Como instalar o Ollama", "How to install Ollama", "Cómo instalar Ollama"),
                                     GUILayout.Height(22)))
                    Application.OpenURL(AiSettings.KeyHelpUrl(p));
            }
            else
            {
                EditorGUILayout.LabelField(Loc.T($"Chave da API — {AiSettings.DisplayName(p)}", $"API key — {AiSettings.DisplayName(p)}",
                                                 $"Clave de la API — {AiSettings.DisplayName(p)}"), EditorStyles.boldLabel);
                _apiKeyInput = EditorGUILayout.PasswordField("API Key", _apiKeyInput ?? "");

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(Loc.T("Salvar chave", "Save key", "Guardar clave"), GUILayout.Height(24)))
                    {
                        AiSettings.ApiKey = _apiKeyInput?.Trim() ?? "";
                        // MOTIVO: a lista antiga pode ser de outra conta, com outro
                        // catálogo de modelos. Invalida em vez de deixar desatualizada.
                        _fetchedModels = null;
                        EditorUtility.DisplayDialog("Performance Advisor",
                            Loc.T("Chave salva.", "Key saved.", "Clave guardada."), "OK");
                    }
                    if (GUILayout.Button(Loc.T("Onde obter a chave", "Where to get a key", "Dónde obtener la clave"),
                                         GUILayout.Height(24)))
                        Application.OpenURL(AiSettings.KeyHelpUrl(p));
                }

                EditorGUILayout.LabelField(Loc.T("Status", "Status", "Estado"),
                    string.IsNullOrEmpty(AiSettings.ApiKey)
                        ? Loc.T("✗ Chave não configurada", "✗ Key not set", "✗ Clave no configurada")
                        : Loc.T("✓ Chave configurada", "✓ Key set", "✓ Clave configurada"),
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(Loc.T(
                    "A chave fica só nesta máquina (EditorPrefs) — nunca é salva no projeto.",
                    "The key stays only on this machine (EditorPrefs) — it is never saved in the project.",
                    "La clave queda solo en esta máquina (EditorPrefs): nunca se guarda en el proyecto."),
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(16);

            // ── Modelo ───────────────────────────────────────────────────
            // MOTIVO: era um enum fixo no código, com duas versões do Gemini escritas à
            // mão. Modelos são aposentados pelo provedor e a lista envelhecia sozinha.
            // Agora a lista vem da API, com a chave do dev — mas a listagem diz quais
            // modelos o provedor OFERECE, não quais a conta tem cota para usar (pergunta
            // do orientador, 01/10/2026). Por isso: a lista é filtrada para modelos de
            // texto, e o modelo escolhido é confirmado com uma chamada de teste.
            EditorGUILayout.LabelField(Loc.T("Modelo", "Model", "Modelo"), EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(_fetchingModels))
            {
                if (GUILayout.Button(_fetchingModels
                                         ? Loc.T("Buscando…", "Fetching…", "Buscando…")
                                         : Loc.T("Buscar modelos disponíveis", "Fetch available models", "Buscar modelos disponibles"),
                                     GUILayout.Height(24)))
                {
                    _fetchingModels = true;
                    _modelFetchError = null;
                    AiHttpClient.ListModels(
                        ids =>
                        {
                            _fetchedModels = ids;
                            _fetchingModels = false;
                            // MOTIVO: se o modelo salvo não está na lista real, ele
                            // falharia com 404 na primeira chamada. Cai no primeiro
                            // disponível em vez de esperar o erro.
                            if (System.Array.IndexOf(ids, AiSettings.SelectedModel) < 0)
                                AiSettings.SelectedModel = ids[0];
                            Repaint();
                        },
                        err =>
                        {
                            _modelFetchError = err;
                            _fetchingModels = false;
                            Repaint();
                        });
                }
            }

            if (!string.IsNullOrEmpty(_modelFetchError))
                NoticeBox.Draw(_modelFetchError, NoticeLevel.Warning);

            if (_fetchedModels != null && _fetchedModels.Length > 0)
            {
                int cur = System.Array.IndexOf(_fetchedModels, AiSettings.SelectedModel);
                if (cur < 0) cur = 0;

                // MOTIVO: o resultado dos testes aparece na própria lista. O dev vê, antes
                // de escolher, quais modelos já responderam à chave dele e quais não.
                var labels = new string[_fetchedModels.Length];
                for (int i = 0; i < _fetchedModels.Length; i++)
                {
                    var state = AiSettings.GetProbe(_fetchedModels[i], out _);
                    labels[i] = state == AiSettings.ProbeState.Ok     ? "✓ " + _fetchedModels[i]
                              : state == AiSettings.ProbeState.Failed ? "✗ " + _fetchedModels[i]
                              : _fetchedModels[i];
                }

                int sel = EditorGUILayout.Popup(Loc.T("Modelo", "Model", "Modelo"), cur, labels);
                if (sel != cur)
                {
                    AiSettings.SelectedModel = _fetchedModels[sel];
                    // MOTIVO: testa ao escolher, não ao listar — só o modelo que o dev
                    // vai usar gasta uma requisição da cota dele.
                    if (AiSettings.GetProbe(AiSettings.SelectedModel, out _) == AiSettings.ProbeState.Untested)
                        StartModelProbe(AiSettings.SelectedModel);
                }

                EditorGUILayout.LabelField(
                    AiSettings.IsLocal(p)
                        ? Loc.T($"{_fetchedModels.Length} modelo(s) de texto instalados no seu Ollama.",
                                $"{_fetchedModels.Length} text model(s) installed in your Ollama.",
                                $"{_fetchedModels.Length} modelo(s) de texto instalados en tu Ollama.")
                        : Loc.T($"{_fetchedModels.Length} modelo(s) de texto oferecidos por {AiSettings.DisplayName(p)}. " +
                                "Modelos de imagem, voz e experimentais ficam de fora.",
                                $"{_fetchedModels.Length} text model(s) offered by {AiSettings.DisplayName(p)}. " +
                                "Image, voice and experimental models are left out.",
                                $"{_fetchedModels.Length} modelo(s) de texto ofrecidos por {AiSettings.DisplayName(p)}. " +
                                "Los modelos de imagen, voz y experimentales quedan fuera."),
                    EditorStyles.miniLabel);
            }
            else
            {
                // MOTIVO: sem a lista buscada, o campo aceita digitação. Um provedor pode
                // ter um modelo novo que a listagem ainda não devolve, e travar o dev
                // numa lista vazia seria pior que deixá-lo escrever o id.
                string typed = EditorGUILayout.TextField(Loc.T("Modelo", "Model", "Modelo"), AiSettings.SelectedModel ?? "");
                if (typed != AiSettings.SelectedModel) AiSettings.SelectedModel = typed;

                EditorGUILayout.LabelField(
                    AiSettings.IsLocal(p)
                        ? Loc.T("Busque a lista para ver os modelos já baixados no seu Ollama.",
                                "Fetch the list to see the models already downloaded in your Ollama.",
                                "Busca la lista para ver los modelos ya descargados en tu Ollama.")
                        : Loc.T("Busque a lista para ver os modelos do provedor. O valor atual é só um palpite.",
                                "Fetch the list to see the provider's models. The current value is only a guess.",
                                "Busca la lista para ver los modelos del proveedor. El valor actual es solo una suposición."),
                    EditorStyles.miniLabel);
            }

            DrawModelProbeStatus();
        }

        // Linha de status do modelo escolhido + botão de teste.
        private void DrawModelProbeStatus()
        {
            string model = AiSettings.SelectedModel;
            if (string.IsNullOrEmpty(model)) return;

            EditorGUILayout.Space(4);

            if (_probingModel == model)
            {
                NoticeBox.Draw(Loc.T($"Testando \"{model}\" com a sua chave…",
                                     $"Testing \"{model}\" with your key…",
                                     $"Probando \"{model}\" con tu clave…"), NoticeLevel.Info);
                return;
            }

            var state = AiSettings.GetProbe(model, out string failure);
            switch (state)
            {
                case AiSettings.ProbeState.Ok:
                    NoticeBox.Draw(Loc.T($"✓ \"{model}\" respondeu ao teste com esta chave.",
                                         $"✓ \"{model}\" answered the test with this key.",
                                         $"✓ \"{model}\" respondió a la prueba con esta clave."), NoticeLevel.Success);
                    break;
                case AiSettings.ProbeState.Failed:
                    NoticeBox.Draw($"✗ \"{model}\": {failure}", NoticeLevel.Warning);
                    break;
                default:
                    NoticeBox.Draw(Loc.T(
                        "Modelo ainda não testado. A lista mostra o que o provedor oferece, não a cota " +
                        "da sua conta — teste antes de gerar o laudo.",
                        "Model not tested yet. The list shows what the provider offers, not your account's " +
                        "quota — test it before generating the report.",
                        "Modelo aún no probado. La lista muestra lo que ofrece el proveedor, no la cuota " +
                        "de tu cuenta: pruébalo antes de generar el informe."),
                        NoticeLevel.Info);
                    break;
            }

            using (new EditorGUI.DisabledScope(_probingModel != null))
            {
                string label = state == AiSettings.ProbeState.Untested
                    ? Loc.T("Testar este modelo", "Test this model", "Probar este modelo")
                    : Loc.T("Testar de novo", "Test again", "Probar de nuevo");
                if (GUILayout.Button(label, GUILayout.Height(22)))
                    StartModelProbe(model);
            }
            EditorGUILayout.LabelField(Loc.T("O teste usa 1 requisição da sua cota.",
                                             "The test uses 1 request from your quota.",
                                             "La prueba usa 1 solicitud de tu cuota."), EditorStyles.miniLabel);
        }

        private void StartModelProbe(string model)
        {
            _probingModel = model;
            AiHttpClient.ProbeModel(model,
                () =>
                {
                    AiSettings.SetProbe(model, true, null);
                    _probingModel = null;
                    Repaint();
                },
                msg =>
                {
                    AiSettings.SetProbe(model, false, msg);
                    _probingModel = null;
                    Repaint();
                });
        }

        // Renderiza UM finding (1 problema diagnosticado) como um bloco completo:
        // header colorido por tipo + causa raiz + código + botão copiar + explicação.
        private void DrawFinding(int index, GeminiFinding f)
        {
            if (f == null) return;

            // Header com cor por tipo de gargalo
            string typeIcon = GetTypeIcon(f.tipo);
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    $"{typeIcon} {Loc.T("Problema", "Issue", "Problema")} {index + 1} — " +
                    $"{f.tipo ?? Loc.T("Geral", "General", "General")}",
                    EditorStyles.boldLabel);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(Loc.T("Causa raiz", "Root cause", "Causa raíz"), EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox(f.causa_raiz ?? Loc.T("(vazio)", "(empty)", "(vacío)"), MessageType.Warning);

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField(Loc.T("Código refatorado", "Refactored code", "Código refactorizado"),
                                           EditorStyles.miniBoldLabel);
                if (index < _codeScrolls.Length)
                {
                    _codeScrolls[index] = EditorGUILayout.BeginScrollView(
                        _codeScrolls[index], GUILayout.Height(180));
                    EditorGUILayout.TextArea(
                        f.codigo_refatorado ?? "",
                        EditorStyles.textArea,
                        GUILayout.ExpandHeight(true));
                    EditorGUILayout.EndScrollView();
                }

                if (GUILayout.Button(Loc.T("Copiar código deste problema", "Copy this issue's code",
                                           "Copiar el código de este problema"), GUILayout.Height(22)))
                {
                    GUIUtility.systemCopyBuffer = f.codigo_refatorado ?? "";
                    ShowNotification(new GUIContent(Loc.T($"Código #{index + 1} copiado!",
                                                          $"Code #{index + 1} copied!",
                                                          $"¡Código #{index + 1} copiado!")));
                }

                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField(Loc.T("Explicação", "Explanation", "Explicación"), EditorStyles.miniBoldLabel);
                if (index < _explanationScrolls.Length)
                {
                    _explanationScrolls[index] = EditorGUILayout.BeginScrollView(
                        _explanationScrolls[index], GUILayout.Height(100));
                    EditorGUILayout.LabelField(
                        f.explicacao_didatica ?? "",
                        EditorStyles.wordWrappedLabel);
                    EditorGUILayout.EndScrollView();
                }

                // MOTIVO: ponte laudo→chat — iniciantes não sabem aplicar o código
                // genérico do finding; o chat responde sobre o projeto real deles.
                EditorGUILayout.Space(4);
                if (GUILayout.Button(Loc.T("💬 Tirar dúvida sobre este problema", "💬 Ask about this issue",
                                           "💬 Preguntar sobre este problema"), GUILayout.Height(22)))
                {
                    string tipo = f.tipo ?? "performance";
                    _chatTab.PrefillQuestion(Loc.T(
                        $"Sobre o problema de {tipo} do laudo: \"{f.causa_raiz}\" — " +
                        "como aplico essa refatoração no meu projeto? Pode me guiar passo a passo?",
                        $"About the {tipo} issue in the report: \"{f.causa_raiz}\" — " +
                        "how do I apply this refactoring to my project? Can you guide me step by step?",
                        $"Sobre el problema de {tipo} del informe: \"{f.causa_raiz}\" — " +
                        "¿cómo aplico esta refactorización en mi proyecto? ¿Puedes guiarme paso a paso?"));
                    _currentTab = Tab.Chat;
                }
            }
        }

        private static string GetTypeIcon(string tipo)
        {
            switch (tipo)
            {
                case "CPU":       return "⚙️";
                case "GC":        return "🗑️";
                case "DrawCalls": return "🎨";
                case "GPU":       return "🖥️";
                case "Memoria":   return "💾";
                default:           return "🔍";
            }
        }

        // Pontinhos animados (1→3) pro botão enquanto a requisição está em voo.
        private static string AnimatedDots()
        {
            int n = 1 + (int)(EditorApplication.timeSinceStartup * 2.0) % 3;
            return new string('.', n);
        }

        private void DrawReportTab()
        {
            if (_isAnalyzing)
            {
                EditorGUILayout.HelpBox(
                    _fallbackNotice ?? Loc.T($"Analisando{AnimatedDots()} aguardando a resposta da IA.",
                                             $"Analyzing{AnimatedDots()} waiting for the AI's answer.",
                                             $"Analizando{AnimatedDots()} esperando la respuesta de la IA."),
                    MessageType.Info);
                return;
            }
            if (!string.IsNullOrEmpty(_lastError))
            {
                NoticeBox.Draw(_lastError, NoticeLevel.Error);
                if (_lastReport == null) return;
                // MOTIVO: o erro da tentativa mais recente NÃO descarta o último laudo
                // bom — sem isso o laudo "some" da tela quando um retry posterior falha.
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(Loc.T("Exibindo o último laudo recebido (análise anterior):",
                                                 "Showing the last report received (previous analysis):",
                                                 "Mostrando el último informe recibido (análisis anterior):"),
                    EditorStyles.miniBoldLabel);
            }
            if (_lastReport == null)
            {
                EditorGUILayout.HelpBox(Loc.T(
                    "Nenhum laudo ainda. Rode um ciclo Play → Stop → Analisar.",
                    "No report yet. Run a Play → Stop → Analyze cycle.",
                    "Aún no hay informe. Ejecuta un ciclo Play → Stop → Analizar."),
                    MessageType.Info);
                return;
            }

            // Header com contagem de findings
            int n = _lastReport.findings?.Length ?? 0;
            EditorGUILayout.LabelField(
                n == 1 ? Loc.T("1 problema diagnosticado", "1 issue diagnosed", "1 problema diagnosticado")
                       : Loc.T($"{n} problemas diagnosticados", $"{n} issues diagnosed", $"{n} problemas diagnosticados"),
                EditorStyles.boldLabel);

            if (GeminiRestBridge.LastReportUsedFallback)
                EditorGUILayout.HelpBox(Loc.T(
                    "O modelo preview estava sobrecarregado (503) — este laudo foi gerado " +
                    "automaticamente com o Gemini 2.5 Flash (estável).",
                    "The preview model was overloaded (503) — this report was generated " +
                    "automatically with Gemini 2.5 Flash (stable).",
                    "El modelo preview estaba sobrecargado (503): este informe se generó " +
                    "automáticamente con Gemini 2.5 Flash (estable)."),
                    MessageType.None);
            EditorGUILayout.Space(4);

            // MOTIVO: scroll externo cobrindo todos os findings — sem isso, com 3
            // findings a janela passa do tamanho útil e o usuário não consegue ler tudo.
            _reportScroll = EditorGUILayout.BeginScrollView(_reportScroll);
            if (_lastReport.findings != null)
            {
                for (int i = 0; i < _lastReport.findings.Length; i++)
                {
                    DrawFinding(i, _lastReport.findings[i]);
                }
            }
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_lastReport.dicas_level_design))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField(Loc.T("Dicas de Level Design", "Level design tips", "Consejos de diseño de niveles"),
                                           EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    _lastReport.dicas_level_design,
                    EditorStyles.wordWrappedLabel);
            }
        }
    }
}
