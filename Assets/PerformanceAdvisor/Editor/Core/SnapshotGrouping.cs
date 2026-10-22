using System.Collections.Generic;

namespace PerformanceAdvisor
{
    /// <summary>
    /// One distinct root cause, plus how many frames showed it.
    /// </summary>
    public struct SnapshotGroup
    {
        /// <summary>Worst frame of the group — the one sent to the LLM.</summary>
        public FrameSnapshot Representative;

        /// <summary>How many captured frames collapsed into this group.</summary>
        public int Occurrences;

        public int FirstFrame;
        public int LastFrame;

        /// <summary>What made these frames the same problem. Empty when the type has no signal.</summary>
        public string Cause;

        /// <summary>
        /// Ids of every frame collapsed into this group, representative included.
        /// </summary>
        /// <remarks>
        /// MOTIVO: a seleção da UI é por GRUPO, mas o set de desmarcados guarda Ids. Sem a
        /// lista completa, desmarcar um grupo tirava só o representante — os demais frames
        /// continuavam selecionados, o agrupamento rodava de novo no envio e **elegia um
        /// novo representante entre eles**, fazendo o tipo inteiro ser analisado mesmo
        /// depois de desmarcado.
        /// </remarks>
        public List<int> MemberIds;
    }

    /// <summary>
    /// Adapts the Detetive's per-cause slots to the shape the UI and the payload use.
    /// </summary>
    /// <remarks>
    /// MOTIVO: a unidade que interessa não é o tipo nem o frame — é a **causa raiz
    /// distinta**. A deduplicação acontece na CAPTURA, no Detetive: cada slot do buffer
    /// guarda uma causa, com o pior frame como representante e a contagem de ocorrências.
    /// Este arquivo só adapta aquilo para a forma que a UI e o envio consomem.
    /// **Limite do dado, não da implementação:** só CPU e GC têm culpado. Gargalo de
    /// renderização e de memória não tem método C# responsável — o campo vem null. Para
    /// esses tipos não existe sinal que separe um snapshot do outro, então todos colapsam
    /// num grupo só, o que é a verdade: são o mesmo problema N vezes.
    /// </remarks>
    public static class SnapshotGrouping
    {
        /// <summary>
        /// Adapts stored snapshots to the group shape the UI consumes.
        /// </summary>
        /// <remarks>
        /// MOTIVO: a deduplicação migrou para a CAPTURA. O Detetive já guarda um slot por
        /// causa distinta, com o pior frame como representante e a contagem de ocorrências
        /// no próprio snapshot — então aqui não há mais o que agrupar, só o que adaptar.
        ///
        /// A versão anterior agrupava na exibição, e isso mascarava o defeito real: como o
        /// buffer era um anel puro de 10 FRAMES, os 10 slots enchiam com frames
        /// consecutivos do problema mais comum e despejavam as causas raras antes mesmo de
        /// chegarem aqui. Agrupar depois não recupera o que já foi sobrescrito.
        /// </remarks>
        public static List<SnapshotGroup> Group(FrameSnapshot[] samples)
        {
            var result = new List<SnapshotGroup>();
            if (samples == null || samples.Length == 0) return result;

            for (int i = 0; i < samples.Length; i++)
            {
                FrameSnapshot s = samples[i];
                result.Add(new SnapshotGroup
                {
                    Representative = s,
                    Occurrences    = s.Occurrences > 0 ? s.Occurrences : 1,
                    FirstFrame     = s.FirstFrame > 0 ? s.FirstFrame : s.FrameIndex,
                    LastFrame      = s.LastFrame  > 0 ? s.LastFrame  : s.FrameIndex,
                    Cause          = s.CauseKey ?? "",
                    MemberIds      = new List<int> { s.Id }
                });
            }

            return result;
        }

        /// <summary>Flattens groups back to the representatives, for sending.</summary>
        public static FrameSnapshot[] Representatives(List<SnapshotGroup> groups)
        {
            var arr = new FrameSnapshot[groups.Count];
            for (int i = 0; i < groups.Count; i++) arr[i] = groups[i].Representative;
            return arr;
        }
    }
}
