using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Builds and runs a development player already wired to the Profiler.
    /// </summary>
    /// <remarks>
    /// MOTIVO: medir na build exige três coisas marcadas juntas — Development Build,
    /// Autoconnect Profiler e a plataforma certa. Esquecer qualquer uma produz a MESMA
    /// tela de "nenhum frame recebido", e aí o dev não sabe se o plugin quebrou, se o
    /// aparelho não conectou ou se ele só esqueceu um checkbox.
    ///
    /// Deixar isso como lista de instruções foi o que a UI fazia antes, e é pedir que o
    /// usuário execute um procedimento de seis passos sem errar nenhum. O plugin sabe
    /// exatamente quais flags precisa; então ele que marque.
    ///
    /// <c>BuildOptions.ConnectWithProfiler</c> é o nome de API do "Autoconnect Profiler"
    /// que aparece na janela de Build Profiles — mesma flag, nome diferente.
    /// </remarks>
    public static class ProfiledBuildLauncher
    {
        /// <summary>Is the platform module for this target actually installed?</summary>
        /// <remarks>
        /// MOTIVO: sem o módulo, BuildPlayer falha com erro cru no console. O caso é
        /// comum e tem solução simples (Unity Hub → Add modules), então vale detectar e
        /// dizer, em vez de deixar o dev decifrar a exceção.
        /// </remarks>
        public static bool IsSupported(BuildTarget target)
        {
            return BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target);
        }

        /// <summary>Human-readable reason the build cannot start, or null when it can.</summary>
        public static string Blocker(BuildTarget target)
        {
            if (!IsSupported(target))
                return Loc.T($"O suporte a {target} não está instalado neste Unity. " +
                             "Unity Hub → Installs → engrenagem → Add modules.",
                             $"{target} support is not installed in this Unity. " +
                             "Unity Hub → Installs → gear icon → Add modules.",
                             $"El soporte para {target} no está instalado en este Unity. " +
                             "Unity Hub → Installs → engranaje → Add modules.");

            if (EnabledScenes().Count == 0)
                return Loc.T("Nenhuma cena marcada em Build Profiles → Scene List. " +
                             "A build abriria vazia e todos os contadores ficariam em zero.",
                             "No scene ticked in Build Profiles → Scene List. " +
                             "The build would open empty and every counter would stay at zero.",
                             "Ninguna escena marcada en Build Profiles → Scene List. " +
                             "La build se abriría vacía y todos los contadores quedarían en cero.");

            return null;
        }

        /// <summary>
        /// Switches platform if needed, then builds and launches with the Profiler attached.
        /// </summary>
        public static void BuildAndRun(BuildTarget target)
        {
            string blocker = Blocker(target);
            if (blocker != null)
            {
                EditorUtility.DisplayDialog("Performance Advisor", blocker, "Ok");
                return;
            }

            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);

            // MOTIVO: trocar de plataforma reimporta o projeto inteiro e pode demorar
            // muito. É decisão do dev, não efeito colateral de clicar num botão.
            if (EditorUserBuildSettings.activeBuildTarget != target)
            {
                bool ok = EditorUtility.DisplayDialog(
                    "Performance Advisor",
                    Loc.T($"O projeto está em {EditorUserBuildSettings.activeBuildTarget} e a build precisa ser " +
                          $"{target}.\n\nTrocar de plataforma reimporta os assets e pode demorar vários minutos.",
                          $"The project is on {EditorUserBuildSettings.activeBuildTarget} and the build needs to be " +
                          $"{target}.\n\nSwitching platform reimports the assets and may take several minutes.",
                          $"El proyecto está en {EditorUserBuildSettings.activeBuildTarget} y la build tiene que ser " +
                          $"{target}.\n\nCambiar de plataforma reimporta los assets y puede tardar varios minutos."),
                    Loc.T("Trocar e continuar", "Switch and continue", "Cambiar y continuar"),
                    Loc.T("Cancelar", "Cancel", "Cancelar"));
                if (!ok) return;

                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                {
                    EditorUtility.DisplayDialog("Performance Advisor",
                        Loc.T($"Não foi possível trocar para {target}.", $"Could not switch to {target}.",
                              $"No se pudo cambiar a {target}."), "Ok");
                    return;
                }
            }

            string path = OutputPath(target);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var options = new BuildPlayerOptions
            {
                scenes           = EnabledScenes().ToArray(),
                locationPathName = path,
                target           = target,
                targetGroup      = group,
                // Development = habilita o Profiler no player.
                // ConnectWithProfiler = o "Autoconnect Profiler" da janela de build.
                // AllowDebugging = recomendado pela Unity junto dos dois acima para
                //   build automatizada que vai ser perfilada.
                // AutoRunPlayer = executa ao terminar, que é o ponto: medir rodando.
                options = BuildOptions.Development
                        | BuildOptions.ConnectWithProfiler
                        | BuildOptions.AllowDebugging
                        | BuildOptions.AutoRunPlayer
            };

            // MOTIVO: as BuildOptions valem para ESTA chamada, mas o pós-processamento de
            // Android lê as preferências persistentes do Editor. Sem espelhar aqui, a
            // build sai marcada como development e mesmo assim parte da preparação de
            // conexão não acontece.
            EditorUserBuildSettings.development    = true;
            EditorUserBuildSettings.connectProfiler = true;
            EditorUserBuildSettings.allowDebugging  = true;

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[Performance Advisor] " +
                          Loc.T("Build pronta e executando: ", "Build ready and running: ", "Build lista y en ejecución: ") + path);

                // MOTIVO: o fluxo pedia sete passos manuais depois daqui — achar a janela
                // do Profiler, conferir o alvo no dropdown, voltar ao plugin, trocar de
                // aba e clicar em Monitorar. No teste real a build subiu e rodou no
                // aparelho enquanto a tela continuava no Build, sem sinal nenhum de que
                // tinha funcionado.
                //
                // A conexão não existe no instante em que BuildPlayer retorna: o player
                // ainda está subindo, e no Android tem o adb no meio. Por isso quem liga
                // o Monitor é uma espera com prazo, não uma chamada direta.
                // MOTIVO: em Android o túnel adb NÃO é criado por BuildPipeline.BuildPlayer
                // — a Unity só faz isso no Build & Run da janela de Build Settings. Sem
                // ele o aparelho nunca aparece na lista de conexões, que foi exatamente o
                // que o diagnóstico mostrou: uma única conexão, "Editor".
                //
                // No standalone não precisa: o player se anuncia sozinho na rede local.
                if (target == BuildTarget.Android
                    && !AndroidProfilerTunnel.OpenAndConnect(out string tunnelError))
                {
                    Debug.LogWarning("[Performance Advisor] " + tunnelError);
                }
                PlayerAttachWatcher.Arm();
            }
            else
            {
                Debug.LogError("[Performance Advisor] " + Loc.T(
                    $"Build falhou ({summary.result}), {summary.totalErrors} erro(s). Veja o console acima.",
                    $"Build failed ({summary.result}), {summary.totalErrors} error(s). See the console above.",
                    $"La build falló ({summary.result}), {summary.totalErrors} error(es). Mira la consola arriba."));
            }
        }

        /// <summary>Scenes currently enabled in Build Profiles.</summary>
        private static List<string> EnabledScenes()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (s.enabled) scenes.Add(s.path);
            return scenes;
        }

        private static string OutputPath(BuildTarget target)
        {
            // Fora de Assets/ pra não virar asset importado. Pasta fixa por alvo, pra
            // rebuild sobrescrever em vez de acumular lixo.
            string root = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "Builds", "PerformanceAdvisor");

            switch (target)
            {
                case BuildTarget.Android:
                    return Path.Combine(root, "android", Application.productName + ".apk");
                default:
                    return Path.Combine(root, "windows", Application.productName + ".exe");
            }
        }

        /// <summary>The build target that matches a plugin platform selection.</summary>
        /// <remarks>
        /// MOTIVO: VR standalone (Quest) É Android — o Horizon OS é Android, e a build
        /// sai pelo mesmo alvo. Só PCVR usaria Windows, e esse caso o painel manda
        /// selecionar plataforma PC.
        /// </remarks>
        public static BuildTarget TargetFor(TargetPlatform platform)
        {
            switch (platform)
            {
                case TargetPlatform.Mobile:
                case TargetPlatform.VR:
                    return BuildTarget.Android;
                default:
                    return BuildTarget.StandaloneWindows64;
            }
        }
    }
}
