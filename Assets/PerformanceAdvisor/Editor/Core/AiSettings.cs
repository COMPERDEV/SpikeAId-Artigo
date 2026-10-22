using UnityEditor;

namespace PerformanceAdvisor
{
    /// <summary>AI back ends the plugin can talk to.</summary>
    /// <remarks>
    /// MOTIVO: o plugin era amarrado ao Gemini, com os modelos num enum fixo no código.
    /// Dois problemas que apareceram na banca do TIC1:
    ///
    ///  - **Modelos envelhecem.** O enum listava versões que a API já tinha aposentado, e
    ///    o dev não tinha como escolher o que a chave DELE de fato atende. Agora a lista
    ///    é buscada no provedor, com a chave do próprio dev.
    ///  - **Privacidade** (apontamento do avaliador Tiago): mandar código-fonte do jogo
    ///    para uma nuvem de terceiros não serve para todo mundo. Ollama roda na máquina
    ///    do dev e fecha esse caso sem o projeto sair dali.
    ///
    /// A ordem aqui não é estética: Ollama expõe API compatível com a da OpenAI, então
    /// implementar OpenAI entrega Ollama quase de graça — e é por isso que os dois
    /// compartilham o formato de corpo e de resposta.
    /// </remarks>
    public enum AiProviderKind
    {
        Gemini,
        OpenAi,
        Anthropic,
        Ollama
    }

    /// <summary>
    /// Persisted plugin settings: provider, per-provider API key, chosen model.
    /// </summary>
    /// <remarks>
    /// MOTIVO: único lugar que toca EditorPrefs — chaves e modelo ficam por máquina,
    /// nunca serializados no projeto (uma chave commitada é uma chave vazada).
    ///
    /// A chave é guardada POR PROVEDOR, não numa variável só. Quem tem conta em dois
    /// serviços não deveria perder uma chave ao experimentar a outra, e sobrescrever
    /// silenciosamente é o tipo de coisa que só se descobre quando já aconteceu.
    /// </remarks>
    public static class AiSettings
    {
        private const string PROVIDER_PREF = "PerformanceAdvisor_AiProvider";
        private const string KEY_PREF      = "PerformanceAdvisor_AiKey_";   // + provedor
        private const string MODEL_PREF    = "PerformanceAdvisor_AiModel_"; // + provedor
        private const string OLLAMA_PREF   = "PerformanceAdvisor_OllamaUrl";

        /// <summary>Endpoint where a local Ollama server is expected.</summary>
        /// <remarks>
        /// MOTIVO: editável porque nem todo mundo roda na porta padrão, e quem usa
        /// Ollama numa máquina da rede precisa apontar para ela.
        /// </remarks>
        public const string OLLAMA_DEFAULT_URL = "http://localhost:11434";

        public static AiProviderKind Provider
        {
            get => (AiProviderKind)EditorPrefs.GetInt(PROVIDER_PREF, (int)AiProviderKind.Gemini);
            set => EditorPrefs.SetInt(PROVIDER_PREF, (int)value);
        }

        public static string OllamaUrl
        {
            get
            {
                string v = EditorPrefs.GetString(OLLAMA_PREF, OLLAMA_DEFAULT_URL);
                return string.IsNullOrEmpty(v) ? OLLAMA_DEFAULT_URL : v.TrimEnd('/');
            }
            set => EditorPrefs.SetString(OLLAMA_PREF, string.IsNullOrEmpty(value) ? OLLAMA_DEFAULT_URL : value);
        }

        /// <summary>
        /// True when the report attaches the source of the project scripts named in the call stacks.
        /// </summary>
        /// <remarks>
        /// MOTIVO: com só a telemetria, o modelo conhece o NOME do método culpado, não o corpo.
        /// No experimento do artigo, o prompt que proíbe inventar símbolos deixou o código vazio
        /// em 100% dos achados — e o prompt que não proíbe "corrigiu" inventando um Update
        /// genérico que não era o do projeto. Correção ancorada exige o código. Ligado por
        /// padrão, com aviso na aba Configuração: o código do projeto vai ao provedor (com o
        /// Ollama, fica na máquina).
        /// </remarks>
        public static bool SendScriptSources
        {
            get => EditorPrefs.GetBool(SOURCES_PREF, true);
            set => EditorPrefs.SetBool(SOURCES_PREF, value);
        }
        private const string SOURCES_PREF = "PerformanceAdvisor_SendScriptSources";

        /// <summary>API key for a given provider. Empty for Ollama, which needs none.</summary>
        public static string GetApiKey(AiProviderKind p) => EditorPrefs.GetString(KEY_PREF + p, "");

        public static void SetApiKey(AiProviderKind p, string value)
            => EditorPrefs.SetString(KEY_PREF + p, value ?? "");

        /// <summary>Key of the provider currently selected.</summary>
        public static string ApiKey
        {
            get => GetApiKey(Provider);
            set => SetApiKey(Provider, value);
        }

        /// <summary>
        /// True when the active provider is ready to be called.
        /// </summary>
        /// <remarks>
        /// MOTIVO: Ollama não usa chave — exigir uma tornaria o modo local inalcançável
        /// justamente para quem escolheu não mandar código para fora.
        /// </remarks>
        public static bool IsReady
        {
            get
            {
                if (Provider == AiProviderKind.Ollama) return !string.IsNullOrEmpty(SelectedModel);
                return !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(SelectedModel);
            }
        }

        /// <summary>Model id as the provider names it, e.g. "gemini-2.5-flash", "gpt-4o".</summary>
        /// <remarks>
        /// MOTIVO: string, não enum. O enum antigo congelava no código uma lista que o
        /// provedor muda sozinho — quando a Google aposentou uma versão, o plugin
        /// continuou oferecendo e a chamada passou a falhar sem explicação. Id textual
        /// vindo da própria API não tem esse problema.
        /// </remarks>
        public static string SelectedModel
        {
            get => EditorPrefs.GetString(MODEL_PREF + Provider, DefaultModelFor(Provider));
            set => EditorPrefs.SetString(MODEL_PREF + Provider, value ?? "");
        }

        /// <summary>
        /// A reasonable starting model, used only until the dev fetches the real list.
        /// </summary>
        /// <remarks>
        /// MOTIVO: é palpite, e palpite tem prazo — por isso a UI empurra o dev a buscar
        /// a lista real com a chave dele. Serve para o plugin não abrir com um campo
        /// vazio, não para ser a resposta final.
        /// </remarks>
        public static string DefaultModelFor(AiProviderKind p)
        {
            switch (p)
            {
                case AiProviderKind.Gemini:    return "gemini-2.5-flash";
                case AiProviderKind.OpenAi:    return "gpt-4o-mini";
                case AiProviderKind.Anthropic: return "claude-sonnet-5";
                case AiProviderKind.Ollama:    return "";
                default:                       return "";
            }
        }

        public static string DisplayName(AiProviderKind p)
        {
            switch (p)
            {
                case AiProviderKind.Gemini:    return "Google Gemini";
                case AiProviderKind.OpenAi:    return "OpenAI";
                case AiProviderKind.Anthropic: return "Anthropic Claude";
                case AiProviderKind.Ollama:    return "Ollama (local)";
                default:                       return p.ToString();
            }
        }

        /// <summary>Where the dev goes to create a key for this provider.</summary>
        public static string KeyHelpUrl(AiProviderKind p)
        {
            switch (p)
            {
                case AiProviderKind.Gemini:    return "https://aistudio.google.com/apikey";
                case AiProviderKind.OpenAi:    return "https://platform.openai.com/api-keys";
                case AiProviderKind.Anthropic: return "https://console.anthropic.com/settings/keys";
                case AiProviderKind.Ollama:    return "https://ollama.com/download";
                default:                       return "";
            }
        }

        /// <summary>True when this provider keeps the project's code on the dev's machine.</summary>
        /// <remarks>
        /// MOTIVO: o plugin manda TRECHOS DE CÓDIGO do jogo no payload. Quem escolhe o
        /// provedor precisa saber se aquilo sai da máquina, e essa informação não pode
        /// ficar escondida na documentação — atende o apontamento de privacidade.
        /// </remarks>
        public static bool IsLocal(AiProviderKind p) => p == AiProviderKind.Ollama;

        // ── Resultado do teste de modelo ────────────────────────────────────
        // MOTIVO: o teste custa uma requisição da cota do dev. Guardar o resultado evita
        // repetir a cada abertura da aba. Ele vale para AQUELA chave: outra conta pode ter
        // outra cota, então a chave entra na identificação — por impressão digital, nunca
        // em texto, para o valor da chave não aparecer em mais um lugar do EditorPrefs.
        private const string PROBE_PREF = "PerformanceAdvisor_ModelProbe_";
        private const string PROBE_OK   = "ok";

        /// <summary>Outcome of the last test of a model with the current key.</summary>
        public enum ProbeState { Untested, Ok, Failed }

        public static ProbeState GetProbe(string model, out string failure)
        {
            string v = EditorPrefs.GetString(ProbeKey(model), "");
            failure = null;
            if (v.Length == 0)  return ProbeState.Untested;
            if (v == PROBE_OK)  return ProbeState.Ok;
            failure = v;
            return ProbeState.Failed;
        }

        public static void SetProbe(string model, bool ok, string failure)
            => EditorPrefs.SetString(ProbeKey(model), ok ? PROBE_OK : (failure ?? "falhou"));

        private static string ProbeKey(string model)
        {
            AiProviderKind p = Provider;
            // Ollama não tem chave; o que muda o resultado é QUAL servidor responde.
            string credential = IsLocal(p) ? OllamaUrl : GetApiKey(p);
            return PROBE_PREF + p + "_" + Fingerprint(credential) + "_" + model;
        }

        // FNV-1a 32 bits: estável entre sessões (string.GetHashCode não é garantido ser).
        private static string Fingerprint(string s)
        {
            uint h = 2166136261;
            foreach (char c in s ?? "")
            {
                h ^= c;
                h *= 16777619;
            }
            return h.ToString("x8");
        }
    }
}
