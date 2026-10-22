namespace PerformanceAdvisor
{
    /// <summary>
    /// What each AI provider needs to work: where the key comes from, whether it costs,
    /// and what the common errors mean.
    /// </summary>
    /// <remarks>
    /// MOTIVO: no teste com a Anthropic a chave listava os modelos e mesmo assim toda
    /// chamada voltava 400 — a conta não tinha crédito. A API de cada provedor é cobrada
    /// à parte da assinatura do chat (ChatGPT Plus, Claude Pro, Gemini Advanced), e quem
    /// já paga o chat razoavelmente espera que a chave funcione. Isso tem que estar na
    /// tela antes do erro, não depois.
    ///
    /// Sem valores em dinheiro de propósito: preço e limite de camada gratuita mudam sem
    /// aviso, e um número velho na tela seria pior que nenhum. O texto descreve o modelo
    /// de cobrança e aponta a página oficial de preços.
    /// </remarks>
    public static class ProviderHelp
    {
        /// <summary>Official pricing page of the provider.</summary>
        public static string PricingUrl(AiProviderKind p)
        {
            switch (p)
            {
                case AiProviderKind.Gemini:    return "https://ai.google.dev/gemini-api/docs/pricing";
                case AiProviderKind.OpenAi:    return "https://openai.com/api/pricing/";
                case AiProviderKind.Anthropic: return "https://www.anthropic.com/pricing";
                default:                       return "https://ollama.com/download";
            }
        }

        /// <summary>Opens the costs-and-requirements popup for this provider.</summary>
        public static void Show(AiProviderKind p)
        {
            ExplainerPopup.Show(
                Loc.T("Custos e requisitos — ", "Costs and requirements — ", "Costos y requisitos — ") + AiSettings.DisplayName(p),
                Body(p) + "\n\n" +
                Loc.T("Preços e limites mudam com o tempo. Página oficial: ",
                      "Prices and limits change over time. Official page: ",
                      "Los precios y límites cambian con el tiempo. Página oficial: ") + PricingUrl(p));
        }

        private static string Body(AiProviderKind p)
        {
            switch (p)
            {
                case AiProviderKind.Gemini: return Loc.T(
                    "• Chave: gerada no Google AI Studio com uma conta Google.\n" +
                    "• Custo: a API tem camada gratuita, com limite de requisições por minuto e por dia. Alguns modelos " +
                    "(em geral os \"Pro\") podem ter cota zero no plano gratuito — use \"Testar este modelo\" antes de gerar o laudo. " +
                    "Para passar dos limites, é preciso ativar o faturamento no Google Cloud.\n" +
                    "• A assinatura do app Gemini (Google One / Gemini Advanced) NÃO inclui a API.\n" +
                    "• Privacidade: segundo os termos do Google, o que é enviado na camada gratuita pode ser usado para " +
                    "melhorar os produtos deles. Na camada paga, não.\n\n" +
                    "Erros comuns:\n" +
                    "• 429 — cota esgotada (por minuto ou do dia) ou modelo sem cota no seu plano.\n" +
                    "• 400 \"API key not valid\" — chave copiada errada ou apagada.",
                    "• Key: created in Google AI Studio with a Google account.\n" +
                    "• Cost: the API has a free tier, with a limit of requests per minute and per day. Some models " +
                    "(usually the \"Pro\" ones) may have zero quota on the free tier — use \"Test this model\" before generating the report. " +
                    "To go beyond the limits, you need to enable billing in Google Cloud.\n" +
                    "• The Gemini app subscription (Google One / Gemini Advanced) does NOT include the API.\n" +
                    "• Privacy: under Google's terms, what you send on the free tier may be used to improve " +
                    "their products. On the paid tier, it is not.\n\n" +
                    "Common errors:\n" +
                    "• 429 — quota used up (per minute or for the day) or a model with no quota on your plan.\n" +
                    "• 400 \"API key not valid\" — key copied wrong or deleted.",
                    "• Clave: se genera en Google AI Studio con una cuenta de Google.\n" +
                    "• Costo: la API tiene un nivel gratuito, con límite de solicitudes por minuto y por día. Algunos modelos " +
                    "(en general los \"Pro\") pueden tener cuota cero en el nivel gratuito: usa \"Probar este modelo\" antes de generar el informe. " +
                    "Para superar los límites hay que activar la facturación en Google Cloud.\n" +
                    "• La suscripción de la app Gemini (Google One / Gemini Advanced) NO incluye la API.\n" +
                    "• Privacidad: según los términos de Google, lo que se envía en el nivel gratuito puede usarse para " +
                    "mejorar sus productos. En el nivel de pago, no.\n\n" +
                    "Errores comunes:\n" +
                    "• 429 — cuota agotada (por minuto o del día) o modelo sin cuota en tu plan.\n" +
                    "• 400 \"API key not valid\" — clave copiada mal o eliminada.");

                case AiProviderKind.OpenAi: return Loc.T(
                    "• Chave: gerada em platform.openai.com (API keys).\n" +
                    "• Custo: a API é PAGA À PARTE, com crédito pré-pago adicionado em Billing. A assinatura do ChatGPT " +
                    "(Plus, Pro, Team) — inclusive o Codex que vem nesses planos — NÃO dá crédito na API: são contas de cobrança " +
                    "separadas, mesmo com o mesmo login.\n" +
                    "• Alguns modelos mais novos exigem que a organização seja verificada no painel da OpenAI.\n\n" +
                    "Erros comuns:\n" +
                    "• 429 com \"insufficient_quota\" — a conta da API está sem crédito.\n" +
                    "• 429 sem esse texto — limite por minuto; espere e tente de novo.\n" +
                    "• 401 — chave inválida ou revogada.\n" +
                    "• 403/404 no teste — a chave não tem acesso àquele modelo.",
                    "• Key: created at platform.openai.com (API keys).\n" +
                    "• Cost: the API is BILLED SEPARATELY, with prepaid credit added under Billing. A ChatGPT subscription " +
                    "(Plus, Pro, Team) — including the Codex that comes with those plans — does NOT give API credit: they are " +
                    "separate billing accounts, even with the same login.\n" +
                    "• Some newer models require the organization to be verified in the OpenAI dashboard.\n\n" +
                    "Common errors:\n" +
                    "• 429 with \"insufficient_quota\" — the API account has no credit.\n" +
                    "• 429 without that text — per-minute limit; wait and try again.\n" +
                    "• 401 — invalid or revoked key.\n" +
                    "• 403/404 in the test — the key has no access to that model.",
                    "• Clave: se genera en platform.openai.com (API keys).\n" +
                    "• Costo: la API se COBRA APARTE, con crédito prepago añadido en Billing. La suscripción de ChatGPT " +
                    "(Plus, Pro, Team), incluido el Codex que viene en esos planes, NO da crédito en la API: son cuentas de " +
                    "facturación distintas, aunque usen el mismo inicio de sesión.\n" +
                    "• Algunos modelos más nuevos exigen verificar la organización en el panel de OpenAI.\n\n" +
                    "Errores comunes:\n" +
                    "• 429 con \"insufficient_quota\" — la cuenta de la API no tiene crédito.\n" +
                    "• 429 sin ese texto — límite por minuto; espera y vuelve a intentarlo.\n" +
                    "• 401 — clave inválida o revocada.\n" +
                    "• 403/404 en la prueba — la clave no tiene acceso a ese modelo.");

                case AiProviderKind.Anthropic: return Loc.T(
                    "• Chave: gerada em console.anthropic.com (API Keys).\n" +
                    "• Custo: a API é PAGA À PARTE, com crédito pré-pago comprado em Plans & Billing. A assinatura do Claude " +
                    "(Pro, Max, Team) NÃO dá crédito na API: são cobranças separadas, mesmo com o mesmo login.\n" +
                    "• Uma chave válida lista os modelos mesmo sem crédito — por isso a lista aparece e o teste falha.\n" +
                    "• Os limites por minuto sobem conforme o gasto acumulado na conta.\n\n" +
                    "Erros comuns:\n" +
                    "• 400 \"credit balance is too low\" — a conta está sem crédito.\n" +
                    "• 401 — chave inválida ou revogada.\n" +
                    "• 429 — limite por minuto; espere e tente de novo.\n" +
                    "• 529 / 5xx — Anthropic sobrecarregada; o plugin tenta de novo sozinho.",
                    "• Key: created at console.anthropic.com (API Keys).\n" +
                    "• Cost: the API is BILLED SEPARATELY, with prepaid credit bought under Plans & Billing. A Claude subscription " +
                    "(Pro, Max, Team) does NOT give API credit: they are separate charges, even with the same login.\n" +
                    "• A valid key lists the models even without credit — that is why the list shows up and the test fails.\n" +
                    "• Per-minute limits go up as the account's accumulated spend grows.\n\n" +
                    "Common errors:\n" +
                    "• 400 \"credit balance is too low\" — the account has no credit.\n" +
                    "• 401 — invalid or revoked key.\n" +
                    "• 429 — per-minute limit; wait and try again.\n" +
                    "• 529 / 5xx — Anthropic overloaded; the plugin retries on its own.",
                    "• Clave: se genera en console.anthropic.com (API Keys).\n" +
                    "• Costo: la API se COBRA APARTE, con crédito prepago comprado en Plans & Billing. La suscripción de Claude " +
                    "(Pro, Max, Team) NO da crédito en la API: son cobros distintos, aunque usen el mismo inicio de sesión.\n" +
                    "• Una clave válida lista los modelos aunque no haya crédito: por eso aparece la lista y la prueba falla.\n" +
                    "• Los límites por minuto suben a medida que crece el gasto acumulado de la cuenta.\n\n" +
                    "Errores comunes:\n" +
                    "• 400 \"credit balance is too low\" — la cuenta no tiene crédito.\n" +
                    "• 401 — clave inválida o revocada.\n" +
                    "• 429 — límite por minuto; espera y vuelve a intentarlo.\n" +
                    "• 529 / 5xx — Anthropic sobrecargada; el plugin reintenta solo.");

                default: return Loc.T(
                    "• Sem chave e sem custo: o modelo roda no seu computador e nada do projeto sai da máquina.\n" +
                    "• Requisitos: instalar o Ollama, baixar um modelo com 'ollama pull <modelo>' e deixar o servidor " +
                    "rodando. O modelo precisa caber na memória (RAM ou memória da placa de vídeo) — modelos maiores pedem mais.\n" +
                    "• Qualidade: modelos pequenos são mais rápidos, mas dão laudos mais fracos e às vezes não seguem o " +
                    "formato JSON do laudo. Se o laudo falhar ao ser lido, tente um modelo maior.\n\n" +
                    "Erros comuns:\n" +
                    "• \"Não foi possível falar com o Ollama\" — o servidor não está rodando ('ollama serve') ou o endereço está errado.\n" +
                    "• Lista vazia — nenhum modelo baixado ainda.\n" +
                    "• Tempo limite — o modelo é pesado demais para esta máquina.",
                    "• No key and no cost: the model runs on your computer and nothing from the project leaves the machine.\n" +
                    "• Requirements: install Ollama, download a model with 'ollama pull <model>' and keep the server " +
                    "running. The model has to fit in memory (RAM or graphics card memory) — bigger models need more.\n" +
                    "• Quality: small models are faster, but give weaker reports and sometimes don't follow the " +
                    "report's JSON format. If the report fails to parse, try a bigger model.\n\n" +
                    "Common errors:\n" +
                    "• \"Could not reach Ollama\" — the server is not running ('ollama serve') or the address is wrong.\n" +
                    "• Empty list — no model downloaded yet.\n" +
                    "• Timeout — the model is too heavy for this machine.",
                    "• Sin clave y sin costo: el modelo se ejecuta en tu ordenador y nada del proyecto sale de la máquina.\n" +
                    "• Requisitos: instalar Ollama, descargar un modelo con 'ollama pull <modelo>' y dejar el servidor " +
                    "en ejecución. El modelo tiene que caber en memoria (RAM o memoria de la tarjeta gráfica): los modelos más grandes piden más.\n" +
                    "• Calidad: los modelos pequeños son más rápidos, pero dan informes más débiles y a veces no siguen el " +
                    "formato JSON del informe. Si el informe no se puede leer, prueba un modelo más grande.\n\n" +
                    "Errores comunes:\n" +
                    "• \"No se pudo conectar con Ollama\" — el servidor no se está ejecutando ('ollama serve') o la dirección es incorrecta.\n" +
                    "• Lista vacía — todavía no hay ningún modelo descargado.\n" +
                    "• Tiempo de espera agotado — el modelo es demasiado pesado para esta máquina.");
            }
        }
    }
}
