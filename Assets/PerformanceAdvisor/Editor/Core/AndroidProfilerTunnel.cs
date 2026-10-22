using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Opens the adb tunnel an Android player needs to reach the Profiler, and points the
    /// Profiler at it.
    /// </summary>
    /// <remarks>
    /// MOTIVO: no Android a build subia e rodava no aparelho, e o plugin não via conexão
    /// nenhuma — o diálogo de diagnóstico listava só "Editor". Não era o filtro: o player
    /// realmente não estava se oferecendo.
    ///
    /// A razão está na documentação da Unity: o Editor cria o túnel adb **quando você usa
    /// o Build &amp; Run da janela de Build Settings**. Chamando
    /// <c>BuildPipeline.BuildPlayer</c> por script, esse pós-processamento não acontece —
    /// a build instala e executa, mas nada liga a porta do aparelho ao Editor.
    ///
    /// No PC isso não apareceu porque o player standalone se anuncia sozinho na rede
    /// local. USB não tem esse mecanismo: alguém precisa fazer o encaminhamento.
    ///
    ///     adb forward tcp:34999 localabstract:Unity-&lt;package&gt;
    ///
    /// Depois disso o alvo existe como "AndroidProfiler(ADB@127.0.0.1:34999)" e o
    /// <see cref="ProfilerDriver"/> consegue se conectar.
    /// </remarks>
    public static class AndroidProfilerTunnel
    {
        // MOTIVO: 34999 é a porta que a Unity documenta para esse encaminhamento. Não é
        // escolha nossa — mudá-la faria o Editor procurar num lugar onde ninguém escuta.
        private const int PORT = 34999;

        // Último encaminhamento registrado, para não repetir o log a cada reconexão.
        private static string _lastForward;

        /// <summary>Forwards the port and connects the Profiler. Returns false with a reason.</summary>
        public static bool OpenAndConnect(out string error)
        {
            string adb = FindAdb();
            if (adb == null)
            {
                error = Loc.T("Não encontrei o adb. Ele vem com o módulo Android Build Support " +
                              "(Unity Hub → Installs → Add modules) ou pode ser apontado em " +
                              "Preferences → External Tools → Android SDK.",
                              "adb not found. It comes with the Android Build Support module " +
                              "(Unity Hub → Installs → Add modules) or can be set in " +
                              "Preferences → External Tools → Android SDK.",
                              "No encontré adb. Viene con el módulo Android Build Support " +
                              "(Unity Hub → Installs → Add modules) o se puede indicar en " +
                              "Preferences → External Tools → Android SDK.");
                return false;
            }

            // MOTIVO: sem aparelho na lista, o forward "funciona" e o túnel aponta para o
            // nada — o sintoma vira "conectou mas não chega frame", que é bem mais difícil
            // de diagnosticar do que "não há aparelho".
            if (!HasDevice(adb, out error)) return false;

            string package = PlayerSettings.applicationIdentifier;
            if (string.IsNullOrEmpty(package))
            {
                error = Loc.T("O Package Name do projeto está vazio (Project Settings → Player → " +
                              "Android → Package Name). O túnel adb é identificado por ele.",
                              "The project's Package Name is empty (Project Settings → Player → " +
                              "Android → Package Name). The adb tunnel is identified by it.",
                              "El Package Name del proyecto está vacío (Project Settings → Player → " +
                              "Android → Package Name). El túnel adb se identifica por él.");
                return false;
            }

            if (!RunAdb(adb, $"forward tcp:{PORT} localabstract:Unity-{package}", out string output))
            {
                error = Loc.T("Falha ao criar o túnel adb: ", "Failed to create the adb tunnel: ",
                              "Error al crear el túnel adb: ") + output;
                return false;
            }

            // MOTIVO: criar o túnel não seleciona o alvo — quem aponta a janela é o
            // Editor, e ele mantém o alvo anterior. Foi o mesmo problema que já apareceu
            // no standalone. Aqui o endereço é conhecido, então dá pra conectar direto.
            ProfilerDriver.DirectURLConnect($"127.0.0.1:{PORT}");
            ProfilerDriver.enabled = true;

            // MOTIVO: o log só sai quando o encaminhamento MUDA. Ele é refeito a cada
            // tentativa de reconexão, e repetir a mesma linha a cada 3 s encheria o
            // console de ruído — foi o que aconteceu na primeira versão, dez linhas
            // idênticas no mesmo segundo, escondendo qualquer mensagem útil.
            string forwardId = package + ":" + PORT;
            if (_lastForward != forwardId)
            {
                _lastForward = forwardId;
                // MOTIVO: "túnel aberto" sozinho não diz nada — o forward tem sucesso mesmo
                // sem ninguém escutando. O teste com o APK parou exatamente aqui, sem pista
                // do motivo. Registrar o estado do APARELHO e o alvo do PROFILER na mesma
                // linha separa as três causas possíveis: app morto, app vivo sem conexão, ou
                // conexão feita com outro alvo.
                string appState = IsAppRunning(out string pid)
                    ? Loc.T($"app rodando (PID {pid})", $"app running (PID {pid})", $"app en ejecución (PID {pid})")
                    : Loc.T("app NÃO está rodando no aparelho", "app is NOT running on the device",
                            "la app NO se está ejecutando en el dispositivo");
                string alvo = ProfilerDriver.GetConnectionIdentifier(ProfilerDriver.connectedProfiler);
                Debug.Log("[Performance Advisor] " + Loc.T(
                    $"Túnel adb aberto na porta {PORT} para {package} — {appState}; alvo do Profiler: {alvo}.",
                    $"adb tunnel open on port {PORT} for {package} — {appState}; Profiler target: {alvo}.",
                    $"Túnel adb abierto en el puerto {PORT} para {package}: {appState}; objetivo del Profiler: {alvo}."));
            }
            error = null;
            return true;
        }


        /// <summary>True when the built app has a live process on the device.</summary>
        /// <remarks>
        /// MOTIVO: <c>adb forward</c> tem sucesso mesmo que NADA esteja escutando do outro
        /// lado — ele só registra o encaminhamento, não verifica o destino. Então "túnel
        /// aberto" não prova nada sobre o app, e o sintoma vira "conectou e não chega
        /// frame", que é o pior tipo de falha: parece que funcionou.
        ///
        /// Perguntar o PID separa as duas causas que pedem ações opostas: app fechado ou
        /// travado (reabrir) versus app rodando mas sem conexão (problema de túnel).
        /// </remarks>
        public static bool IsAppRunning(out string pid)
        {
            pid = null;
            string adb = FindAdb();
            if (adb == null) return false;

            string package = PlayerSettings.applicationIdentifier;
            if (string.IsNullOrEmpty(package)) return false;

            if (!RunAdb(adb, $"shell pidof {package}", out string output)) return false;

            string t = output.Trim();
            if (string.IsNullOrEmpty(t)) return false;

            pid = t;
            return true;
        }

        /// <summary>Human-readable reason the device side is not ready, or null.</summary>
        /// <remarks>
        /// MOTIVO: o Android SUSPENDE app em segundo plano. Se o dev builda e volta pro
        /// PC, a tela do aparelho bloqueia ou o app sai de foco — e app suspenso não
        /// renderiza nem envia frame, mesmo com processo vivo e túnel aberto. É a causa
        /// mais provável de "tudo certo e nada chega", e nenhuma mensagem genérica de
        /// erro levaria o dev até ela.
        /// </remarks>
        public static string DescribeDeviceState()
        {
            if (!IsAppRunning(out string pid))
                return Loc.T("O app NÃO está rodando no aparelho. Abra-o na tela do celular — " +
                             "ele pode ter sido fechado, ter travado, ou nunca ter subido.",
                             "The app is NOT running on the device. Open it on the phone's screen — " +
                             "it may have been closed, crashed, or never started.",
                             "La app NO se está ejecutando en el dispositivo. Ábrela en la pantalla del teléfono: " +
                             "puede haberse cerrado, colgado o no haber arrancado nunca.");

            return Loc.T($"O app está rodando (PID {pid}). Se mesmo assim não chega frame, deixe-o " +
                         "em PRIMEIRO PLANO e com a tela ligada: o Android suspende app em segundo " +
                         "plano, e app suspenso não envia telemetria.",
                         $"The app is running (PID {pid}). If frames still don't arrive, keep it " +
                         "in the FOREGROUND with the screen on: Android suspends background apps, " +
                         "and a suspended app sends no telemetry.",
                         $"La app se está ejecutando (PID {pid}). Si aun así no llegan frames, déjala " +
                         "en PRIMER PLANO y con la pantalla encendida: Android suspende las apps en segundo " +
                         "plano, y una app suspendida no envía telemetría.");
        }

        /// <summary>True when adb lists at least one device in "device" state.</summary>
        private static bool HasDevice(string adb, out string error)
        {
            if (!RunAdb(adb, "devices", out string output))
            {
                error = Loc.T("Não consegui rodar 'adb devices': ", "Could not run 'adb devices': ",
                              "No pude ejecutar 'adb devices': ") + output;
                return false;
            }

            // Saída: cabeçalho + uma linha por aparelho. Só "device" conta — "unauthorized"
            // significa que falta aceitar a depuração USB na tela do aparelho, e
            // "offline" que ele está no cabo mas não respondendo.
            foreach (string line in output.Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0 || t.StartsWith("List of devices")) continue;

                if (t.EndsWith("\tdevice") || t.EndsWith(" device")) { error = null; return true; }

                if (t.Contains("unauthorized"))
                {
                    error = Loc.T("O aparelho está conectado mas NÃO autorizado. Desbloqueie a tela e " +
                                  "aceite o pedido de depuração USB que aparece nele.",
                                  "The device is connected but NOT authorized. Unlock the screen and " +
                                  "accept the USB debugging prompt that shows up on it.",
                                  "El dispositivo está conectado pero NO autorizado. Desbloquea la pantalla y " +
                                  "acepta la solicitud de depuración USB que aparece en él.");
                    return false;
                }
            }

            error = Loc.T("Nenhum aparelho Android em 'adb devices'. Confirme o cabo e a depuração " +
                          "USB ligada nas Opções do desenvolvedor.",
                          "No Android device in 'adb devices'. Check the cable and that USB debugging " +
                          "is on in Developer options.",
                          "Ningún dispositivo Android en 'adb devices'. Revisa el cable y que la depuración " +
                          "USB esté activada en Opciones de desarrollador.");
            return false;
        }

        /// <summary>Locates adb, preferring the SDK the Editor is configured to use.</summary>
        private static string FindAdb()
        {
            // MOTIVO: a preferência do Editor ganha do caminho embutido — quem apontou um
            // SDK próprio em External Tools tem motivo, e usar outro adb pode falar com
            // um servidor adb diferente do que o aparelho está pareado.
            string root = EditorPrefs.GetString("AndroidSdkRoot", "");
            string candidate = Combine(root);
            if (candidate != null) return candidate;

            // Fallback: o SDK que vem com o módulo Android do próprio Editor.
            string editorSdk = Path.Combine(
                Path.GetDirectoryName(EditorApplication.applicationPath) ?? "",
                "Data", "PlaybackEngines", "AndroidPlayer", "SDK");
            return Combine(editorSdk);
        }

        private static string Combine(string sdkRoot)
        {
            if (string.IsNullOrEmpty(sdkRoot)) return null;
            string exe = Path.Combine(sdkRoot, "platform-tools",
                                      Application.platform == RuntimePlatform.WindowsEditor ? "adb.exe" : "adb");
            return File.Exists(exe) ? exe : null;
        }

        private static bool RunAdb(string adb, string args, out string output)
        {
            try
            {
                var psi = new ProcessStartInfo(adb, args)
                {
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };

                using (Process p = Process.Start(psi))
                {
                    if (p == null)
                    {
                        output = Loc.T("não foi possível iniciar o processo", "could not start the process",
                                       "no se pudo iniciar el proceso");
                        return false;
                    }

                    string so = p.StandardOutput.ReadToEnd();
                    string se = p.StandardError.ReadToEnd();
                    // MOTIVO: prazo curto — adb forward e adb devices respondem em
                    // milissegundos. Travar o Editor esperando um adb pendurado seria
                    // pior que falhar e dizer o motivo.
                    if (!p.WaitForExit(10000))
                    {
                        try { p.Kill(); } catch { /* já morreu */ }
                        output = Loc.T("o adb não respondeu em 10s", "adb did not answer within 10s",
                                       "adb no respondió en 10 s");
                        return false;
                    }

                    output = string.IsNullOrEmpty(so) ? se : so;
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return false;
            }
        }
    }
}
