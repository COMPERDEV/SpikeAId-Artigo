using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace PerformanceAdvisor
{
    /// <summary>One project script attached to the report request.</summary>
    [Serializable]
    public class ScriptSource
    {
        public string file;   // caminho no projeto (Assets/...)
        public string code;
    }

    /// <summary>
    /// Finds the source files of the project classes named in culprit call stacks.
    /// </summary>
    /// <remarks>
    /// MOTIVO: a pilha do Profiler traz "StressTest.dll!PerformanceAdvisor.StressTest::StressTestScene.Update()"
    /// — o nome da classe, não o arquivo. A classe é procurada entre os MonoScripts do projeto
    /// (só em Assets/: código de pacote e da Unity não é do desenvolvedor e não se refatora).
    /// Marcadores da engine ("Update.ScriptRunBehaviourUpdate", "GC.Collect") não casam com
    /// nenhum script e são ignorados sem erro.
    /// </remarks>
    public static class ScriptSourceResolver
    {
        // MOTIVO do teto: o mesmo do anexo do chat. Acima disso a requisição pesa e o custo sobe
        // sem ganho — o culpado costuma estar num ou dois arquivos.
        public const int MAX_TOTAL_BYTES = 100 * 1024;

        private static readonly Regex CLASS_METHOD = new Regex(@"(?:::|\b)([A-Za-z_]\w*)\.([A-Za-z_]\w*)\s*\(");

        /// <summary>Class names that look like "Class.Method(" in the stacks.</summary>
        public static List<string> ClassesIn(IEnumerable<string> stacks)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<string>();
            foreach (string s in stacks)
            {
                if (string.IsNullOrEmpty(s)) continue;
                foreach (Match m in CLASS_METHOD.Matches(s))
                    if (seen.Add(m.Groups[1].Value)) list.Add(m.Groups[1].Value);
            }
            return list;
        }

        /// <summary>Source files (under Assets/) of the classes named in the stacks, within the size cap.</summary>
        public static ScriptSource[] Resolve(IEnumerable<string> stacks)
        {
            var result = new List<ScriptSource>();
            int total = 0;
            foreach (string cls in ClassesIn(stacks))
            {
                string path = FindScript(cls);
                if (path == null) continue;
                string code = File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n");
                if (total + code.Length > MAX_TOTAL_BYTES) break;
                total += code.Length;
                result.Add(new ScriptSource { file = path, code = code });
            }
            return result.ToArray();
        }

        private static string FindScript(string cls)
        {
            foreach (string guid in AssetDatabase.FindAssets(cls + " t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms == null) continue;
                Type t = ms.GetClass();
                bool match = t != null ? t.Name == cls : Path.GetFileNameWithoutExtension(path) == cls;
                if (match) return path;
            }
            return null;
        }
    }
}
