using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PerformanceAdvisor
{
    /// <summary>
    /// Aba Build: seletor de plataforma (PC/Mobile/VR), painéis de seleção e
    /// resumo dos 4 budgets. Edita uma cópia da build e aplica via
    /// HardwareBuildSettings.Apply (que valida, persiste e notifica).
    /// </summary>
    [Serializable]
    public class BuildTab
    {
        private static readonly string[] PlatformNames = { "PC", "Mobile", "VR" };

        // Rótulos derivados da lista do catálogo — some se as opções mudarem lá.
        private static readonly string[] FpsNames = BuildFpsNames();

        private static string[] BuildFpsNames()
        {
            var opts = HardwareCatalog.TargetFpsOptions;
            var names = new string[opts.Length];
            for (int i = 0; i < opts.Length; i++) names[i] = $"{opts[i]} FPS";
            return names;
        }
        // MOTIVO: o filtro de CPU era por classe (Pentium/Dual core/i3/i5/…) num array
        // MANTIDO À MÃO em paralelo ao enum CpuClass, comparado por índice
        // (`(int)c.Class == _classFilter`). Qualquer mexida no enum desalinhava os dois em
        // silêncio — o filtro passaria a mostrar a classe errada sem erro de compilação.
        // E com o catálogo atual seriam 8 botões numa fileira só.
        //
        // Fabricante resolve os dois problemas: é o corte que a própria Steam publica
        // (Intel 54,5%, AMD 45,5%), cabe em três botões, e sai de uma propriedade do
        // dado em vez de um índice posicional.
        // MOTIVO: métodos e não arrays estáticos — os rótulos mudam com o idioma escolhido.
        private static string[] CpuVendorNames() => new[] { "Intel", "AMD", Loc.T("Todas", "All", "Todas") };
        private static string[] GpuVendorNames() => new[] { "NVIDIA", "AMD", Loc.T("Integrada", "Integrated", "Integrada"),
                                                            Loc.T("Todas", "All", "Todas") };
        private static string[] TestModeNames() => new[] {
            Loc.T("No Editor (Play Mode)", "In the Editor (Play Mode)", "En el Editor (Play Mode)"),
            Loc.T("Na build (medição real)", "On the build (real measurement)", "En la build (medición real)") };
        private const int CPU_VENDOR_COUNT = 3;
        private const int GPU_VENDOR_COUNT = 4;

        [SerializeField] private int _cpuVendor = 2; // "Todas"
        [SerializeField] private int _gpuVendor = 3; // "Todas"
        [SerializeField] private Vector2 _scroll;
        public void Draw()
        {
            var build = HardwareBuildSettings.GetActiveCopy();
            bool changed = false;

            // MOTIVO da REMOÇÃO das "Builds prontas": eram combinações montadas por nós
            // (esta CPU com esta GPU com esta RAM), e a Steam publica a distribuição de
            // cada peça SEPARADAMENTE, nunca a conjunta. Não existe fonte dizendo que
            // alguma dessas máquinas existe. Montar a build peça por peça, onde cada peça
            // tem procedência, é mais honesto do que oferecer atalhos que não têm.

            // ── Plataforma ───────────────────────────────────────────────
            var newPlatform = (TargetPlatform)GUILayout.Toolbar((int)build.Platform, PlatformNames);
            if (newPlatform != build.Platform) { build.Platform = newPlatform; changed = true; }

            // ── Onde medir ───────────────────────────────────────────────
            // MOTIVO: subiu pra cá, logo abaixo da plataforma, porque é a segunda decisão
            // que o dev toma e ela muda o significado de tudo que vem depois — quais
            // budgets existem, o que ganha veredito e se a memória quer dizer alguma
            // coisa. Estava no fim do painel, depois das peças, que é tarde demais.
            EditorGUILayout.Space(8);
            changed |= DrawTestModePanel(build);

            EditorGUILayout.Space(8);

            // ── FPS alvo ─────────────────────────────────────────────────
            // MOTIVO: NÃO é atributo do hardware, é decisão de projeto — por isso fica
            // separado das peças, pra não sugerir que uma CPU "tem" um FPS. O orçamento
            // de frame em ms sai de 1000/alvo (aritmética pura), e em VR o teto é a taxa
            // de atualização do headset, que é especificação de fabricante.
            EditorGUILayout.LabelField(Loc.T("Alvo do projeto", "Project target", "Objetivo del proyecto"), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                int cur = System.Array.IndexOf(HardwareCatalog.TargetFpsOptions, build.TargetFps);
                if (cur < 0) cur = 0;

                int sel = GUILayout.Toolbar(cur, FpsNames);
                if (sel != cur)
                {
                    int wanted  = HardwareCatalog.TargetFpsOptions[sel];
                    int applied = HardwareCatalog.ClampTargetFps(wanted, build.Platform, build.VrId);
                    build.TargetFps = applied;
                    changed = true;

                    if (applied != wanted)
                        Debug.Log("[PerformanceAdvisor] " + Loc.T(
                            $"{wanted} FPS não faz sentido neste headset — limitado a {applied} FPS, que é a taxa de atualização dele.",
                            $"{wanted} FPS makes no sense on this headset — capped at {applied} FPS, its refresh rate.",
                            $"{wanted} FPS no tiene sentido en este visor: limitado a {applied} FPS, su frecuencia de actualización."));
                }
            }

            // MOTIVO: a linha mostrava sempre "1000 ÷ FPS", mas em mobile o orçamento
            // aplicado é 65% disso (folga térmica publicada pela Unity). A conta exibida
            // tem que ser a conta usada, senão o resumo lá embaixo parece errado.
            float teorico = 1000f / Mathf.Max(1, build.TargetFps);
            string contaFps = build.Platform == TargetPlatform.Mobile
                ? $"1000 ÷ {build.TargetFps} = {teorico:F1} ms, × 65% " +
                  Loc.T("de folga térmica", "thermal headroom", "de margen térmico") +
                  $" = {teorico * HardwareCatalog.MOBILE_THERMAL_HEADROOM:F1} ms"
                : $"1000 ÷ {build.TargetFps} = {teorico:F1} ms";
            EditorGUILayout.LabelField(" ", Loc.T("orçamento de frame = ", "frame budget = ", "presupuesto de frame = ") + contaFps,
                                       EditorStyles.miniLabel);

            EditorGUILayout.Space(8);

            // ── Calibração: qual CPU é a DESTA máquina ───────────────────
            // MOTIVO: o teto de draw calls é derivado de um custo MEDIDO aqui, projetado
            // pro alvo pela razão entre índices PassMark publicados. A projeção precisa
            // saber de qual CPU partiu a medição — e isso o plugin não tem como adivinhar
            // de forma confiável a partir de SystemInfo.processorType.
            //
            // Só aparece quando há projeção: em Mobile e VR o veredito vem de medir no
            // aparelho, e a CPU desta máquina não entra em conta nenhuma.
            if (build.Platform == TargetPlatform.PC)
            {
                DrawDevCpuSelector();
                EditorGUILayout.Space(8);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            switch (build.Platform)
            {
                case TargetPlatform.Mobile: changed |= DrawMobilePanel(build); break;
                case TargetPlatform.VR:     changed |= DrawVrPanel(build);     break;
                default:                    changed |= DrawPcPanels(build);    break;
            }

            // Etapa 3: as configurações de render que vão na build DESTE alvo.
            DrawProjectSettingsSection(build);


            EditorGUILayout.EndScrollView();

            if (changed) HardwareBuildSettings.Apply(build);

            // ── Resumo dos budgets (sempre da build APLICADA) ────────────
            EditorGUILayout.Space(6);
            int fps = HardwareBuildSettings.TargetFps;
            EditorGUILayout.HelpBox(
                Loc.T("Esta build espera: ", "This build expects: ", "Esta build espera: ") +
                $"{fps} FPS ({HardwareBuildSettings.CpuBudgetMs:F1} ms/frame) · " +
                $"{DescribeDrawCalls(HardwareBuildSettings.DrawCallBudget)} · " +
                $"{DescribeMemory(HardwareBuildSettings.MemoryBudgetBytes)}",
                MessageType.Info);

            // MOTIVO: a procedência do teto fica ao lado do número, não escondida no
            // código — é o que permite o dev (e a banca) saber se aquele limiar foi
            // medido, publicado pelo fabricante ou ainda é estimativa.
            string src = HardwareBuildSettings.VerdictSource;
            if (!string.IsNullOrEmpty(src))
                NoticeBox.Draw(Loc.T("Veredito", "Verdict", "Veredicto") + $" — {src}",
                    HardwareBuildSettings.VerdictIncomplete ? NoticeLevel.Warning : NoticeLevel.Success);
        }

        /// <summary>
        /// Lets the developer state which catalog CPU is their own machine, so a measured
        /// draw-call cost can be projected onto the target hardware.
        /// </summary>
        /// <summary>
        /// Shows the machine's own performance index — silently when auto-detected,
        /// with a manual entry only when the built-in table has no match.
        /// </summary>
        /// <remarks>
        /// MOTIVO: quando a detecção funciona não há decisão a tomar, então não há razão
        /// pra ocupar a tela nem pra emitir alerta. A versão anterior mostrava um dropdown
        /// e dois avisos mesmo no caminho feliz — ruído puro.
        /// </remarks>
        private static void DrawDevCpuSelector()
        {
            // Roda o benchmark próprio uma vez por domínio se nunca houve medição.
            // MOTIVO: não há razão pra pedir ao dev que abra uma cena e rode — o plugin
            // renderiza os próprios draw calls num RenderTexture e se mede sozinho.
            DrawCallCalibrator.EnsureCalibrated();

            if (CpuPerformanceIndex.WasAutoDetected)
            {
                // Caminho feliz: uma linha discreta, sem alerta e sem campo.
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        Loc.T("Esta máquina", "This machine", "Esta máquina"),
                        $"{CpuPerformanceIndex.RawProcessorName} · {Loc.T("índice", "index", "índice")} {CpuPerformanceIndex.DevRating} · " +
                        $"{DrawCallBudgetModel.MeasuredCostUs:F3} µs/draw",
                        EditorStyles.miniLabel);

                    if (GUILayout.Button(Loc.T("Recalibrar", "Recalibrate", "Recalibrar"), EditorStyles.miniButton, GUILayout.Width(80)))
                        DrawCallCalibrator.Calibrate();
                }
                return;
            }

            EditorGUILayout.LabelField(Loc.T("Calibração desta máquina", "Calibration of this machine", "Calibración de esta máquina"),
                                       EditorStyles.boldLabel);
            string cpuNome = CpuPerformanceIndex.RawProcessorName;
            NoticeBox.Draw(Loc.T(
                $"Não reconheci esta CPU ({cpuNome}). Informe o índice single-thread dela para o plugin " +
                "conseguir projetar a medição no hardware alvo.",
                $"I did not recognize this CPU ({cpuNome}). Enter its single-thread index so the plugin " +
                "can project the measurement onto the target hardware.",
                $"No reconocí esta CPU ({cpuNome}). Indica su índice single-thread para que el plugin " +
                "pueda proyectar la medición al hardware objetivo."),
                NoticeLevel.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                int typed = EditorGUILayout.IntField(Loc.T("Índice PassMark", "PassMark index", "Índice PassMark"), CpuPerformanceIndex.ManualRating);
                if (typed != CpuPerformanceIndex.ManualRating)
                    CpuPerformanceIndex.ManualRating = typed < 0 ? 0 : typed;

                // MOTIVO: abrir a página é um link comum. NÃO raspamos o site: a PassMark
                // não tem API pública e trata os dados como uso pessoal — um plugin
                // publicado sob MIT que raspasse o site deles seria passivo jurídico.
                if (GUILayout.Button(Loc.T("Abrir PassMark", "Open PassMark", "Abrir PassMark"), GUILayout.Width(110)))
                    Application.OpenURL(CpuPerformanceIndex.LookupUrl);
            }
        }

        // ── PC: Processador / Memória / Placa de vídeo ───────────────────
        private bool DrawPcPanels(HardwareBuild b)
        {
            bool changed = false;

            // ── Processador ──────────────────────────────────────────────
            EditorGUILayout.LabelField(Loc.T("Processador", "Processor", "Procesador"), EditorStyles.boldLabel);
            _cpuVendor = GUILayout.Toolbar(Mathf.Clamp(_cpuVendor, 0, CPU_VENDOR_COUNT - 1), CpuVendorNames());

            var cpuIds = new List<string>();
            var cpuNames = new List<string>();
            int cpuIdx = 0;
            foreach (var c in HardwareCatalog.CpusAscending)
            {
                if (!MatchesCpuVendor(c.Class, _cpuVendor)) continue;
                if (c.Id == b.CpuId) cpuIdx = cpuIds.Count;
                cpuIds.Add(c.Id);
                // MOTIVO: o índice aparece no rótulo porque é ele que produz o veredito.
                // Um número que decide algo não pode ficar escondido atrás da escolha.
                cpuNames.Add($"{c.DisplayName}  —  {c.SingleThreadRating} pts");
            }

            if (cpuIds.Count == 0)
            {
                EditorGUILayout.HelpBox(Loc.T("Nenhuma CPU deste fabricante no catálogo.", "No CPU from this vendor in the catalog.",
                                              "Ninguna CPU de este fabricante en el catálogo."), MessageType.Info);
            }
            else
            {
                // MOTIVO: se o filtro escondeu a CPU atual, ela continua ativa — o
                // popup mostra só a lista filtrada e selecionar um item troca de fato.
                int newCpu = EditorGUILayout.Popup(Loc.T("Modelo", "Model", "Modelo"), cpuIdx, cpuNames.ToArray());
                if (cpuIds[newCpu] != b.CpuId) { b.CpuId = cpuIds[newCpu]; changed = true; }
            }
            EditorGUILayout.LabelField(
                Loc.T("pts = PassMark single thread. É o índice que projeta o tempo medido aqui para o alvo.",
                      "pts = PassMark single thread. It is the index that projects the time measured here onto the target.",
                      "pts = PassMark single thread. Es el índice que proyecta el tiempo medido aquí al objetivo."),
                EditorStyles.miniLabel);

            // ── Memória ──────────────────────────────────────────────────
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(Loc.T("Memória", "Memory", "Memoria"), EditorStyles.boldLabel);
            // MOTIVO: compatibilidade real CPU×RAM — só mostra as gerações que a
            // CPU selecionada suporta (AM5 = só DDR5; AM4 = só DDR4; LGA1700 = as duas).
            HardwareCatalog.TryGetCpu(b.CpuId, out var cpuForRam);
            var gens = new List<RamGenSpec>();
            foreach (var g in HardwareCatalog.RamGens)
                if (cpuForRam.SupportedRam != null && Array.IndexOf(cpuForRam.SupportedRam, g.Type) >= 0)
                    gens.Add(g);
            if (gens.Count == 0) gens.AddRange(HardwareCatalog.RamGens); // defensivo

            var ramNames = new string[gens.Count];
            int ramIdx = 0;
            for (int i = 0; i < gens.Count; i++)
            {
                ramNames[i] = gens[i].Type.ToString();
                if (gens[i].Type == b.RamType) ramIdx = i;
            }
            int newRam = EditorGUILayout.Popup(Loc.T("Tipo", "Type", "Tipo"), ramIdx, ramNames);
            if (gens[newRam].Type != b.RamType)
            {
                b.RamType = gens[newRam].Type;
                b.RamMhz  = gens[newRam].BaseMhz;
                changed = true;
            }
            var gen = gens[newRam];
            int mhzIdx = Mathf.Max(0, Array.IndexOf(gen.MhzOptions, b.RamMhz));
            var mhzNames = new string[gen.MhzOptions.Length];
            for (int i = 0; i < gen.MhzOptions.Length; i++) mhzNames[i] = gen.MhzOptions[i] + " MHz";
            int newMhz = EditorGUILayout.Popup(Loc.T("Frequência", "Frequency", "Frecuencia"), mhzIdx, mhzNames);
            if (gen.MhzOptions[newMhz] != b.RamMhz) { b.RamMhz = gen.MhzOptions[newMhz]; changed = true; }

            // MOTIVO: ordem crescente, como o resto das listas — o dev escolhe o piso que
            // pretende suportar. A fatia de cada capacidade na Steam continua no rótulo
            // abaixo, porque é o que justifica quais capacidades estão na lista.
            int gbIdx = Mathf.Max(0, Array.IndexOf(HardwareCatalog.RamGbAscending, b.RamGb));
            var gbNames = new string[HardwareCatalog.RamGbAscending.Length];
            for (int i = 0; i < HardwareCatalog.RamGbAscending.Length; i++)
                gbNames[i] = HardwareCatalog.RamGbAscending[i] + " GB";
            int newGb = EditorGUILayout.Popup(Loc.T("Quantidade", "Amount", "Cantidad"), gbIdx, gbNames);
            if (HardwareCatalog.RamGbAscending[newGb] != b.RamGb) { b.RamGb = HardwareCatalog.RamGbAscending[newGb]; changed = true; }
            EditorGUILayout.LabelField(
                Loc.T("Fatia na Steam: 16 GB 41,2% · 32 GB 37,5% · 8 GB 7,5% · 64 GB 4,0% · 12 GB 2,4%",
                      "Steam share: 16 GB 41.2% · 32 GB 37.5% · 8 GB 7.5% · 64 GB 4.0% · 12 GB 2.4%",
                      "Cuota en Steam: 16 GB 41,2% · 32 GB 37,5% · 8 GB 7,5% · 64 GB 4,0% · 12 GB 2,4%"),
                EditorStyles.miniLabel);

            changed |= DrawMemoryCeiling(b);

            // ── Placa de vídeo ───────────────────────────────────────────
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(Loc.T("Placa de vídeo", "Graphics card", "Tarjeta gráfica"), EditorStyles.boldLabel);
            HardwareCatalog.TryGetCpu(b.CpuId, out var cpu);
            bool hasIgpu = !string.IsNullOrEmpty(cpu.IntegratedGpuId);
            using (new EditorGUI.DisabledScope(!hasIgpu))
            {
                bool newIntegrated = EditorGUILayout.Toggle(
                    Loc.T("Usar a integrada desta CPU", "Use this CPU's integrated GPU", "Usar la integrada de esta CPU"),
                    b.UseIntegratedGpu && hasIgpu);
                if (newIntegrated != b.UseIntegratedGpu) { b.UseIntegratedGpu = newIntegrated; changed = true; }
            }
            // MOTIVO: o atalho só funciona para as CPUs cuja integrada tem índice próprio
            // publicado. Para as demais o caminho é escolher a integrada na lista abaixo,
            // pelo filtro "Integrada" — a Steam lista Iris Xe (16ª) e UHD Graphics (26ª)
            // como placas por direito, então elas estão no catálogo como qualquer outra.
            if (!hasIgpu)
                EditorGUILayout.LabelField(
                    Loc.T($"{cpu.DisplayName}: sem integrada mapeada. Escolha pelo filtro \"Integrada\" abaixo.",
                          $"{cpu.DisplayName}: no integrated GPU mapped. Pick it with the \"Integrated\" filter below.",
                          $"{cpu.DisplayName}: sin integrada mapeada. Elígela con el filtro \"Integrada\" de abajo."),
                    EditorStyles.miniLabel);

            if (b.UseIntegratedGpu && hasIgpu)
            {
                HardwareCatalog.TryGetGpu(cpu.IntegratedGpuId, out var igpu);
                EditorGUILayout.LabelField(Loc.T("Modelo", "Model", "Modelo"), igpu.DisplayName, EditorStyles.miniLabel);
            }
            else
            {
                _gpuVendor = GUILayout.Toolbar(Mathf.Clamp(_gpuVendor, 0, GPU_VENDOR_COUNT - 1), GpuVendorNames());

                var gpuIds = new List<string>();
                var gpuNames = new List<string>();
                int gpuIdx = 0;
                foreach (var g in HardwareCatalog.GpusAscending)
                {
                    if (!MatchesGpuVendor(g, _gpuVendor)) continue;
                    if (g.Id == b.GpuId) gpuIdx = gpuIds.Count;
                    gpuIds.Add(g.Id);
                    // MOTIVO: o rótulo carrega as DUAS procedências lado a lado — a
                    // posição na Steam (por que esta placa está na lista) e o G3D (quanto
                    // ela entrega). Separar as duas na tela é o que impede repetir o erro
                    // apontado na banca, de citar uma fonte para justificar a outra.
                    gpuNames.Add($"#{g.SteamRank}  {g.DisplayName}  —  G3D {g.G3dRating}");
                }

                if (gpuIds.Count == 0)
                {
                    EditorGUILayout.HelpBox(Loc.T("Nenhuma placa nesta categoria.", "No card in this category.",
                                                  "Ninguna tarjeta en esta categoría."), MessageType.Info);
                }
                else
                {
                    int newGpu = EditorGUILayout.Popup(Loc.T("Modelo", "Model", "Modelo"), gpuIdx, gpuNames.ToArray());
                    if (gpuIds[newGpu] != b.GpuId) { b.GpuId = gpuIds[newGpu]; changed = true; }
                }
                EditorGUILayout.LabelField(
                    Loc.T("# = posição na Steam Hardware Survey (quem tem). G3D = PassMark (quanto entrega).",
                          "# = rank in the Steam Hardware Survey (who owns it). G3D = PassMark (how much it delivers).",
                          "# = posición en la Steam Hardware Survey (quién la tiene). G3D = PassMark (cuánto rinde)."),
                    EditorStyles.miniLabel);
            }

            return changed;
        }

        /// <summary>Maps a CPU class onto the vendor filter buttons.</summary>
        private static bool MatchesCpuVendor(CpuClass cls, int filter)
        {
            if (filter < 0 || filter >= CPU_VENDOR_COUNT - 1) return true; // "Todas"
            bool intel = cls == CpuClass.I5 || cls == CpuClass.I7
                      || cls == CpuClass.I9 || cls == CpuClass.CoreUltra;
            return filter == 0 ? intel : !intel;
        }

        /// <summary>Maps a GPU onto the vendor filter buttons.</summary>
        private static bool MatchesGpuVendor(GpuSpec g, int filter)
        {
            switch (filter)
            {
                case 0:  return g.Vendor == GpuVendor.Nvidia;
                case 1:  return g.Vendor == GpuVendor.Amd;
                case 2:  return g.IsIntegrated;
                default: return true;
            }
        }

        /// <summary>Memory ceiling selector, shared by the PC and Android panels.</summary>
        /// <remarks>
        /// MOTIVO: nasceu dentro do painel de PC, e ficou preso lá. Em Android a memória
        /// ficava sem teto MESMO medindo no aparelho — sem veredito no caso que a etapa 2
        /// existe para atender. Nenhum fabricante de celular publica limite por app, então
        /// a regra é a mesma do PC: quem declara é o dev, e a medição na build pode propor
        /// o valor a partir do pico real.
        ///
        /// O VR não usa este bloco: ali a Meta PUBLICA o limite por app, e número
        /// publicado ganha de número declarado.
        ///
        /// MOTIVO da reescrita: na versão anterior as orientações ("rode uma sessão na
        /// build", "este pico é do Editor") ficavam DENTRO do bloco que só aparece com teto
        /// definido — e clicar em "Definir…" sem medição deixava o teto em 0, que é "sem
        /// teto". Resultado: sem medição não havia como definir teto, e a orientação para
        /// medir nunca aparecia. O autor perguntou por que memória ficou sem laudo, e a
        /// tela não tinha como responder. Agora a sugestão aparece no estado "sem teto",
        /// que é onde o dev está quando precisa dela.
        /// </remarks>
        private bool DrawMemoryCeiling(HardwareBuild b)
        {
            bool changed = false;

            EditorGUILayout.LabelField(Loc.T("Teto de memória do jogo", "Game memory ceiling", "Techo de memoria del juego"),
                                       EditorStyles.boldLabel);

            long medido = HardwareBuildSettings.LastMeasuredPeakBytes;
            string origemPico = HardwareBuildSettings.LastMeasuredPeakSource;
            int medidoMb = medido > 0 ? Mathf.CeilToInt(medido / (1024f * 1024f)) : 0;
            int sugeridoMb = Mathf.CeilToInt(medidoMb * 1.2f);

            // MOTIVO: só um pico medido NA BUILD serve de base para o teto do jogo. Um pico
            // de Play Mode é a memória da Unity inteira (3 a 4 GB), e "pico + 20%" viraria
            // um teto que nunca estoura — um semáforo que aprova tudo.
            bool picoDoEditor = medido > 0 && string.IsNullOrEmpty(origemPico);
            bool temMedicao   = medido > 0 && !picoDoEditor;

            // MOTIVO: o teto é por plataforma — um teto do tablet não vale no PC. Lido e
            // gravado só por CeilingFor/SetCeiling, que fazem essa checagem.
            int teto = HardwareBuildSettings.CeilingFor(b);

            int origemAtual = teto > 0 ? 1 : 0;
            int novaOrigem = GUILayout.Toolbar(origemAtual, new[] { Loc.T("Sem teto", "No ceiling", "Sin techo"),
                                                                    Loc.T("Definir…", "Set…", "Definir…") });
            if (novaOrigem != origemAtual)
            {
                // MOTIVO: sem medição válida, "Definir…" começa no mínimo do controle. É o
                // ponto de partida de uma DECLARAÇÃO manual, não uma sugestão — por isso é
                // o mínimo, que obriga o dev a arrastar até o valor que quer, em vez de um
                // número "razoável" que pareceria recomendação sem ter fonte.
                HardwareBuildSettings.SetCeiling(b,
                    novaOrigem == 1 ? (temMedicao ? sugeridoMb : CEILING_MIN_MB) : 0,
                    isAuto: false, source: "");
                teto = HardwareBuildSettings.CeilingFor(b);
                changed = true;
            }

            // MOTIVO: um teto que o plugin definiu sozinho precisa dizer que foi ele — senão
            // o dev encontra um número que não lembra de ter posto e não sabe de onde veio.
            if (teto > 0 && b.MemoryCeilingIsAuto)
            {
                int secs = b.MemoryCeilingSessionSeconds;
                string src = b.MemoryCeilingSource;
                EditorGUILayout.LabelField(Loc.T(
                    $"Definido automaticamente a partir da primeira sessão medida em {src}" +
                    (secs > 0 ? $", que durou {secs} s" : "") +
                    " (pico + 20%). Não sobe sozinho — é a referência contra a qual as próximas " +
                    "sessões são comparadas. Sessão curta pode não chegar ao consumo real do jogo: " +
                    "nesse caso, recalcule depois de uma sessão longa.",
                    $"Set automatically from the first session measured on {src}" +
                    (secs > 0 ? $", which lasted {secs} s" : "") +
                    " (peak + 20%). It does not rise on its own — it is the reference the next " +
                    "sessions are compared against. A short session may not reach the game's real usage: " +
                    "in that case, recalculate after a long session.",
                    $"Definido automáticamente a partir de la primera sesión medida en {src}" +
                    (secs > 0 ? $", que duró {secs} s" : "") +
                    " (pico + 20%). No sube solo: es la referencia con la que se comparan las próximas " +
                    "sesiones. Una sesión corta puede no llegar al consumo real del juego: " +
                    "en ese caso, recalcula después de una sesión larga."),
                    EditorStyles.wordWrappedMiniLabel);
            }

            // ── Pico medido: o que ajuda a decidir, nos dois estados ─────
            if (temMedicao)
            {
                string cabe = teto >= medidoMb
                    ? Loc.T("cabe no teto atual", "fits the current ceiling", "cabe en el techo actual")
                    : Loc.T("ESTOURA o teto atual", "EXCEEDS the current ceiling", "SUPERA el techo actual");
                EditorGUILayout.LabelField(
                    Loc.T($"Pico medido em {origemPico}: {medidoMb} MB",
                          $"Peak measured on {origemPico}: {medidoMb} MB",
                          $"Pico medido en {origemPico}: {medidoMb} MB") +
                    (teto > 0 ? $" ({cabe})." : "."),
                    EditorStyles.miniLabel);

                if (teto != sugeridoMb
                    && GUILayout.Button(teto > 0
                        ? Loc.T($"Recalcular a partir da última sessão ({sugeridoMb} MB)",
                                $"Recalculate from the last session ({sugeridoMb} MB)",
                                $"Recalcular a partir de la última sesión ({sugeridoMb} MB)")
                        : Loc.T($"Usar o pico medido + 20% ({sugeridoMb} MB)",
                                $"Use the measured peak + 20% ({sugeridoMb} MB)",
                                $"Usar el pico medido + 20% ({sugeridoMb} MB)")))
                {
                    // MOTIVO da folga de 20%: um teto colado no pico acusaria qualquer
                    // variação normal entre execuções. Recalcular é ação EXPLÍCITA: se o
                    // plugin subisse o teto sozinho a cada sessão, apagaria o sinal de
                    // regressão que ele existe para dar.
                    HardwareBuildSettings.SetCeiling(b, sugeridoMb, isAuto: false, source: "");
                    teto = sugeridoMb;
                    changed = true;
                }
            }
            else if (picoDoEditor)
            {
                EditorGUILayout.HelpBox(Loc.T(
                    $"O último pico ({medidoMb} MB) foi medido no Editor — é a memória da Unity " +
                    "inteira, não do jogo, e não serve como teto. Rode uma sessão \"Na build\" " +
                    "e o teto se define sozinho ao final dela.",
                    $"The last peak ({medidoMb} MB) was measured in the Editor — it is the memory of the whole " +
                    "of Unity, not the game, and cannot serve as a ceiling. Run an \"On the build\" session " +
                    "and the ceiling sets itself at the end of it.",
                    $"El último pico ({medidoMb} MB) se midió en el Editor: es la memoria de toda Unity, " +
                    "no del juego, y no sirve como techo. Ejecuta una sesión \"En la build\" " +
                    "y el techo se definirá solo al final."),
                    MessageType.Warning);
            }
            else if (teto <= 0)
            {
                Notice(
                    Loc.T("Rode uma sessão \"Na build\": ao final dela o teto se define sozinho.",
                          "Run an \"On the build\" session: at the end of it the ceiling sets itself.",
                          "Ejecuta una sesión \"En la build\": al final, el techo se definirá solo."),
                    MessageType.Info,
                    Loc.T("De onde sai o teto automático", "Where the automatic ceiling comes from",
                          "De dónde sale el techo automático"),
                    Loc.T(
                    "Ao terminar a primeira sessão medida no aparelho (ou na build standalone), o " +
                    "plugin grava como teto o maior consumo de memória que o jogo atingiu, mais " +
                    "20% de folga. As sessões SEGUINTES passam a ser comparadas a ele.\n\n" +
                    "Por que não na mesma sessão: um teto tirado do pico da própria sessão nunca " +
                    "é ultrapassado por ela. O semáforo ficaria sempre verde e não mediria nada.\n\n" +
                    "O que ele detecta: o jogo passar a ocupar MAIS memória do que ocupava — " +
                    "regressão e vazamento entre uma build e a próxima. Isso é medição do seu " +
                    "projeto, não um número genérico de tabela.",
                    "When the first session measured on the device (or on the standalone build) ends, the " +
                    "plugin records as the ceiling the highest memory usage the game reached, plus " +
                    "20% headroom. The FOLLOWING sessions are compared against it.\n\n" +
                    "Why not in the same session: a ceiling taken from a session's own peak is never " +
                    "exceeded by that session. The indicator would always be green and would measure nothing.\n\n" +
                    "What it detects: the game starting to use MORE memory than it used to — " +
                    "regressions and leaks between one build and the next. This is a measurement of your " +
                    "project, not a generic number from a table.",
                    "Al terminar la primera sesión medida en el dispositivo (o en la build standalone), el " +
                    "plugin guarda como techo el mayor consumo de memoria que alcanzó el juego, más " +
                    "un 20% de margen. Las sesiones SIGUIENTES se comparan con él.\n\n" +
                    "Por qué no en la misma sesión: un techo sacado del pico de la propia sesión nunca " +
                    "es superado por ella. El semáforo estaría siempre en verde y no mediría nada.\n\n" +
                    "Lo que detecta: que el juego pase a ocupar MÁS memoria de la que ocupaba: " +
                    "regresiones y fugas entre una build y la siguiente. Es una medición de tu " +
                    "proyecto, no un número genérico de una tabla."));
            }

            // ── Teto definido ────────────────────────────────────────────
            if (teto > 0)
            {
                int novo = EditorGUILayout.IntSlider("MB", teto, CEILING_MIN_MB, CEILING_MAX_MB);
                if (novo != teto)
                {
                    // Arrastar é declaração manual: deixa de ser o teto automático.
                    HardwareBuildSettings.SetCeiling(b, novo, isAuto: false, source: "");
                    changed = true;
                }

                if (b.Test != TestMode.NaBuild)
                    Notice(
                        Loc.T("Este teto só funciona no modo \"Na build\".",
                              "This ceiling only works in \"On the build\" mode.",
                              "Este techo solo funciona en el modo \"En la build\"."),
                        MessageType.Warning,
                        Loc.T("Por que o teto não vale no Editor", "Why the ceiling does not apply in the Editor",
                              "Por qué el techo no vale en el Editor"),
                        Loc.T(
                        "No Play Mode, o jogo roda dentro do processo do Editor, e os contadores de " +
                        "memória medem o processo inteiro — ou seja, medem a Unity junto.\n\n" +
                        "Um teto pensado para o jogo comparado com a memória da Unity inteira dá " +
                        "vermelho o tempo todo, sem nunca dizer nada sobre o jogo. Por isso ele fica " +
                        "desligado aqui, em vez de mostrar um alarme que não significa nada.",
                        "In Play Mode the game runs inside the Editor process, and the memory counters " +
                        "measure the whole process — that is, they measure Unity too.\n\n" +
                        "A ceiling meant for the game compared with the memory of the whole of Unity is " +
                        "red all the time, without ever saying anything about the game. That is why it is " +
                        "switched off here, instead of showing an alarm that means nothing.",
                        "En Play Mode el juego se ejecuta dentro del proceso del Editor, y los contadores de " +
                        "memoria miden el proceso entero, es decir, también miden Unity.\n\n" +
                        "Un techo pensado para el juego comparado con la memoria de toda Unity da " +
                        "rojo todo el tiempo, sin decir nunca nada sobre el juego. Por eso queda " +
                        "desactivado aquí, en lugar de mostrar una alarma que no significa nada."));
            }
            else
            {
                EditorGUILayout.LabelField(Loc.T(
                    "Sem teto: o Monitor mostra a memória e não julga. Mesma regra do teto de draw calls.",
                    "No ceiling: the Monitor shows memory and does not judge it. Same rule as the draw call ceiling.",
                    "Sin techo: el Monitor muestra la memoria y no la juzga. Misma regla que el techo de draw calls."),
                    EditorStyles.miniLabel);
            }

            return changed;
        }

        // Limites do controle deslizante — faixa de interface, não de hardware. Não são
        // usados para julgar nada: o teto é o valor que o dev escolher dentro dela.
        private const int CEILING_MIN_MB = 64;
        private const int CEILING_MAX_MB = 16384;

        /// <summary>Android needs no hardware panel: the connected device answers for itself.</summary>
        /// <remarks>
        /// MOTIVO: aqui havia um seletor de modelo (Galaxy A14, Redmi Note 12…). Ele não
        /// decidia nada — nenhum limiar saía dele, porque nenhum fabricante de celular
        /// publica orçamento por modelo. Era decoração com aparência de configuração, e o
        /// dev razoavelmente esperava que escolher outro aparelho mudasse algum número.
        ///
        /// Com a medição no aparelho, quem responde é o que estiver plugado. Escolher um
        /// modelo numa lista não muda o que está na ponta do cabo.
        /// </remarks>
        private bool DrawMobilePanel(HardwareBuild b)
        {
            // MOTIVO: Android não tem painel de hardware — quem responde é o aparelho na
            // ponta do cabo. O que sobra de configurável é o teto de memória, que ninguém
            // publica para celular e por isso é declarado pelo dev, igual ao PC.
            return DrawMemoryCeiling(b);
        }

        private bool DrawVrPanel(HardwareBuild b)
        {
            bool changed = false;

            EditorGUILayout.LabelField(Loc.T("Headset standalone", "Standalone headset", "Visor standalone"), EditorStyles.boldLabel);
            var hs = HardwareCatalog.VrHeadsetsAscending;
            var names = new string[hs.Length];
            int idx = 0;
            for (int i = 0; i < hs.Length; i++)
            {
                names[i] = $"{hs[i].DisplayName} — {hs[i].RefreshHz}Hz";
                if (hs[i].Id == b.VrId) idx = i;
            }
            int sel = EditorGUILayout.Popup(Loc.T("Modelo", "Model", "Modelo"), idx, names);
            if (hs[sel].Id != b.VrId) { b.VrId = hs[sel].Id; changed = true; }

            EditorGUILayout.HelpBox(Loc.T(
                "Headsets PCVR (Valve Index, Vive ligado no PC) usam a build PC — " +
                "selecione a plataforma PC com o hardware do computador que roda o jogo.",
                "PCVR headsets (Valve Index, Vive tethered to a PC) use the PC build — " +
                "select the PC platform with the hardware of the computer that runs the game.",
                "Los visores PCVR (Valve Index, Vive conectado al PC) usan la build de PC: " +
                "selecciona la plataforma PC con el hardware del ordenador que ejecuta el juego."),
                MessageType.None);

            return changed;
        }


        // ─── Configurações do projeto (etapa 3) ───────────────────────────
        // MOTIVO do cache: a análise lê o QualitySettings, o asset da URP e os ARQUIVOS das
        // cenas da build como texto. Refazer isso a cada repintura do IMGUI (várias por
        // segundo) seria desperdício; o resultado só muda quando o dev troca de plataforma
        // ou mexe nas configurações — daí o botão de reanalisar.
        private List<ProjectSettingsAnalyzer.Finding> _settingsFindings;
        private ProjectSettingsAnalyzer.TargetAsset _settingsTarget;
        private TargetPlatform _settingsPlatform = (TargetPlatform)(-1);
        private UiLanguage _settingsLang;

        private void DrawProjectSettingsSection(HardwareBuild b)
        {
            // MOTIVO: os textos dos achados nascem na análise; trocar o idioma refaz a análise.
            if (_settingsFindings == null || _settingsPlatform != b.Platform || _settingsLang != Loc.Current)
                RunSettingsAnalysis(b.Platform);

            EditorGUILayout.Space(12);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(Loc.T("Configurações de render do projeto", "Project render settings",
                                                 "Configuración de render del proyecto"), EditorStyles.boldLabel);
                if (GUILayout.Button(Loc.T("Reanalisar", "Re-analyze", "Reanalizar"), EditorStyles.miniButton, GUILayout.Width(80)))
                    RunSettingsAnalysis(b.Platform);
            }

            if (_settingsTarget.Problem != null)
            {
                EditorGUILayout.HelpBox(_settingsTarget.Problem, MessageType.Info);
                return;
            }

            // MOTIVO: dizer QUAL asset foi lido. O projeto pode ter um por nível de
            // qualidade, e sem esta linha o dev não sabe se a análise olhou o asset que vai
            // de fato para a build do alvo.
            using (new EditorGUILayout.HorizontalScope())
            {
                string assetName = _settingsTarget.Asset.name;
                string nivel = _settingsTarget.QualityLevelName;
                EditorGUILayout.LabelField(Loc.T(
                    $"Asset do alvo: {assetName} (nível de qualidade \"{nivel}\")",
                    $"Target asset: {assetName} (quality level \"{nivel}\")",
                    $"Asset del objetivo: {assetName} (nivel de calidad \"{nivel}\")"),
                    EditorStyles.miniLabel);
                if (GUILayout.Button(Loc.T("Selecionar", "Select", "Seleccionar"), EditorStyles.miniButton, GUILayout.Width(80)))
                {
                    Selection.activeObject = _settingsTarget.Asset;
                    EditorGUIUtility.PingObject(_settingsTarget.Asset);
                }
            }

            int pendentes = 0;
            foreach (var f in _settingsFindings)
                if (f.Status == ProjectSettingsAnalyzer.Status.Recomendacao) pendentes++;

            EditorGUILayout.LabelField(
                pendentes == 0
                    ? Loc.T("Tudo como recomendado pelas fontes publicadas para este alvo.",
                            "Everything as recommended by the published sources for this target.",
                            "Todo como recomiendan las fuentes publicadas para este objetivo.")
                    : Loc.T($"{pendentes} configuração(ões) diferente(s) do que a Unity/Google recomendam para este alvo.",
                            $"{pendentes} setting(s) differ from what Unity/Google recommend for this target.",
                            $"{pendentes} ajuste(s) distinto(s) de lo que Unity/Google recomiendan para este objetivo."),
                EditorStyles.miniLabel);

            foreach (var f in _settingsFindings)
                DrawSettingsFinding(f);

            // MOTIVO: o plugin não corrige sozinho. Depth e Opaque Texture, por exemplo, são
            // necessários quando algum shader amostra a cena — desligar às cegas quebraria o
            // visual do jogo, e só o dev sabe se é o caso.
            EditorGUILayout.LabelField(
                Loc.T("O plugin só lê — mudar é decisão sua: algumas opções são necessárias para certos shaders.",
                      "The plugin only reads — changing is your call: some options are required by certain shaders.",
                      "El plugin solo lee: cambiar es decisión tuya, algunas opciones son necesarias para ciertos shaders."),
                EditorStyles.miniLabel);
        }

        private void RunSettingsAnalysis(TargetPlatform platform)
        {
            _settingsPlatform = platform;
            _settingsLang     = Loc.Current;
            _settingsTarget   = ProjectSettingsAnalyzer.ResolveAsset(platform);
            _settingsFindings = ProjectSettingsAnalyzer.Analyze(platform, _settingsTarget);
        }

        private static void DrawSettingsFinding(ProjectSettingsAnalyzer.Finding f)
        {
            string icon;
            switch (f.Status)
            {
                case ProjectSettingsAnalyzer.Status.Ok:           icon = "✓"; break;
                case ProjectSettingsAnalyzer.Status.Recomendacao: icon = "⚠"; break;
                case ProjectSettingsAnalyzer.Status.Ilegivel:     icon = "?"; break;
                default:                                          icon = "–"; break;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUIStyle style = f.Status == ProjectSettingsAnalyzer.Status.Recomendacao
                    ? EditorStyles.boldLabel
                    : EditorStyles.label;
                EditorGUILayout.LabelField($"{icon}  {f.Title}", f.Current, style);

                if (GUILayout.Button(WhyLabel(), EditorStyles.miniButton, GUILayout.Width(70)))
                {
                    ExplainerPopup.Show(f.Title,
                        $"{Loc.T("Recomendado", "Recommended", "Recomendado")}: {f.Recommendation}\n\n" +
                        $"{Loc.T("Motivo", "Reason", "Motivo")}: {f.Why}\n\n" +
                        $"{Loc.T("Fonte", "Source", "Fuente")}: {f.SourceName}\n{f.SourceUrl}\n\n" +
                        Loc.T("Esta regra só é aplicada às plataformas que a fonte cobre.",
                              "This rule is applied only to the platforms the source covers.",
                              "Esta regla solo se aplica a las plataformas que cubre la fuente."));
                }
                if (GUILayout.Button(Loc.T("Fonte", "Source", "Fuente"), EditorStyles.miniButton, GUILayout.Width(50)))
                    Application.OpenURL(f.SourceUrl);
            }

            if (f.Status == ProjectSettingsAnalyzer.Status.Recomendacao)
                EditorGUILayout.LabelField(" ", Loc.T("recomendado: ", "recommended: ", "recomendado: ") + f.Recommendation,
                                           EditorStyles.miniLabel);
        }

        /// <summary>Short notice on screen, full justification one click away.</summary>
        /// <remarks>
        /// MOTIVO: os avisos tinham cinco linhas e termos que só significam algo pra quem
        /// já conhece o problema. Aviso que o leitor não entende não avisa — vira ruído
        /// que ele aprende a pular, e aí o alerta que importa passa batido junto.
        ///
        /// O texto longo não sumiu: ele é o que sustenta o número na banca. Só deixou de
        /// disputar espaço com a informação que o dev precisa pra agir agora.
        /// </remarks>
        private static void Notice(string curto, MessageType tipo, string titulo, string detalhe)
        {
            EditorGUILayout.HelpBox(curto, tipo);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(WhyLabel(), EditorStyles.miniButton, GUILayout.Width(70)))
                    ExplainerPopup.Show(titulo, detalhe);
            }
        }

        private static string WhyLabel() => Loc.T("Por quê?", "Why?", "¿Por qué?");
        /// <summary>Draws the Editor-vs-build selector and, in build mode, the launcher.</summary>
        /// <remarks>
        /// MOTIVO: em Mobile não há seletor — o modo é fixo em build. A versão anterior
        /// mostrava as duas opções e usava o modo Editor pra explicar que ali não sai
        /// veredito nenhum. Só que oferecer uma opção que não produz resultado é pior do
        /// que não oferecer: o dev escolhe, roda, não recebe nada e não sabe se errou.
        /// Em Android a única medição que responde alguma coisa é no aparelho.
        /// </remarks>
        private bool DrawTestModePanel(HardwareBuild b)
        {
            bool changed = false;

            if (b.Platform == TargetPlatform.Mobile)
            {
                // Garante o modo mesmo se a build salva veio de antes desta regra.
                if (b.Test != TestMode.NaBuild) { b.Test = TestMode.NaBuild; changed = true; }

                EditorGUILayout.LabelField(Loc.T("Como testar", "How to test", "Cómo probar"), EditorStyles.boldLabel);
                DrawBuildModeLauncher(b);
                return changed;
            }

            EditorGUILayout.LabelField(Loc.T("Onde medir", "Where to measure", "Dónde medir"), EditorStyles.boldLabel);

            int modeIdx = b.Test == TestMode.NaBuild ? 1 : 0;
            int newMode = GUILayout.Toolbar(modeIdx, TestModeNames());
            if (newMode != modeIdx)
            {
                b.Test = newMode == 1 ? TestMode.NaBuild : TestMode.NoEditor;
                changed = true;
            }

            if (b.Test == TestMode.NoEditor) DrawEditorModeNotice(b);
            else                             DrawBuildModeLauncher(b);

            return changed;
        }

        private void DrawEditorModeNotice(HardwareBuild b)
        {
            Notice(
                Loc.T("Play normal, sem buildar. A memória aqui não recebe veredito.",
                      "Regular Play, no build. Memory gets no verdict here.",
                      "Play normal, sin compilar. La memoria aquí no recibe veredicto."),
                MessageType.Warning,
                Loc.T("Por que a memória não tem veredito no Editor", "Why memory has no verdict in the Editor",
                      "Por qué la memoria no tiene veredicto en el Editor"),
                Loc.T(
                "Quando você dá Play dentro do Unity, o jogo roda DENTRO do processo do Editor. " +
                "E os contadores de memória medem o processo inteiro — ou seja, medem a Unity: as " +
                "janelas abertas, o inspector, os assets que o Editor mantém carregados, tudo.\n\n" +
                "Na prática isso deu entre 3,2 e 4,2 GB numa cena em que o jogo usava uma fração " +
                "disso. Comparado a um teto de jogo, o semáforo ficava vermelho o tempo todo, sem " +
                "nunca dizer nada sobre o jogo. Foi exatamente o problema que apareceu quando o " +
                "professor testou.\n\n" +
                "Não é defeito do Profiler da Unity: o número está certo sobre o processo. O erro " +
                "era nosso, de ler esse número como se fosse do jogo.\n\n" +
                "O que FUNCIONA neste modo: contagem de draw calls, porque ela depende da cena e " +
                "do pipeline de render, não da máquina. E comparar uma versão da cena com outra, " +
                "já que o excesso do Editor é parecido nas duas.\n\n" +
                "Para medir a memória do jogo de verdade, use 'Na build'. É o que a própria Unity " +
                "recomenda: \"the most accurate profiling results always come from running and " +
                "profiling builds on target devices\".",
                "When you press Play inside Unity, the game runs INSIDE the Editor process. " +
                "And the memory counters measure the whole process — that is, they measure Unity: the " +
                "open windows, the inspector, the assets the Editor keeps loaded, everything.\n\n" +
                "In practice that came to between 3.2 and 4.2 GB in a scene where the game used a fraction " +
                "of that. Compared with a game ceiling, the indicator was red all the time, without " +
                "ever saying anything about the game. That was exactly the problem that showed up when the " +
                "advisor tested it.\n\n" +
                "It is not a flaw in Unity's Profiler: the number is right about the process. The mistake " +
                "was ours, reading that number as if it were the game's.\n\n" +
                "What DOES work in this mode: the draw call count, because it depends on the scene and " +
                "the render pipeline, not on the machine. And comparing one version of the scene with another, " +
                "since the Editor's overhead is similar in both.\n\n" +
                "To measure the game's real memory, use 'On the build'. It is what Unity itself " +
                "recommends: \"the most accurate profiling results always come from running and " +
                "profiling builds on target devices\".",
                "Cuando das Play dentro de Unity, el juego se ejecuta DENTRO del proceso del Editor. " +
                "Y los contadores de memoria miden el proceso entero, es decir, miden Unity: las " +
                "ventanas abiertas, el inspector, los assets que el Editor mantiene cargados, todo.\n\n" +
                "En la práctica eso dio entre 3,2 y 4,2 GB en una escena en la que el juego usaba una fracción " +
                "de eso. Comparado con un techo de juego, el semáforo estaba en rojo todo el tiempo, sin " +
                "decir nunca nada sobre el juego. Fue exactamente el problema que apareció cuando el " +
                "profesor lo probó.\n\n" +
                "No es un defecto del Profiler de Unity: el número es correcto sobre el proceso. El error " +
                "era nuestro, al leer ese número como si fuera del juego.\n\n" +
                "Lo que SÍ funciona en este modo: el conteo de draw calls, porque depende de la escena y " +
                "del pipeline de render, no de la máquina. Y comparar una versión de la escena con otra, " +
                "ya que el exceso del Editor es parecido en ambas.\n\n" +
                "Para medir la memoria real del juego, usa 'En la build'. Es lo que la propia Unity " +
                "recomienda: \"the most accurate profiling results always come from running and " +
                "profiling builds on target devices\"."));
        }

        private void DrawBuildModeLauncher(HardwareBuild b)
        {
            BuildTarget target = ProfiledBuildLauncher.TargetFor(b.Platform);

            string destino;
            switch (b.Platform)
            {
                case TargetPlatform.VR:
                    destino = Loc.T("no headset conectado por USB", "on the headset connected over USB",
                                    "en el visor conectado por USB"); break;
                case TargetPlatform.Mobile:
                    destino = Loc.T("no aparelho conectado por USB", "on the device connected over USB",
                                    "en el dispositivo conectado por USB"); break;
                default:
                    destino = Loc.T("numa janela separada, fora do Editor", "in a separate window, outside the Editor",
                                    "en una ventana aparte, fuera del Editor"); break;
            }

            Notice(
                Loc.T($"O jogo roda {destino}. Os números passam a ser só dele.",
                      $"The game runs {destino}. The numbers are now its own.",
                      $"El juego se ejecuta {destino}. Los números pasan a ser solo suyos."),
                MessageType.Info,
                Loc.T("Como funciona a medição na build", "How measuring on the build works", "Cómo funciona la medición en la build"),
                Loc.T(
                $"O botão abaixo compila o projeto e executa {destino}, já conectado ao Profiler.\n\n" +
                "Ele marca sozinho as duas opções que isso exige — Development Build e Autoconnect " +
                "Profiler. Você não precisa abrir Build Profiles nem lembrar de nada: esquecer " +
                "qualquer uma das duas faz o plugin não receber frame nenhum, e a tela fica igual " +
                "à de um aparelho desconectado.\n\n" +
                (target == BuildTarget.Android
                    ? "Antes de clicar: ligue a depuração USB no aparelho (Opções do desenvolvedor) " +
                      "e confirme que ele aparece ao rodar 'adb devices' no terminal.\n\n" +
                      "Durante a medição: deixe o APK ABERTO, em primeiro plano e com a tela ligada. " +
                      "O Android suspende app em segundo plano, e app suspenso não envia telemetria.\n\n" +
                      "Não use o Unity Remote para medir. Ele parece rodar no celular — a imagem " +
                      "aparece lá e o toque funciona — mas o jogo executa NO PC, e o aparelho é só " +
                      "tela e input. Os números seriam do seu computador.\n\n"
                    : "") +
                "Depois que a build abrir, volte ao Monitor e clique em Monitorar. Os valores que " +
                "chegarem são do jogo rodando de verdade — sem Editor no meio, sem estimativa e " +
                "sem projeção.\n\n" +
                (b.Platform == TargetPlatform.VR
                    ? "No Quest, trave o nível de CPU e GPU antes de medir (os comandos " +
                      "'debug.oculus.cpuLevel' e 'gpuLevel'). O processador do headset baixa o " +
                      "clock sozinho quando esquenta, e sem travar o mesmo teste dá resultado " +
                      "diferente a cada execução — a Meta documenta isso.\n\n"
                    : "") +
                "A build fica salva em Builds/PerformanceAdvisor, fora da pasta Assets.",
                $"The button below builds the project and runs it {destino}, already connected to the Profiler.\n\n" +
                "It ticks the two options this requires by itself — Development Build and Autoconnect " +
                "Profiler. You don't need to open Build Profiles or remember anything: forgetting " +
                "either one makes the plugin receive no frames at all, and the screen looks the same " +
                "as with a disconnected device.\n\n" +
                (target == BuildTarget.Android
                    ? "Before clicking: enable USB debugging on the device (Developer options) " +
                      "and make sure it shows up when you run 'adb devices' in a terminal.\n\n" +
                      "While measuring: keep the APK OPEN, in the foreground and with the screen on. " +
                      "Android suspends background apps, and a suspended app sends no telemetry.\n\n" +
                      "Do not use Unity Remote to measure. It looks like it runs on the phone — the image " +
                      "shows up there and touch works — but the game runs ON THE PC, and the device is only " +
                      "screen and input. The numbers would be your computer's.\n\n"
                    : "") +
                "Once the build opens, go back to the Monitor and click Monitor. The values that " +
                "arrive come from the game actually running — no Editor in between, no estimate and " +
                "no projection.\n\n" +
                (b.Platform == TargetPlatform.VR
                    ? "On Quest, lock the CPU and GPU levels before measuring (the commands " +
                      "'debug.oculus.cpuLevel' and 'gpuLevel'). The headset's processor lowers its " +
                      "clock on its own when it heats up, and without locking the same test gives a " +
                      "different result on every run — Meta documents this.\n\n"
                    : "") +
                "The build is saved in Builds/PerformanceAdvisor, outside the Assets folder.",
                $"El botón de abajo compila el proyecto y lo ejecuta {destino}, ya conectado al Profiler.\n\n" +
                "Marca solo las dos opciones que esto requiere: Development Build y Autoconnect " +
                "Profiler. No necesitas abrir Build Profiles ni recordar nada: olvidar " +
                "cualquiera de las dos hace que el plugin no reciba ningún frame, y la pantalla queda igual " +
                "que con un dispositivo desconectado.\n\n" +
                (target == BuildTarget.Android
                    ? "Antes de hacer clic: activa la depuración USB en el dispositivo (Opciones de desarrollador) " +
                      "y confirma que aparece al ejecutar 'adb devices' en la terminal.\n\n" +
                      "Durante la medición: deja el APK ABIERTO, en primer plano y con la pantalla encendida. " +
                      "Android suspende las apps en segundo plano, y una app suspendida no envía telemetría.\n\n" +
                      "No uses Unity Remote para medir. Parece ejecutarse en el teléfono (la imagen " +
                      "aparece allí y el táctil funciona), pero el juego se ejecuta EN EL PC, y el dispositivo es solo " +
                      "pantalla y entrada. Los números serían de tu ordenador.\n\n"
                    : "") +
                "Cuando la build se abra, vuelve al Monitor y haz clic en Monitorear. Los valores que " +
                "lleguen son del juego ejecutándose de verdad: sin el Editor de por medio, sin estimación y " +
                "sin proyección.\n\n" +
                (b.Platform == TargetPlatform.VR
                    ? "En Quest, fija el nivel de CPU y GPU antes de medir (los comandos " +
                      "'debug.oculus.cpuLevel' y 'gpuLevel'). El procesador del visor baja el " +
                      "reloj solo cuando se calienta, y sin fijarlo la misma prueba da un resultado " +
                      "distinto en cada ejecución; Meta lo documenta.\n\n"
                    : "") +
                "La build se guarda en Builds/PerformanceAdvisor, fuera de la carpeta Assets."));

            // MOTIVO: o bloqueio é detectado ANTES do clique. Módulo faltando ou lista de
            // cenas vazia produzem a mesma tela de "nenhum frame recebido" depois, e aí o
            // dev não tem como saber qual dos dois foi.
            string blocker = ProfiledBuildLauncher.Blocker(target);
            if (blocker != null) EditorGUILayout.HelpBox(blocker, MessageType.Error);

            using (new EditorGUI.DisabledScope(blocker != null))
            {
                if (GUILayout.Button(Loc.T($"Compilar e executar ({target})", $"Build and run ({target})",
                                           $"Compilar y ejecutar ({target})"), GUILayout.Height(28)))
                {
                    ProfiledBuildLauncher.BuildAndRun(target);
                    GUIUtility.ExitGUI();
                }
            }
        }

        /// <summary>Formats a budget, or says it has no ceiling when it is zero.</summary>
        /// <remarks>
        /// MOTIVO: o resumo imprimia o teto cru. Com teto 0 saía "0 draw calls" e
        /// "0,0 GB de RAM", que se lê como um limite apertadíssimo e não como ausência de
        /// limite — o oposto exato do que o número significa. Zero aqui quer dizer "não
        /// há fonte para um teto", e a tela tem que dizer isso com palavras.
        /// </remarks>
        private static string DescribeDrawCalls(long value)
        {
            return value > 0 ? $"{value} draw calls"
                             : Loc.T("sem teto de draw calls", "no draw call ceiling", "sin techo de draw calls");
        }

        private static string DescribeMemory(long bytes)
        {
            if (bytes <= 0) return Loc.T("memória sem teto", "memory without ceiling", "memoria sin techo");
            float gb = bytes / (1024f * 1024f * 1024f);
            return Loc.T($"{gb:F1} GB de RAM", $"{gb:F1} GB of RAM", $"{gb:F1} GB de RAM");
        }
    }
}
