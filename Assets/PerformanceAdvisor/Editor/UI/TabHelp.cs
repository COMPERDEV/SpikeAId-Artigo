namespace PerformanceAdvisor
{
    /// <summary>
    /// Text behind the ⓘ button of each dashboard tab: what the screen is for and what
    /// each element on it does.
    /// </summary>
    /// <remarks>
    /// MOTIVO: o orientador pediu um botão de ajuda em cada tela. Os avisos curtos e os
    /// botões "Por quê?" explicam uma decisão de cada vez; faltava o mapa da tela inteira
    /// — o que cada bloco mostra e em que ordem usar. O índice segue a ordem do enum de
    /// abas da janela (Monitor, Build, Snapshots, Configuração, Laudo, Chat).
    /// </remarks>
    public static class TabHelp
    {
        /// <summary>Opens the help popup for the tab at this index.</summary>
        public static void Show(int tab)
        {
            ExplainerPopup.Show(Title(tab), Body(tab));
        }

        private static string Title(int tab)
        {
            switch (tab)
            {
                case 0:  return Loc.T("Aba Monitor", "Monitor tab", "Pestaña Monitor");
                case 1:  return Loc.T("Aba Build", "Build tab", "Pestaña Build");
                case 2:  return Loc.T("Aba Snapshots", "Snapshots tab", "Pestaña Snapshots");
                case 3:  return Loc.T("Aba Configuração", "Settings tab", "Pestaña Configuración");
                case 4:  return Loc.T("Aba Laudo", "Report tab", "Pestaña Informe");
                default: return Loc.T("Aba Chat", "Chat tab", "Pestaña Chat");
            }
        }

        private static string Body(int tab)
        {
            switch (tab)
            {
                case 0: return Loc.T(
                    "Mostra, quadro a quadro, quanto o jogo gasta enquanto roda e compara com o orçamento do hardware alvo.\n\n" +
                    "• Build alvo / Alterar → — o hardware contra o qual o jogo é julgado; \"Alterar\" leva à aba Build.\n" +
                    "• ▶ Monitorar / ⏸ Parar monitoramento — inicia e encerra a coleta. No Editor, o botão libera com o Play Mode ativo; " +
                    "medindo na build, o monitoramento liga sozinho quando a build conecta.\n" +
                    "• Procurar build já em execução — conecta a uma build que já está aberta, sem compilar de novo.\n" +
                    "• Semáforo (CPU, GPU, GC, draw calls, memória) — verde até 80% do orçamento, amarelo até 100%, vermelho acima. " +
                    "Branco (⚪) significa que aquela métrica não tem veredito, e a linha diz por quê.\n" +
                    "• Valores e orçamentos — cada número aparece ao lado do limite e da origem desse limite.\n" +
                    "• Aquecimento — os primeiros 2 s são ignorados: carga de cena e compilação de shader não são custo do jogo rodando.\n" +
                    "• Snapshots capturados — quantos quadros estouraram o orçamento, por tipo. Ao parar, \"Ir para Snapshots\" leva à próxima etapa.\n\n" +
                    "Os números são do processo medido: no Editor, da Unity inteira; na build, só do jogo.",
                    "Shows, frame by frame, how much the game spends while it runs and compares it with the target hardware's budget.\n\n" +
                    "• Target build / Change → — the hardware the game is judged against; \"Change\" takes you to the Build tab.\n" +
                    "• ▶ Monitor / ⏸ Stop monitoring — starts and ends collection. In the Editor, the button unlocks with Play Mode on; " +
                    "when measuring a build, monitoring turns on by itself when the build connects.\n" +
                    "• Look for a build already running — connects to a build that is already open, without building again.\n" +
                    "• Indicators (CPU, GPU, GC, draw calls, memory) — green up to 80% of the budget, yellow up to 100%, red above. " +
                    "White (⚪) means that metric has no verdict, and the line says why.\n" +
                    "• Values and budgets — every number appears next to its limit and where that limit comes from.\n" +
                    "• Warm-up — the first 2 s are ignored: scene loading and shader compilation are not the cost of the game running.\n" +
                    "• Captured snapshots — how many frames went over budget, by type. When you stop, \"Go to Snapshots\" takes you to the next step.\n\n" +
                    "The numbers belong to the measured process: in the Editor, the whole of Unity; on a build, only the game.",
                    "Muestra, frame a frame, cuánto gasta el juego mientras se ejecuta y lo compara con el presupuesto del hardware objetivo.\n\n" +
                    "• Build objetivo / Cambiar → — el hardware con el que se juzga el juego; \"Cambiar\" lleva a la pestaña Build.\n" +
                    "• ▶ Monitorear / ⏸ Detener monitoreo — inicia y termina la recolección. En el Editor, el botón se habilita con el Play Mode activo; " +
                    "midiendo la build, el monitoreo se activa solo cuando la build se conecta.\n" +
                    "• Buscar una build ya en ejecución — se conecta a una build que ya está abierta, sin volver a compilar.\n" +
                    "• Semáforo (CPU, GPU, GC, draw calls, memoria) — verde hasta el 80% del presupuesto, amarillo hasta el 100%, rojo por encima. " +
                    "Blanco (⚪) significa que esa métrica no tiene veredicto, y la línea explica por qué.\n" +
                    "• Valores y presupuestos — cada número aparece junto a su límite y al origen de ese límite.\n" +
                    "• Calentamiento — se ignoran los primeros 2 s: la carga de escena y la compilación de shaders no son costo del juego en ejecución.\n" +
                    "• Snapshots capturados — cuántos frames superaron el presupuesto, por tipo. Al detener, \"Ir a Snapshots\" lleva al siguiente paso.\n\n" +
                    "Los números son del proceso medido: en el Editor, de toda Unity; en la build, solo del juego.");

                case 1: return Loc.T(
                    "Define o hardware alvo — a máquina em que o jogo precisa rodar bem. Todos os orçamentos saem daqui.\n\n" +
                    "• Plataforma (PC, Mobile, VR) — muda quais peças e quais limites existem.\n" +
                    "• Onde medir — no Editor (Play normal) ou na build (o jogo compilado, sem o Editor no meio). " +
                    "Em Android só existe a build no aparelho conectado.\n" +
                    "• Compilar e executar — compila com Development Build e Autoconnect Profiler já marcados e abre o jogo no alvo.\n" +
                    "• Alvo do projeto (FPS) — decisão sua. O orçamento de frame é 1000 ÷ FPS; em mobile, 65% disso (folga térmica da Unity).\n" +
                    "• Esta máquina — a CPU deste computador e o custo medido por draw call. Servem para projetar a medição no PC alvo.\n" +
                    "• Processador, memória e placa de vídeo — peças do PC alvo, com o índice PassMark e a posição na Steam ao lado.\n" +
                    "• Teto de memória do jogo — sem teto, a memória só é mostrada; o teto pode ser definido pelo pico medido na build + 20%.\n" +
                    "• Headset (VR) — os limites vêm do que a Meta publica para cada modelo.\n" +
                    "• Configurações de render do projeto — compara o asset da URP do alvo com guias publicados. O plugin só lê; " +
                    "\"Por quê?\" e \"Fonte\" mostram o motivo e a referência.\n" +
                    "• Resumo e veredito — o que esta build espera e de onde vem cada conta.",
                    "Sets the target hardware — the machine the game has to run well on. Every budget comes from here.\n\n" +
                    "• Platform (PC, Mobile, VR) — changes which parts and which limits exist.\n" +
                    "• Where to measure — in the Editor (regular Play) or on the build (the compiled game, with no Editor in between). " +
                    "On Android there is only the build on the connected device.\n" +
                    "• Build and run — builds with Development Build and Autoconnect Profiler already ticked and opens the game on the target.\n" +
                    "• Project target (FPS) — your decision. The frame budget is 1000 ÷ FPS; on mobile, 65% of that (Unity's thermal headroom).\n" +
                    "• This machine — this computer's CPU and the measured cost per draw call. They are used to project the measurement onto the target PC.\n" +
                    "• Processor, memory and graphics card — parts of the target PC, with the PassMark index and the Steam rank next to them.\n" +
                    "• Game memory ceiling — without a ceiling, memory is only shown; the ceiling can be set from the peak measured on the build + 20%.\n" +
                    "• Headset (VR) — the limits come from what Meta publishes for each model.\n" +
                    "• Project render settings — compares the target's URP asset with published guides. The plugin only reads; " +
                    "\"Why?\" and \"Source\" show the reason and the reference.\n" +
                    "• Summary and verdict — what this build expects and where each calculation comes from.",
                    "Define el hardware objetivo: la máquina en la que el juego tiene que funcionar bien. Todos los presupuestos salen de aquí.\n\n" +
                    "• Plataforma (PC, Mobile, VR) — cambia qué piezas y qué límites existen.\n" +
                    "• Dónde medir — en el Editor (Play normal) o en la build (el juego compilado, sin el Editor de por medio). " +
                    "En Android solo existe la build en el dispositivo conectado.\n" +
                    "• Compilar y ejecutar — compila con Development Build y Autoconnect Profiler ya marcados y abre el juego en el objetivo.\n" +
                    "• Objetivo del proyecto (FPS) — decisión tuya. El presupuesto de frame es 1000 ÷ FPS; en móviles, el 65% (margen térmico de Unity).\n" +
                    "• Esta máquina — la CPU de este ordenador y el costo medido por draw call. Sirven para proyectar la medición al PC objetivo.\n" +
                    "• Procesador, memoria y tarjeta gráfica — piezas del PC objetivo, con el índice PassMark y la posición en Steam al lado.\n" +
                    "• Techo de memoria del juego — sin techo, la memoria solo se muestra; el techo puede salir del pico medido en la build + 20%.\n" +
                    "• Visor (VR) — los límites vienen de lo que Meta publica para cada modelo.\n" +
                    "• Configuración de render del proyecto — compara el asset de URP del objetivo con guías publicadas. El plugin solo lee; " +
                    "\"¿Por qué?\" y \"Fuente\" muestran el motivo y la referencia.\n" +
                    "• Resumen y veredicto — lo que espera esta build y de dónde sale cada cálculo.");

                case 2: return Loc.T(
                    "Lista os quadros que estouraram o orçamento durante a última sessão, separados por tipo de problema.\n\n" +
                    "• Tipos (CPU, GC, draw calls, GPU, memória) — até 10 causas distintas por tipo. A mesma causa repetida soma " +
                    "ocorrências em vez de ocupar outra vaga.\n" +
                    "• Caixa de seleção — escolhe quais snapshots vão para a IA. \"Marcar todos\" e \"Desmarcar todos\" agilizam.\n" +
                    "• ▸ — abre os detalhes do snapshot: todas as métricas daquele quadro e os responsáveis lidos do Profiler " +
                    "(a pilha de chamadas), quando existem.\n" +
                    "• Analisar selecionados com IA — envia os marcados ao provedor escolhido na Configuração. O laudo abre na aba Laudo.\n\n" +
                    "Pare o monitoramento antes de analisar: a coleta precisa estar encerrada.",
                    "Lists the frames that went over budget during the last session, grouped by kind of problem.\n\n" +
                    "• Kinds (CPU, GC, draw calls, GPU, memory) — up to 10 distinct causes per kind. The same cause repeated adds " +
                    "occurrences instead of taking another slot.\n" +
                    "• Checkbox — chooses which snapshots go to the AI. \"Select all\" and \"Deselect all\" speed it up.\n" +
                    "• ▸ — opens the snapshot's details: every metric of that frame and the culprits read from the Profiler " +
                    "(the call stack), when there are any.\n" +
                    "• Analyze selected with AI — sends the ticked ones to the provider chosen in Settings. The report opens in the Report tab.\n\n" +
                    "Stop monitoring before analyzing: collection has to be finished.",
                    "Lista los frames que superaron el presupuesto durante la última sesión, separados por tipo de problema.\n\n" +
                    "• Tipos (CPU, GC, draw calls, GPU, memoria) — hasta 10 causas distintas por tipo. La misma causa repetida suma " +
                    "ocurrencias en lugar de ocupar otro hueco.\n" +
                    "• Casilla — elige qué snapshots van a la IA. \"Marcar todos\" y \"Desmarcar todos\" lo agilizan.\n" +
                    "• ▸ — abre los detalles del snapshot: todas las métricas de ese frame y los responsables leídos del Profiler " +
                    "(la pila de llamadas), cuando los hay.\n" +
                    "• Analizar seleccionados con IA — envía los marcados al proveedor elegido en Configuración. El informe se abre en la pestaña Informe.\n\n" +
                    "Detén el monitoreo antes de analizar: la recolección tiene que haber terminado.");

                case 3: return Loc.T(
                    "Escolhe o idioma da ferramenta e a IA que gera o laudo e responde no chat.\n\n" +
                    "• Idioma — vale para a interface inteira, para o laudo e para o chat. O padrão é o idioma do sistema.\n" +
                    "• Serviço — Google Gemini, OpenAI ou Anthropic (na nuvem, com a sua chave) ou Ollama (local, nada sai da máquina).\n" +
                    "• 💰 Custos e requisitos — o que cada provedor exige para funcionar, se é pago e o que significam os erros comuns. " +
                    "A API é cobrada à parte da assinatura do chat (ChatGPT, Claude, Gemini).\n" +
                    "• Chave da API — fica só nesta máquina (EditorPrefs), nunca no projeto. \"Onde obter a chave\" abre a página do provedor.\n" +
                    "• Endereço (Ollama) — onde o servidor local responde; o padrão é o da instalação.\n" +
                    "• Buscar modelos disponíveis — pergunta ao provedor quais modelos de texto ele oferece para a sua chave.\n" +
                    "• Modelo — ✓ respondeu ao teste, ✗ falhou, sem marca ainda não testado.\n" +
                    "• Testar este modelo — faz 1 chamada curta para confirmar que a sua conta pode usar o modelo. A lista mostra o que o " +
                    "provedor oferece, não a sua cota.",
                    "Chooses the tool's language and the AI that writes the report and answers in the chat.\n\n" +
                    "• Language — applies to the whole interface, the report and the chat. The default is the system language.\n" +
                    "• Service — Google Gemini, OpenAI or Anthropic (cloud, with your key) or Ollama (local, nothing leaves the machine).\n" +
                    "• 💰 Costs and requirements — what each provider needs to work, whether it is paid and what the common errors mean. " +
                    "The API is billed separately from the chat subscription (ChatGPT, Claude, Gemini).\n" +
                    "• API key — stays only on this machine (EditorPrefs), never in the project. \"Where to get a key\" opens the provider's page.\n" +
                    "• Address (Ollama) — where the local server answers; the default is the installation's.\n" +
                    "• Fetch available models — asks the provider which text models it offers for your key.\n" +
                    "• Model — ✓ passed the test, ✗ failed, no mark not tested yet.\n" +
                    "• Test this model — makes 1 short call to confirm your account can use the model. The list shows what the " +
                    "provider offers, not your quota.",
                    "Elige el idioma de la herramienta y la IA que genera el informe y responde en el chat.\n\n" +
                    "• Idioma — se aplica a toda la interfaz, al informe y al chat. Por defecto, el idioma del sistema.\n" +
                    "• Servicio — Google Gemini, OpenAI o Anthropic (en la nube, con tu clave) u Ollama (local, nada sale de la máquina).\n" +
                    "• 💰 Costos y requisitos — lo que cada proveedor exige para funcionar, si es de pago y qué significan los errores comunes. " +
                    "La API se cobra aparte de la suscripción al chat (ChatGPT, Claude, Gemini).\n" +
                    "• Clave de la API — queda solo en esta máquina (EditorPrefs), nunca en el proyecto. \"Dónde obtener la clave\" abre la página del proveedor.\n" +
                    "• Dirección (Ollama) — dónde responde el servidor local; por defecto, la de la instalación.\n" +
                    "• Buscar modelos disponibles — pregunta al proveedor qué modelos de texto ofrece para tu clave.\n" +
                    "• Modelo — ✓ pasó la prueba, ✗ falló, sin marca aún no probado.\n" +
                    "• Probar este modelo — hace 1 llamada corta para confirmar que tu cuenta puede usar el modelo. La lista muestra lo que " +
                    "ofrece el proveedor, no tu cuota.");

                case 4: return Loc.T(
                    "Mostra o laudo que a IA gerou a partir dos snapshots enviados.\n\n" +
                    "• Problema — um bloco por gargalo diagnosticado, com o tipo (CPU, GC, draw calls, GPU, memória).\n" +
                    "• Causa raiz — o que a IA identificou nos dados medidos como origem do custo.\n" +
                    "• Código refatorado — uma sugestão de correção em C#. \"Copiar código\" leva o trecho para a área de transferência.\n" +
                    "• Explicação — por que a correção resolve, em linguagem direta.\n" +
                    "• Tirar dúvida sobre este problema — abre o Chat com a pergunta pronta sobre aquele item.\n" +
                    "• Dicas de Level Design — quando o problema está na cena (objetos, luzes, materiais), e não no código.\n\n" +
                    "O laudo é sugestão, não ordem: revise antes de aplicar e meça de novo depois.",
                    "Shows the report the AI produced from the snapshots you sent.\n\n" +
                    "• Issue — one block per diagnosed bottleneck, with its kind (CPU, GC, draw calls, GPU, memory).\n" +
                    "• Root cause — what the AI identified in the measured data as the source of the cost.\n" +
                    "• Refactored code — a suggested fix in C#. \"Copy code\" puts the snippet on the clipboard.\n" +
                    "• Explanation — why the fix works, in plain language.\n" +
                    "• Ask about this issue — opens the Chat with a ready question about that item.\n" +
                    "• Level design tips — when the problem is in the scene (objects, lights, materials), not in the code.\n\n" +
                    "The report is a suggestion, not an order: review it before applying and measure again afterwards.",
                    "Muestra el informe que generó la IA a partir de los snapshots enviados.\n\n" +
                    "• Problema — un bloque por cuello de botella diagnosticado, con su tipo (CPU, GC, draw calls, GPU, memoria).\n" +
                    "• Causa raíz — lo que la IA identificó en los datos medidos como origen del costo.\n" +
                    "• Código refactorizado — una sugerencia de corrección en C#. \"Copiar código\" lleva el fragmento al portapapeles.\n" +
                    "• Explicación — por qué la corrección funciona, en lenguaje directo.\n" +
                    "• Preguntar sobre este problema — abre el Chat con la pregunta lista sobre ese punto.\n" +
                    "• Consejos de diseño de niveles — cuando el problema está en la escena (objetos, luces, materiales), no en el código.\n\n" +
                    "El informe es una sugerencia, no una orden: revísalo antes de aplicarlo y vuelve a medir después.");

                default: return Loc.T(
                    "Conversa com a IA sobre o laudo e sobre o seu projeto.\n\n" +
                    "• A IA já recebe o resumo da sessão medida e o último laudo — não precisa colar os números.\n" +
                    "• Anexar script — envia um arquivo .cs (até 100 KB) para a resposta usar o seu código real, e não um exemplo genérico.\n" +
                    "• Enviar — manda a pergunta. \"Copiar\" em cada resposta copia o texto.\n" +
                    "• Tentar de novo — aparece quando a chamada falha (sem rede, cota, provedor sobrecarregado).\n" +
                    "• Limpar conversa — apaga o histórico. Ele sobrevive ao Play Mode e a recompilações, e zera ao fechar a Unity.",
                    "Talk to the AI about the report and about your project.\n\n" +
                    "• The AI already gets the summary of the measured session and the last report — no need to paste the numbers.\n" +
                    "• Attach script — sends a .cs file (up to 100 KB) so the answer uses your real code, not a generic example.\n" +
                    "• Send — sends the question. \"Copy\" on each answer copies the text.\n" +
                    "• Try again — shows up when the call fails (no network, quota, overloaded provider).\n" +
                    "• Clear chat — deletes the history. It survives Play Mode and recompiles, and resets when Unity closes.",
                    "Conversa con la IA sobre el informe y sobre tu proyecto.\n\n" +
                    "• La IA ya recibe el resumen de la sesión medida y el último informe: no hace falta pegar los números.\n" +
                    "• Adjuntar script — envía un archivo .cs (hasta 100 KB) para que la respuesta use tu código real y no un ejemplo genérico.\n" +
                    "• Enviar — manda la pregunta. \"Copiar\" en cada respuesta copia el texto.\n" +
                    "• Reintentar — aparece cuando la llamada falla (sin red, cuota, proveedor sobrecargado).\n" +
                    "• Borrar conversación — elimina el historial. Sobrevive al Play Mode y a las recompilaciones, y se vacía al cerrar Unity.");
            }
        }
    }
}
