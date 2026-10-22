using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Waits for a built player to show up in the Profiler, selects it as the target and
    /// starts monitoring.
    /// </summary>
    /// <remarks>
    /// MOTIVO: a primeira versão disto era um <c>EditorApplication.update</c> estático
    /// registrado logo depois do build. Não funcionou, e o teste mostrou as duas razões:
    ///
    /// **1. Domain reload mata a espera.** Compilar para outra plataforma reimporta o
    /// projeto e recarrega o domínio C#, e isso apaga TODA a estática do Editor —
    /// inclusive a inscrição no update. No teste em Windows o player conectou
    /// normalmente (1386 frames chegando no Profiler) e não havia mais ninguém olhando.
    /// Por isso o prazo mora em <see cref="SessionState"/>, que sobrevive ao reload, e o
    /// vigia se re-registra sozinho via <c>[InitializeOnLoad]</c>.
    ///
    /// **2. A Unity não troca o alvo do Profiler sozinha.** Em Android, a build subiu e
    /// rodou no aparelho enquanto o dropdown do Profiler continuava em "Play Mode", com
    /// 0 frames. Esperar passivamente nunca ia dar certo: é preciso PROCURAR o alvo entre
    /// as conexões disponíveis e selecioná-lo.
    ///
    /// <see cref="SessionState"/> e não <see cref="EditorPrefs"/> de propósito: uma espera
    /// pendente não deve sobreviver a fechar o Unity. Se o dev voltar amanhã, não há build
    /// nenhuma rodando, e um vigia ressuscitado só produziria um aviso sem sentido.
    /// </remarks>
    [InitializeOnLoad]
    public static class PlayerAttachWatcher
    {
        private const string DEADLINE_KEY = "PerformanceAdvisor.AwaitPlayerUntil";

        static PlayerAttachWatcher()
        {
            // MOTIVO: o construtor estático roda a cada domain reload. Se havia uma
            // espera em curso, ela é retomada aqui — é o que faz a troca de plataforma
            // deixar de cancelar o fluxo pelas costas.
            if (SessionState.GetFloat(DEADLINE_KEY, 0f) > 0f)
                EditorApplication.update += Poll;
        }

        /// <summary>Starts (or restarts) waiting for a player to attach.</summary>
        public static void Arm(double timeoutSeconds = 180.0)
        {
            // MOTIVO do prazo folgado: entre clicar e a build conectar pode haver
            // reimportação de plataforma, gradle e instalação por adb. 90s não bastava.
            SessionState.SetFloat(DEADLINE_KEY, (float)(EditorApplication.timeSinceStartup + timeoutSeconds));

            EditorApplication.update -= Poll; // evita inscrição dupla
            EditorApplication.update += Poll;
        }

        private static void Disarm()
        {
            SessionState.EraseFloat(DEADLINE_KEY);
            EditorApplication.update -= Poll;
        }

        // MOTIVO do intervalo: o Poll roda a cada update do Editor, dezenas de vezes por
        // segundo. Na primeira versão ele chamava a abertura do túnel a cada passagem —
        // o console encheu com a mesma linha dez vezes no mesmo segundo, e cada uma
        // disparava dois processos de adb E um DirectURLConnect novo.
        //
        // Isso não era só barulho: reconectar o Profiler 60 vezes por segundo impede a
        // conexão de se estabelecer, porque ela é derrubada antes de completar. A
        // tentativa de conexão passa a ter intervalo, e a varredura (que é barata e só
        // lê estado) continua a cada passagem.
        private const double CONNECT_RETRY_SECONDS = 3.0;
        private static double _nextConnectAttempt;

        private static void Poll()
        {
            float deadline = SessionState.GetFloat(DEADLINE_KEY, 0f);
            if (deadline <= 0f) { EditorApplication.update -= Poll; return; }

            // Varredura barata: só lê a lista de conexões e compara nomes.
            bool ready = TelemetryMonitorCore.HasConnectedPlayer() || SelectPlayerTargetInList();

            // Caro: fala com o adb e reabre a conexão. Só de tempos em tempos.
            if (!ready
                && HardwareBuildSettings.ActivePlatform != TargetPlatform.PC
                && EditorApplication.timeSinceStartup >= _nextConnectAttempt)
            {
                _nextConnectAttempt = EditorApplication.timeSinceStartup + CONNECT_RETRY_SECONDS;
                if (AndroidProfilerTunnel.OpenAndConnect(out string tunnelError))
                    ready = TelemetryMonitorCore.HasConnectedPlayer() || SelectPlayerTargetInList();
                else if (!string.IsNullOrEmpty(tunnelError))
                    Debug.LogWarning("[Performance Advisor] " + tunnelError);
            }

            if (ready)
            {
                Disarm();

                OptimizerDashboardWindow.FocusMonitorTab();

                if (!TelemetryMonitorCore.IsMonitoring)
                    TelemetryMonitorCore.StartMonitoring();

                Debug.Log("[Performance Advisor] " +
                          Loc.T("Build conectada — monitorando ", "Build connected — monitoring ",
                                "Build conectada: monitoreando ") +
                          TelemetryMonitorCore.RemoteTargetName);
                return;
            }

            if (EditorApplication.timeSinceStartup > deadline)
            {
                Disarm();
                Debug.LogWarning(
                    "[Performance Advisor] " +
                    Loc.T("A build não apareceu no Profiler dentro do tempo.\n\nConexões vistas:\n",
                          "The build did not show up in the Profiler in time.\n\nConnections seen:\n",
                          "La build no apareció en el Profiler a tiempo.\n\nConexiones vistas:\n") +
                    DescribeAvailableTargets() + "\n" +
                    // MOTIVO: sem isto a mensagem era um beco — "não apareceu" não diz se
                    // o app morreu, se está suspenso ou se o túnel está errado, e as três
                    // pedem ações diferentes. Perguntar ao aparelho separa os casos.
                    (HardwareBuildSettings.ActivePlatform != TargetPlatform.PC
                        ? Loc.T("Estado no aparelho: ", "State on the device: ", "Estado en el dispositivo: ") +
                          AndroidProfilerTunnel.DescribeDeviceState()
                        : Loc.T("Confirme que a janela da build continua aberta.",
                                "Make sure the build window is still open.",
                                "Confirma que la ventana de la build sigue abierta.")));
            }
        }

        /// <summary>Every Profiler target right now, for when the search finds nothing.</summary>
        /// <remarks>
        /// MOTIVO: "Nenhuma build encontrada" é um beco sem saída — o dev vê o Profiler
        /// conectado, recebendo frames, e uma caixa dizendo que não há nada. Foi
        /// exatamente o que aconteceu quando o teste de identificador estava errado
        /// ("Player" contém "Play"), e não havia como descobrir isso pela tela.
        ///
        /// Listar o que O PLUGIN vê transforma o beco em diagnóstico: se o alvo certo
        /// está na lista e mesmo assim foi recusado, o problema é a regra, não a conexão.
        /// </remarks>
        public static string DescribeAvailableTargets()
        {
            int[] guids = ProfilerDriver.GetAvailableProfilers();
            if (guids == null || guids.Length == 0)
                return Loc.T("(nenhuma conexão visível)", "(no visible connection)", "(ninguna conexión visible)");

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < guids.Length; i++)
            {
                string id = ProfilerDriver.GetConnectionIdentifier(guids[i]);
                sb.Append("• ").Append(string.IsNullOrEmpty(id) ? Loc.T("(sem nome)", "(unnamed)", "(sin nombre)") : id);
                if (!TelemetryMonitorCore.IsPlayerIdentifier(id))
                    sb.Append(Loc.T("   [tratado como Editor]", "   [treated as Editor]", "   [tratado como Editor]"));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Opens the tunnel when needed, then picks the player target. For manual use.
        /// </summary>
        /// <remarks>
        /// MOTIVO da separação entre isto e <see cref="SelectPlayerTargetInList"/>: os
        /// dois passos têm custos muito diferentes. Varrer a lista é ler estado; abrir o
        /// túnel dispara dois processos de adb e refaz a conexão do Profiler.
        ///
        /// Na primeira versão estavam juntos, e o <c>Poll</c> chamava o conjunto a cada
        /// update do Editor. O resultado foi o console recebendo a mesma linha dez vezes
        /// no mesmo segundo — e, pior que o barulho, a conexão sendo derrubada e refeita
        /// dezenas de vezes por segundo, sem nunca chegar a se estabelecer.
        /// </remarks>
        public static bool TrySelectPlayerTarget()
        {
            if (HardwareBuildSettings.ActivePlatform != TargetPlatform.PC
                && !TelemetryMonitorCore.HasConnectedPlayer())
            {
                AndroidProfilerTunnel.OpenAndConnect(out _);
            }
            return SelectPlayerTargetInList();
        }

        /// <summary>
        /// Picks a connected player from the Profiler's target list. Reads state only.
        /// </summary>
        /// <remarks>
        /// MOTIVO: quem decide para onde a janela Profiler aponta é o Editor, e ele
        /// mantém o alvo anterior — o aparelho podia aparecer na lista sem ninguém
        /// selecioná-lo. Este passo é barato e pode rodar a cada frame.
        /// </remarks>
        public static bool SelectPlayerTargetInList()
        {
            int[] guids = ProfilerDriver.GetAvailableProfilers();
            if (guids == null) return false;

            for (int i = 0; i < guids.Length; i++)
            {
                string id = ProfilerDriver.GetConnectionIdentifier(guids[i]);
                if (!TelemetryMonitorCore.IsPlayerIdentifier(id)) continue;

                ProfilerDriver.connectedProfiler = guids[i];
                // MOTIVO: sem isso o alvo é trocado mas nenhum frame é gravado, e a tela
                // fica igual à de aparelho desconectado.
                ProfilerDriver.enabled = true;
                return true;
            }
            return false;
        }
    }
}
