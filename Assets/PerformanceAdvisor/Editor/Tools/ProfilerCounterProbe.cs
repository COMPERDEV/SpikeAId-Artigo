using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Diagnostic tool: enumerates every Profiler counter this Unity version and
    /// render pipeline actually expose, samples them for a while and dumps a CSV.
    /// </summary>
    /// <remarks>
    /// MOTIVO: o plugin lia draw calls tentando 3 nomes de contador conhecidos e
    /// ficando com o que respondesse. Medindo na StressTestAllIn (Unity 6.4, URP,
    /// DX12) os três deram 0 enquanto o contador de troca de estado marcava ~84 — e 84
    /// trocas de estado com 0 draws é impossível, então o problema era o NOME do
    /// contador, não a cena. Chutar um quarto nome repetiria o erro; esta sonda
    /// lista o que existe de fato, via ProfilerRecorderHandle.GetAvailable().
    ///
    /// É ferramenta de investigação, não parte do fluxo do usuário. Fica fora das
    /// abas e pode ser removida quando a etapa fechar — o CSV que ela gera é que
    /// vira o registro de evidência.
    /// </remarks>
    public static class ProfilerCounterProbe
    {
        private const int SAMPLE_FRAMES = 300;

        private static readonly List<ProfilerRecorder> _recorders = new List<ProfilerRecorder>();
        private static readonly List<string> _names = new List<string>();
        private static readonly List<string> _categories = new List<string>();

        private static StringBuilder _csv;
        private static int _frame;
        private static bool _running;

        /// <summary>
        /// Lists every available counter without sampling. Cheap — answers
        /// "does a draw-call counter exist at all here?" in one click.
        /// </summary>
        [MenuItem("Tools/Performance Advisor/Diagnostics/List available counters", false, 200)]
        public static void ListAvailableCounters()
        {
            // MOTIVO: GetAvailable preenche uma lista fornecida e devolve void —
            // não retorna coleção.
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);

            var sb = new StringBuilder(8192);
            int n = 0;

            var lines = new List<string>();
            foreach (var h in handles)
            {
                if (!h.Valid) continue;
                ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
                lines.Add($"[{d.Category.Name}] {d.Name}");
                n++;
            }
            lines.Sort(System.StringComparer.OrdinalIgnoreCase);

            sb.Append(n).Append(Loc.T(" contadores disponíveis nesta versão/pipeline:\n",
                                      " counters available in this version/pipeline:\n",
                                      " contadores disponibles en esta versión/pipeline:\n"));
            for (int i = 0; i < lines.Count; i++) sb.Append(lines[i]).Append('\n');

            string path = WriteToDisk("profiler-counters-disponiveis.txt", sb.ToString());
            Debug.Log("[Performance Advisor] " + Loc.T($"{n} contadores listados → {path}",
                                                       $"{n} counters listed → {path}",
                                                       $"{n} contadores listados → {path}"));
            EditorUtility.RevealInFinder(path);
        }

        /// <summary>
        /// Samples every Render-category counter for <see cref="SAMPLE_FRAMES"/> frames
        /// and writes a CSV. Requires Play Mode — counters only tick while rendering.
        /// </summary>
        [MenuItem("Tools/Performance Advisor/Diagnostics/Probe render counters (CSV)", false, 201)]
        public static void ProbeRenderCounters()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Performance Advisor",
                    Loc.T("Entre em Play antes de sondar — os contadores de render só avançam enquanto a cena renderiza.",
                          "Enter Play before probing — render counters only advance while the scene renders.",
                          "Entra en Play antes de sondear: los contadores de render solo avanzan mientras la escena renderiza."),
                    "Ok");
                return;
            }
            if (_running) return;

            StopInternal();

            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);

            foreach (var h in handles)
            {
                if (!h.Valid) continue;
                ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);

                // MOTIVO: Render e Memory são as categorias que o plugin lê — é onde vale olhar
                string cat = d.Category.Name;
                if (cat != "Render" && cat != "Memory") continue;

                _recorders.Add(new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default));
                _names.Add(d.Name);
                _categories.Add(cat);
            }

            for (int i = 0; i < _recorders.Count; i++)
            {
                var r = _recorders[i];
                r.Start();
                _recorders[i] = r;
            }

            _csv = new StringBuilder(64 * 1024);
            _csv.Append("frame");
            for (int i = 0; i < _names.Count; i++)
                _csv.Append(';').Append(_categories[i]).Append(" / ").Append(_names[i]);
            _csv.Append('\n');

            _frame = 0;
            _running = true;
            EditorApplication.update += Sample;

            Debug.Log("[Performance Advisor] " + Loc.T(
                $"Sondando {_recorders.Count} contadores por {SAMPLE_FRAMES} frames…",
                $"Probing {_recorders.Count} counters for {SAMPLE_FRAMES} frames…",
                $"Sondeando {_recorders.Count} contadores durante {SAMPLE_FRAMES} frames…"));
        }

        private static void Sample()
        {
            if (!_running) return;

            // Para sozinho se o dev sair do Play no meio — sem isso os recorders
            // continuariam vivos segurando handle nativo.
            if (!EditorApplication.isPlaying)
            {
                Finish("interrompido (saiu do Play)");
                return;
            }

            _csv.Append(_frame.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < _recorders.Count; i++)
                _csv.Append(';').Append(_recorders[i].LastValue.ToString(CultureInfo.InvariantCulture));
            _csv.Append('\n');

            _frame++;
            if (_frame >= SAMPLE_FRAMES) Finish("completo");
        }

        private static void Finish(string reason)
        {
            EditorApplication.update -= Sample;
            _running = false;

            string path = WriteToDisk("profiler-counters-render.csv", _csv.ToString());
            int counters = _recorders.Count;
            StopInternal();

            Debug.Log("[Performance Advisor] " + Loc.T(
                $"Sonda {reason}: {_frame} frames × {counters} contadores → {path}",
                $"Probe {reason}: {_frame} frames × {counters} counters → {path}",
                $"Sonda {reason}: {_frame} frames × {counters} contadores → {path}"));
            EditorUtility.RevealInFinder(path);
        }

        private static void StopInternal()
        {
            // MOTIVO: ProfilerRecorder é struct com handle nativo — Dispose obrigatório,
            // senão vaza até o próximo domain reload (mesma regra do Porteiro).
            for (int i = 0; i < _recorders.Count; i++)
            {
                if (_recorders[i].Valid) _recorders[i].Dispose();
            }
            _recorders.Clear();
            _names.Clear();
            _categories.Clear();
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
