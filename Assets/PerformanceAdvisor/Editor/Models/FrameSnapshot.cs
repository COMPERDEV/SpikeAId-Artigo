namespace PerformanceAdvisor
{
    /// <summary>
    /// Tipo de gargalo que disparou a captura do snapshot. Um mesmo frame pode
    /// gerar até 5 snapshots distintos (um por tipo violado), cada um com seu próprio Id.
    /// </summary>
    public enum SnapshotKind
    {
        Cpu,
        Gc,
        DrawCalls,
        // MOTIVO: unico tipo que representa trabalho da PLACA. Os outros quatro sao CPU
        // ou memoria — o plugin monitorava cinco grandezas e nenhuma era de GPU.
        Gpu,
        Memory
    }

    /// <summary>
    /// Single-frame telemetry record produced by the Porteiro and stored in the
    /// circular buffer of the Detetive. Declared as a struct (not a class) to keep
    /// the per-frame capture path heap-free — see RN-01 / "Proibições absolutas".
    /// </summary>
    public struct FrameSnapshot
    {
        // MOTIVO: Id monotônico global (atribuído na captura) — usado pela aba
        // Snapshots para manter a seleção do usuário estável mesmo quando o buffer
        // circular sobrescreve slots antigos.
        public int Id;

        public SnapshotKind Kind;

        public int FrameIndex;
        public float CpuLatencyMs;
        public long GcAllocatedBytes;

        // MOTIVO: tempo que a COLETA de lixo custou neste frame, medido pelos marcadores
        // GC.Collect e GarbageCollector.CollectIncremental. Bytes alocados dizem que houve
        // alocação; só este campo diz que houve custo. Antes o GC era capturado por
        // qualquer byte alocado num frame lento — 24 bytes viravam "problema de GC".
        public float GcCollectMs;
        public int DrawCallsCount;

        // MOTIVO: identidade da causa, vinda pronta do resolver. É por ela que o Detetive
        // decide se um frame anômalo é problema NOVO ou repetição de um já capturado.
        public string CauseKey;

        // Quantos frames colapsaram nesta causa, e em que intervalo.
        public int Occurrences;
        public int FirstFrame;
        public int LastFrame;
        public float GpuMs;
        public long MemoryUsedBytes;

        // MOTIVO: strings no struct são só referência copiada — preenchidas apenas
        // em frames anômalos, custo amortizado pela raridade do evento.
        public string CulpritCallStack;

        // Id da build ativa na captura (ex.: "q6600+ddr3-1333-4gb+gtx750ti").
        public string BuildId;
        public long TimestampMs;
    }
}
