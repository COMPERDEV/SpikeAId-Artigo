using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling.LowLevel;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Diagnostic tool: enumerates the Profiler counters a CONNECTED PLAYER actually
    /// exposes — an Android phone or a Meta Quest — and dumps them with their current
    /// values.
    /// </summary>
    /// <remarks>
    /// MOTIVO: o <see cref="ProfilerCounterProbe"/> lista os contadores do processo do
    /// EDITOR, via <c>ProfilerRecorderHandle.GetAvailable()</c>. Essa API é local por
    /// design — não enxerga player remoto. Então ela responde "o que existe no Windows
    /// com DX12", que não é a pergunta quando o alvo é Android com Vulkan.
    ///
    /// E a pergunta importa: no Unity 6/DX12 os três nomes de contador de draw call que
    /// o plugin tentava davam 0, e só a sonda local revelou que os reais eram outros
    /// ONZE. Assumir que no Android os nomes são os mesmos repetiria exatamente esse
    /// erro, num lugar onde ele é mais caro — o aparelho não está aqui para conferir a
    /// olho.
    ///
    /// Por isso esta sonda existe ANTES do Porteiro remoto, e não depois: ela é o que
    /// transforma "quais contadores o Android expõe" de suposição em dado.
    ///
    /// Caminho dos dados: <see cref="ProfilerDriver"/> segura a conexão (local ou
    /// remota) e <see cref="RawFrameDataView"/> lê os frames que chegaram por ela. Os
    /// contadores viajam no mesmo fluxo que os markers, marcados com
    /// <see cref="MarkerFlags.Counter"/> — é assim que a própria janela Profiler mostra
    /// Draw Calls e memória de um aparelho conectado.
    ///
    /// Pré-requisitos no aparelho (doc da Unity 6 e da Meta, iguais para celular e
    /// Quest, porque o Horizon OS é Android):
    ///  - build marcada como **Development Build** E **Autoconnect Profiler**;
    ///  - depuração USB ligada, aparelho visível em <c>adb devices</c>;
    ///  - o Build &amp; Run abre o túnel adb sozinho.
    ///
    /// É ferramenta de investigação, não parte do fluxo do usuário — mesma regra do
    /// <see cref="ProfilerCounterProbe"/>. O que vira registro de evidência é o arquivo
    /// que ela escreve em <c>Diagnostics/</c>.
    /// </remarks>
    public static class RemoteCounterProbe
    {
        /// <summary>
        /// Lists the connections the Editor can currently attach the Profiler to.
        /// </summary>
        /// <remarks>
        /// MOTIVO: antes de perguntar "quais contadores esse aparelho tem" é preciso
        /// saber se existe aparelho. Sem isso a sonda leria o Editor e devolveria os
        /// contadores do Windows achando que são do Android — falha silenciosa e
        /// convincente, o pior tipo.
        /// </remarks>
        [MenuItem("Tools/Performance Advisor/Diagnostics/List Profiler connections", false, 210)]
        public static void ListConnections()
        {
            var sb = new StringBuilder(1024);
            AppendConnections(sb);
            Debug.Log("[Performance Advisor] " + sb);
        }

        /// <summary>
        /// Dumps every counter the currently connected target exposes, with its value
        /// on the most recent frame.
        /// </summary>
        [MenuItem("Tools/Performance Advisor/Diagnostics/Probe connected device counters", false, 211)]
        public static void ProbeConnectedTarget()
        {
            var sb = new StringBuilder(16 * 1024);
            AppendConnections(sb);
            sb.Append('\n');

            // MOTIVO: sem ProfilerDriver.enabled não chega frame nenhum, e a sonda
            // devolveria "nenhum contador" — que se parece com "o Android não tem
            // contadores" e não com "o gravador estava desligado".
            bool wasEnabled = ProfilerDriver.enabled;
            ProfilerDriver.enabled = true;

            try
            {
                int lastFrame = ProfilerDriver.lastFrameIndex;
                if (lastFrame < 0)
                {
                    Report(sb, Loc.T("Nenhum frame recebido ainda.\n" +
                                     "Confira: a build está com Development Build + Autoconnect Profiler? " +
                                     "O app está EXECUTANDO no aparelho? O alvo certo está selecionado acima?",
                                     "No frame received yet.\n" +
                                     "Check: does the build have Development Build + Autoconnect Profiler? " +
                                     "Is the app RUNNING on the device? Is the right target selected above?",
                                     "Todavía no se recibió ningún frame.\n" +
                                     "Revisa: ¿la build tiene Development Build + Autoconnect Profiler? " +
                                     "¿La app se está EJECUTANDO en el dispositivo? ¿Está seleccionado arriba el objetivo correcto?"));
                    return;
                }

                // threadIndex 0 = Main Thread, sempre presente.
                using (RawFrameDataView view = ProfilerDriver.GetRawFrameDataView(lastFrame, 0))
                {
                    if (!view.valid)
                    {
                        Report(sb, Loc.T($"Frame {lastFrame} veio inválido — nada a listar.",
                                         $"Frame {lastFrame} came back invalid — nothing to list.",
                                         $"El frame {lastFrame} llegó inválido: nada que listar."));
                        return;
                    }

                    sb.Append("Frame lido: ").Append(lastFrame)
                      .Append("  (frameStartTimeNs=").Append(view.frameStartTimeNs).Append(")\n\n");

                    var markers = new List<FrameDataView.MarkerInfo>();
                    view.GetMarkers(markers);

                    var lines = new List<string>();
                    int counters = 0;

                    foreach (FrameDataView.MarkerInfo m in markers)
                    {
                        // MOTIVO: o fluxo traz markers E contadores misturados. Só os
                        // que carregam MarkerFlags.Counter têm valor por frame — é o
                        // que o Porteiro precisa ler.
                        if ((m.flags & MarkerFlags.Counter) == 0) continue;

                        string value = view.HasCounterValue(m.id)
                            ? view.GetCounterValueAsLong(m.id).ToString(CultureInfo.InvariantCulture)
                            : "(sem valor neste frame)";

                        lines.Add($"[{m.category}] {m.name} = {value}");
                        counters++;
                    }

                    lines.Sort(System.StringComparer.OrdinalIgnoreCase);

                    sb.Append(counters).Append(" contadores expostos por este alvo")
                      .Append(" (de ").Append(markers.Count).Append(" markers no total):\n");
                    for (int i = 0; i < lines.Count; i++) sb.Append(lines[i]).Append('\n');

                    Report(sb, null);
                }
            }
            finally
            {
                ProfilerDriver.enabled = wasEnabled;
            }
        }

        /// <summary>
        /// Writes the connection list into <paramref name="sb"/>, marking the active one.
        /// </summary>
        private static void AppendConnections(StringBuilder sb)
        {
            int active = ProfilerDriver.connectedProfiler;
            int[] guids = ProfilerDriver.GetAvailableProfilers();

            sb.Append(Loc.T("Conexões disponíveis (", "Available connections (", "Conexiones disponibles ("))
              .Append(guids.Length).Append("):\n");
            for (int i = 0; i < guids.Length; i++)
            {
                string id = ProfilerDriver.GetConnectionIdentifier(guids[i]);
                sb.Append(guids[i] == active ? "  → " : "    ")
                  .Append(id)
                  .Append(guids[i] == active ? Loc.T("   [CONECTADO]", "   [CONNECTED]", "   [CONECTADO]") : "")
                  .Append('\n');
            }

            // MOTIVO: "Editor" ou "Play Mode" como alvo ativo significa que a sonda vai
            // medir o Windows, não o aparelho. Dizer isso na cara evita que o resultado
            // seja lido como se fosse do Android.
            string activeName = ProfilerDriver.GetConnectionIdentifier(active);
            if (!string.IsNullOrEmpty(activeName)
                && (activeName.IndexOf("Editor", System.StringComparison.OrdinalIgnoreCase) >= 0
                 || activeName.IndexOf("Play", System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                sb.Append(Loc.T(
                    "\n⚠ O alvo ativo é o EDITOR, não um aparelho. O que sair daqui descreve\n" +
                    "  esta máquina. Selecione o aparelho em Window → Analysis → Profiler,\n" +
                    "  no dropdown de alvo (ex.: AndroidProfiler(ADB@127.0.0.1:34999)).\n",
                    "\n⚠ The active target is the EDITOR, not a device. What comes out of here describes\n" +
                    "  this machine. Select the device in Window → Analysis → Profiler,\n" +
                    "  in the target dropdown (e.g. AndroidProfiler(ADB@127.0.0.1:34999)).\n",
                    "\n⚠ El objetivo activo es el EDITOR, no un dispositivo. Lo que salga de aquí describe\n" +
                    "  esta máquina. Selecciona el dispositivo en Window → Analysis → Profiler,\n" +
                    "  en el desplegable de objetivo (p. ej., AndroidProfiler(ADB@127.0.0.1:34999)).\n"));
            }
        }

        private static void Report(StringBuilder sb, string problem)
        {
            if (problem != null) sb.Append(problem).Append('\n');

            string path = WriteToDisk("profiler-counters-aparelho.txt", sb.ToString());
            Debug.Log("[Performance Advisor] " + Loc.T("Sonda remota → ", "Remote probe → ", "Sonda remota → ") + path);
            EditorUtility.RevealInFinder(path);
        }

        private static string WriteToDisk(string fileName, string content)
        {
            // Fora de Assets/ pra não virar asset importado nem sujar o projeto.
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Diagnostics");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, fileName);
            File.WriteAllText(path, content, Encoding.UTF8);
            return path;
        }
    }
}
