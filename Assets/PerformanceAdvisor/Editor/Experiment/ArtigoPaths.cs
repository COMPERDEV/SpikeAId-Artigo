using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace PerformanceAdvisor.Artigo
{
    /// <summary>Experiment settings read from Artigo/config.json.</summary>
    [Serializable]
    public class ArtigoConfig
    {
        public string modelo = "gemini-3.5-flash";
        public int    repeticoes = 5;
        public float  intervaloSegundos = 8f;
        public float  esperaApos429Segundos = 65f;
        public int    maxTentativasPor429 = 6;
        // Preço por milhão de tokens (US$). O raciocínio do modelo é cobrado como saída.
        public double precoEntradaUsdMilhao = 0.75;
        public double precoSaidaUsdMilhao = 3.75;
        // Teto de gasto das duas condições somadas; a coleta para antes de passar dele.
        public double limiteGastoUsd = 3.6;
    }

    /// <summary>
    /// Where the article experiment keeps its files, and which prompt condition this branch runs.
    /// </summary>
    /// <remarks>
    /// MOTIVO: as duas branches do artigo (artigo-prompt-amador e artigo-prompt-profissional)
    /// têm o MESMO código e os dois prompts. O que muda entre elas é uma linha só, em
    /// Artigo/condicao.txt. Assim "a única variável é o texto do prompt" (seção 5.1 do
    /// artigo) é verificável com um git diff entre as branches, em vez de ser promessa.
    ///
    /// A pasta Artigo/ fica na raiz do repositório, fora de Assets: casos, respostas brutas e
    /// resultados são dados do experimento, publicados com ele, e não fazem parte do plugin.
    /// </remarks>
    public static class ArtigoPaths
    {
        /// <summary>Repository root (the folder that contains Assets/).</summary>
        public static string Root => Directory.GetParent(Application.dataPath).FullName;

        public static string Dir        => Path.Combine(Root, "Artigo");
        public static string CasesDir   => Path.Combine(Dir, "casos");
        public static string ScenesDir  => "Assets/PerformanceAdvisor/StressScenes/Artigo";

        public static string ResponsesDir(string condicao) => Path.Combine(Dir, "respostas", condicao);
        public static string ResultsDir(string condicao)   => Path.Combine(Dir, "resultados", condicao);
        public static string ComparisonDir                  => Path.Combine(Dir, "resultados");

        /// <summary>True only in the two article branches.</summary>
        public static bool IsExperimentBranch => File.Exists(Path.Combine(Dir, "condicao.txt"));

        /// <summary>"amador" or "profissional", from Artigo/condicao.txt.</summary>
        public static string Condition
        {
            get
            {
                string f = Path.Combine(Dir, "condicao.txt");
                if (!File.Exists(f)) return null;
                return File.ReadAllText(f, Encoding.UTF8).Trim();
            }
        }

        /// <summary>System prompt of a condition, read verbatim.</summary>
        public static string Prompt(string condicao)
        {
            string f = Path.Combine(Dir, "prompts", "prompt_" + condicao + ".txt");
            if (!File.Exists(f)) throw new FileNotFoundException("Prompt da condição não encontrado", f);
            // MOTIVO: normaliza CRLF → LF. O mesmo arquivo clonado no Windows e no Linux não
            // pode virar dois prompts diferentes por causa do fim de linha.
            return File.ReadAllText(f, Encoding.UTF8).Replace("\r\n", "\n").TrimEnd();
        }

        /// <summary>Prompt of this branch's condition, or null outside the article branches.</summary>
        public static string ActivePrompt
        {
            get
            {
                string c = Condition;
                return string.IsNullOrEmpty(c) ? null : Prompt(c);
            }
        }

        /// <summary>The Gemini generationConfig (response schema + temperature), verbatim.</summary>
        public static string GenerationConfigJson
        {
            get
            {
                string f = Path.Combine(Dir, "response_schema.json");
                return File.Exists(f) ? File.ReadAllText(f, Encoding.UTF8).Trim() : null;
            }
        }

        public static ArtigoConfig Config
        {
            get
            {
                string f = Path.Combine(Dir, "config.json");
                return File.Exists(f)
                    ? JsonUtility.FromJson<ArtigoConfig>(File.ReadAllText(f, Encoding.UTF8))
                    : new ArtigoConfig();
            }
        }

        public static void WriteUtf8(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
    }
}
