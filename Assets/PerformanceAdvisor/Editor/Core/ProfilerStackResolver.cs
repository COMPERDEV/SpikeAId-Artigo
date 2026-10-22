using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.Profiling;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Lê os dados já gravados pelo Profiler do Unity para identificar os
    /// markers (Scripts→Métodos) responsáveis pelos gargalos do último frame.
    /// </summary>
    /// <remarks>
    /// MOTIVO: o orientador pediu que o laudo mostre quais métodos C# causaram
    /// o gargalo. Usamos <see cref="ProfilerDriver"/> + <see cref="RawFrameDataView"/>
    /// (API interna do Editor) por ser a fonte mais barata de dados reais —
    /// não exige Deep Profile e funciona em qualquer cena.
    ///
    /// Risco aceito: <c>UnityEditorInternal</c> é API interna e pode mudar entre
    /// versões. Aceitável para plugin Editor-only em Unity 6.
    /// </remarks>
    public static class ProfilerStackResolver
    {
        // MOTIVO: 3 é suficiente pra dar contexto ao Gemini sem inflar o payload.
        private const int TOP_N = 3;

        private static bool _sessionActive;
        private static bool _wasEnabledBeforeSession;

        /// <summary>True enquanto a sessão de captura segura o Profiler ligado.</summary>
        public static bool SessionActive => _sessionActive;

        /// <summary>
        /// Guarda o estado anterior de <see cref="ProfilerDriver.enabled"/> e força true.
        /// Chamar no início do monitoramento. Idempotente.
        /// </summary>
        public static void EnableForSession()
        {
            if (_sessionActive) return;
            _wasEnabledBeforeSession = ProfilerDriver.enabled;
            ProfilerDriver.enabled = true;
            _sessionActive = true;
        }

        /// <summary>
        /// Restaura o estado de <see cref="ProfilerDriver.enabled"/> que existia antes
        /// de <see cref="EnableForSession"/>. Idempotente.
        /// </summary>
        public static void RestoreSession()
        {
            if (!_sessionActive) return;
            ProfilerDriver.enabled = _wasEnabledBeforeSession;
            _sessionActive = false;
        }

        /// <summary>
        /// Lê o último frame disponível na main thread e retorna uma string formatada
        /// com os top-N markers por tempo de CPU e o GC.Alloc mais notável.
        /// Retorna <c>null</c> nas duas saídas se o frame não estiver disponível.
        /// </summary>
        /// <remarks>
        /// Aloca apenas quando chamado — não está no hot-path. O chamador (Detetive)
        /// faz throttle para não disparar todo frame anômalo.
        /// </remarks>
        public static void ResolveCulprits(out string cpuTop, out string gcTop,
                                           out string cpuKey, out string gcKey)
        {
            cpuTop = null;
            cpuKey = null;
            gcKey  = null;
            gcTop = null;

            int lastFrame = ProfilerDriver.lastFrameIndex;
            if (lastFrame < 0) return;

            // threadIndex 0 = Main Thread, sempre disponível.
            using (var view = ProfilerDriver.GetRawFrameDataView(lastFrame, 0))
            {
                if (!view.valid) return;
                int sampleCount = view.sampleCount;
                if (sampleCount <= 1) return;

                // Top-N CPU por tempo inclusivo (ns). Insertion sort fixo em TOP_N
                // evita alocar uma List<>.
                string[] topNames = new string[TOP_N];
                long[]   topTimes = new long[TOP_N];

                // Track parent script marker enquanto andamos pelos samples — quando
                // batemos num GC.Alloc, atribuímos a culpa ao último marker "real"
                // (não-builtin) que vimos antes dele.
                string lastScriptMarker = null;
                string gcCulprit = null;

                // index 0 é a raiz ("PlayerLoop" / "Frame") — pular pra não dominar o top.
                for (int i = 1; i < sampleCount; i++)
                {
                    string name = view.GetSampleName(i);
                    if (string.IsNullOrEmpty(name)) continue;

                    // MOTIVO: GetSampleTimeNs retorna ulong; tempo de um sample dentro
                    // de um frame nunca chega perto de long.MaxValue, cast é seguro.
                    long timeNs = (long)view.GetSampleTimeNs(i);

                    // GC.Alloc é um marker emitido pelo runtime sempre que o GC
                    // alocou nesse stack. O tempo dele é desprezível — o que interessa
                    // é o método que estava executando quando alocou.
                    if (name == "GC.Alloc")
                    {
                        if (gcCulprit == null && lastScriptMarker != null)
                        {
                            gcCulprit = lastScriptMarker;
                        }
                        continue;
                    }

                    // Heurística pra detectar markers de script: contém "." ou
                    // termina em "()". Filtra markers internos como "PlayerLoop",
                    // "EarlyUpdate", "BehaviourUpdate" etc.
                    bool looksLikeScript = name.IndexOf('.') >= 0 || name.EndsWith("()");
                    if (looksLikeScript)
                    {
                        lastScriptMarker = name;
                    }

                    if (timeNs <= 0) continue;

                    // MOTIVO: o top-N precisa do MESMO filtro que o culpado de GC usa. Sem
                    // esta linha o ranking considerava todo marcador, inclusive raízes do
                    // engine como "PlayerLoop" — e, como GetSampleTimeNs é tempo INCLUSIVO,
                    // o pai sempre vence o filho. O resultado era o laudo receber
                    // culprit_stack: "PlayerLoop", que é verdadeiro e inútil: não aponta
                    // linha de código nenhuma. Restringir a marcadores que parecem método
                    // faz o ranking cair em StressTestScene.Update e afins.
                    if (!looksLikeScript) continue;

                    // Top-N insertion sort
                    for (int k = 0; k < TOP_N; k++)
                    {
                        if (timeNs > topTimes[k])
                        {
                            for (int s = TOP_N - 1; s > k; s--)
                            {
                                topTimes[s] = topTimes[s - 1];
                                topNames[s] = topNames[s - 1];
                            }
                            topTimes[k] = timeNs;
                            topNames[k] = name;
                            break;
                        }
                    }
                }

                var sb = new StringBuilder(256);
                for (int k = 0; k < TOP_N; k++)
                {
                    if (topTimes[k] <= 0 || string.IsNullOrEmpty(topNames[k])) break;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(topNames[k]).Append(" (")
                      .Append((topTimes[k] * 1e-6).ToString("F2", CultureInfo.InvariantCulture))
                      .Append("ms)");
                }
                if (sb.Length > 0) cpuTop = sb.ToString();

                // MOTIVO: a CHAVE DE CAUSA sai daqui, não do Detetive. Ela é o marcador
                // dominante sem o tempo — "quem" causou, não "quanto custou naquele
                // frame". Calcular isto no caminho por frame alocaria uma string por
                // frame anômalo e quebraria o zero-alloc (RN-01); aqui sai uma vez por
                // resolve, que já é throttled, e o Detetive só compara referências.
                cpuKey = topNames[0];
                gcKey  = gcCulprit;

                if (gcCulprit != null)
                {
                    gcTop = $"{gcCulprit} → GC.Alloc";
                }
            }
        }
    }
}
