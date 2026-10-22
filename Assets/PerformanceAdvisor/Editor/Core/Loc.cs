using System;
using System.Globalization;
using UnityEditor;

namespace PerformanceAdvisor
{
    /// <summary>Languages the plugin's interface and AI output can use.</summary>
    public enum UiLanguage
    {
        English,
        Portuguese,
        Spanish
    }

    /// <summary>
    /// Picks the text for the language chosen in the Configuration tab.
    /// </summary>
    /// <remarks>
    /// MOTIVO do seletor no plugin: o pedido era seguir o idioma da engine, mas a Unity 6
    /// não tem português nem espanhol como idioma do Editor — os pacotes oferecidos são
    /// japonês, coreano e chinês (conferido no modules.json da 6000.4.6f1). Seguir a
    /// engine daria inglês para todo mundo. Então o padrão é o idioma do SISTEMA, e o dev
    /// pode trocar na aba Configuração.
    ///
    /// MOTIVO das três strings lado a lado em cada ponto, em vez de chaves num arquivo
    /// de tradução: o texto fica junto do código que o usa, nenhum idioma pode ficar
    /// faltando sem erro de compilação, e revisar uma tela é ler um arquivo só.
    ///
    /// Custo zero no OnGUI: T devolve uma das strings literais recebidas, sem alocar.
    /// </remarks>
    public static class Loc
    {
        private const string PREF = "PerformanceAdvisor_Language";

        /// <summary>Raised when the language changes, so open windows can repaint.</summary>
        public static event Action Changed;

        private static UiLanguage? _cached;

        /// <summary>The language in use: the one chosen, or the system's until one is chosen.</summary>
        public static UiLanguage Current
        {
            get
            {
                if (_cached == null)
                    _cached = EditorPrefs.HasKey(PREF) ? (UiLanguage)EditorPrefs.GetInt(PREF) : SystemLanguage();
                return _cached.Value;
            }
            set
            {
                EditorPrefs.SetInt(PREF, (int)value);
                _cached = value;
                Changed?.Invoke();
            }
        }

        /// <summary>Language of the operating system, mapped to the three the plugin has.</summary>
        public static UiLanguage SystemLanguage()
        {
            string iso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            if (iso == "pt") return UiLanguage.Portuguese;
            if (iso == "es") return UiLanguage.Spanish;
            return UiLanguage.English;
        }

        /// <summary>Text in the current language.</summary>
        public static string T(string pt, string en, string es)
        {
            switch (Current)
            {
                case UiLanguage.Portuguese: return pt;
                case UiLanguage.Spanish:    return es;
                default:                    return en;
            }
        }

        /// <summary>Name of each language, written in that language, for the selector.</summary>
        public static readonly string[] Names = { "English", "Português", "Español" };

        /// <summary>
        /// Instruction appended to the AI system prompts so the report and chat come back
        /// in the chosen language.
        /// </summary>
        /// <remarks>
        /// MOTIVO: os prompts de sistema continuam em português (é o texto documentado no
        /// TIC, seção A.4.3) — o que muda é só o idioma da RESPOSTA. As chaves do JSON
        /// (tipo, causa_raiz...) ficam como estão: o plugin as lê pelo nome.
        /// </remarks>
        public static string AiLanguageInstruction()
        {
            switch (Current)
            {
                case UiLanguage.English:
                    return "IDIOMA DA RESPOSTA: escreva todo o texto (causa_raiz, explicacao_didatica, " +
                           "dicas_level_design, respostas do chat e comentários de código) em inglês. " +
                           "Não traduza as chaves do JSON.";
                case UiLanguage.Spanish:
                    return "IDIOMA DA RESPOSTA: escreva todo o texto (causa_raiz, explicacao_didatica, " +
                           "dicas_level_design, respostas do chat e comentários de código) em espanhol. " +
                           "Não traduza as chaves do JSON.";
                default:
                    return "IDIOMA DA RESPOSTA: escreva todo o texto (causa_raiz, explicacao_didatica, " +
                           "dicas_level_design, respostas do chat e comentários de código) em português do Brasil. " +
                           "Não traduza as chaves do JSON.";
            }
        }
    }
}
