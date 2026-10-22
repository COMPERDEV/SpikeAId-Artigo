using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace PerformanceAdvisor.Artigo
{
    /// <summary>Automatic metrics of one model answer, as defined in the article (Quadro 6).</summary>
    public class ArtigoMetrics
    {
        public string caso, grupo, condicao; public int repeticao;
        public bool   respondeu;              // a API devolveu texto
        public bool   formatoValido;
        public int    achados, esperados;
        public bool   contagemErrada;
        public bool   tipoErrado;
        public int    simbolosCitados, simbolosForaDaPilha, simbolosInventados;
        public bool   fabricacaoSimboloEstrita;   // algum símbolo fora da pilha (contrato R4 ao pé da letra)
        public bool   fabricacaoSimbolo;          // algum símbolo que não existe em lugar nenhum
        public int    valoresCitados, valoresFabricados, quadrosCitados, quadrosFabricados;
        public bool   fabricacaoValor;
        public bool   tetoAusenteViolado;
        public bool   dicasIndevidas, dicasAusentes, batchingEmGpu;
        public bool   vazamentoEscopo;
        public bool   indeterminacaoCorreta;      // só G5
        public bool   indeterminacaoIndevida;     // só G1–G4
        public bool   falsoEstouro;               // só G5 e G6
        public bool   validaPoremErrada;
        public int    achadosComCodigo;
        public bool   semCodigo;                  // nenhum achado trouxe código (medida pós-hoc)
        public string detalhe = "";

        public static readonly string[] Header =
        {
            "caso","grupo","condicao","repeticao","respondeu","formato_valido","achados","esperados",
            "contagem_errada","tipo_errado","simbolos_citados","simbolos_fora_da_pilha","simbolos_inventados",
            "fabricacao_simbolo_estrita","fabricacao_simbolo","valores_citados","valores_fabricados",
            "quadros_citados","quadros_fabricados","fabricacao_valor","teto_ausente_violado",
            "dicas_indevidas","dicas_ausentes","batching_em_gpu","vazamento_escopo",
            "indeterminacao_correta","indeterminacao_indevida","falso_estouro","valida_porem_errada",
            "achados_com_codigo","sem_codigo","detalhe"
        };

        public string ToCsv()
        {
            string B(bool b) => b ? "1" : "0";
            var f = new[]
            {
                caso, grupo, condicao, repeticao.ToString(), B(respondeu), B(formatoValido), achados.ToString(), esperados.ToString(),
                B(contagemErrada), B(tipoErrado), simbolosCitados.ToString(), simbolosForaDaPilha.ToString(), simbolosInventados.ToString(),
                B(fabricacaoSimboloEstrita), B(fabricacaoSimbolo), valoresCitados.ToString(), valoresFabricados.ToString(),
                quadrosCitados.ToString(), quadrosFabricados.ToString(), B(fabricacaoValor), B(tetoAusenteViolado),
                B(dicasIndevidas), B(dicasAusentes), B(batchingEmGpu), B(vazamentoEscopo),
                B(indeterminacaoCorreta), B(indeterminacaoIndevida), B(falsoEstouro), B(validaPoremErrada),
                achadosComCodigo.ToString(), B(semCodigo), detalhe
            };
            return string.Join(";", f.Select(Csv));
        }

        private static string Csv(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"" : s;
        }
    }

    /// <summary>
    /// Checks one report against the case that produced it. Every rule here is the
    /// operational definition of a metric in the article; change one, change the article.
    /// </summary>
    /// <remarks>
    /// MOTIVO das regras lexicais (estouro, batching, indeterminação): o laudo é texto em
    /// português dentro de um JSON. Detectar "afirmou que estourou" exige ler a frase, e a
    /// leitura aqui é por padrões declarados abaixo, sentença a sentença, com negação. É uma
    /// aproximação — o artigo declara isso como limitação e uma amostra é conferida à mão.
    /// O que é estrutural (contagem, tipo, símbolos, quadros, valores) não depende de leitura.
    /// </remarks>
    public static class ArtigoValidator
    {
        private static readonly string[] TIPOS = { "CPU", "GC", "DrawCalls", "GPU", "Memoria", "Geral" };

        public static string TypeOfArray(string array)
        {
            switch (array)
            {
                case "cpu_samples":       return "CPU";
                case "gc_samples":        return "GC";
                case "drawcalls_samples": return "DrawCalls";
                case "memory_samples":    return "Memoria";
                default:                  return "GPU";
            }
        }

        private class Finding { public string tipo, causa, codigo, explicacao; }

        public static ArtigoMetrics Validate(ArtigoCase c, string condicao, int rep, string answer)
        {
            var m = new ArtigoMetrics
            {
                caso = c.id, grupo = c.grupo, condicao = condicao, repeticao = rep,
                esperados = c.esperadoAchados, respondeu = !string.IsNullOrEmpty(answer)
            };
            if (!m.respondeu) { m.detalhe = "sem resposta"; return m; }

            // ── 1. Validade de formato: JSON estrito + campos do contrato ─────
            var findings = new List<Finding>();
            string dicas = null;
            try
            {
                var root = MiniJson.Parse(answer.Trim()) as Dictionary<string, object>;
                if (root == null) throw new FormatException("a raiz não é objeto");
                if (!root.TryGetValue("findings", out object fo) || !(fo is List<object> fl)) throw new FormatException("sem lista findings");
                foreach (object o in fl)
                {
                    if (!(o is Dictionary<string, object> fd)) throw new FormatException("finding não é objeto");
                    var f = new Finding
                    {
                        tipo = Str(fd, "tipo"), causa = Str(fd, "causa_raiz"),
                        codigo = Str(fd, "codigo_refatorado"), explicacao = Str(fd, "explicacao_didatica")
                    };
                    if (Array.IndexOf(TIPOS, f.tipo) < 0) throw new FormatException($"tipo fora da lista: {f.tipo}");
                    findings.Add(f);
                }
                if (root.TryGetValue("dicas_level_design", out object d))
                {
                    if (d != null && !(d is string)) throw new FormatException("dicas_level_design não é string nem null");
                    dicas = d as string;
                }
                m.formatoValido = true;
            }
            catch (FormatException ex)
            {
                m.formatoValido = false;
                m.detalhe = "formato: " + ex.Message;
                m.validaPoremErrada = false;
                return m;   // sem estrutura não há o que medir nas outras classes
            }

            m.achados = findings.Count;
            m.achadosComCodigo = findings.Count(f => !string.IsNullOrWhiteSpace(f.codigo));
            m.semCodigo = m.achadosComCodigo == 0;

            // ── 2. Contagem e tipo ─────────────────────────────────────────────
            m.contagemErrada = findings.Count != c.esperadoAchados;
            m.tipoErrado = !TypesMatch(c, findings);

            // ── 3. Fabricação de símbolos (código) ─────────────────────────────
            foreach (var f in findings)
            {
                if (string.IsNullOrWhiteSpace(f.codigo)) continue;
                var allowed = StackSymbols(c, f.tipo);
                var declared = DeclaredSymbols(f.codigo);
                foreach (string sym in CitedSymbols(f.codigo))
                {
                    m.simbolosCitados++;
                    if (allowed.Contains(sym)) continue;
                    m.simbolosForaDaPilha++;
                    if (!declared.Contains(sym) && !KnownApi.Contains(sym))
                    {
                        m.simbolosInventados++;
                        m.detalhe += $"símbolo inventado: {sym}; ";
                    }
                }
            }
            m.fabricacaoSimboloEstrita = m.simbolosForaDaPilha > 0;
            m.fabricacaoSimbolo = m.simbolosInventados > 0;

            // ── 4. Fabricação de valores e quadros (texto) ─────────────────────
            var frames = new HashSet<int>(c.amostras.Select(a => a.frame));
            var numbers = PayloadNumbers(c.userText);
            // MOTIVO: "cerca de 22 ms", "mais de 120 ms" — aproximação declarada pelo próprio
            // texto não é fabricação. Visto nas respostas reais (22 para 21,92; 120 para 122,68).
            // Com o marcador, a tolerância vira 10% do valor. Sem marcador, um valor DERIVADO
            // (soma, diferença) continua contando: no caso real, "43,79 + 17,94 = 61,73 ms,
            // ultrapassando o orçamento" era exatamente uma alucinação de estouro.
            var approx = new Regex(@"(?:~|≈|cerca de|aproximadamente|aprox\.?|quase|mais de|menos de|acima de|abaixo de|superior a|inferior a|por volta de|em torno de|até)\s*$", RegexOptions.IgnoreCase);
            foreach (var f in findings)
            {
                string text = f.causa + "\n" + f.explicacao;
                foreach (Match fm in FRAME.Matches(text))
                {
                    m.quadrosCitados++;
                    if (!frames.Contains(int.Parse(fm.Groups[1].Value))) { m.quadrosFabricados++; m.detalhe += $"quadro inexistente: {fm.Value}; "; }
                }
                foreach (Match vm in VALUE.Matches(text))
                {
                    if (!TryValue(vm, out double v, out double tol, out string kind)) continue;
                    int from = Math.Max(0, vm.Index - 30);
                    if (approx.IsMatch(text.Substring(from, vm.Index - from))) tol = Math.Max(tol, Math.Abs(v) * 0.10);
                    m.valoresCitados++;
                    if (!numbers.Any(x => Matches(v, tol + (kind == "bytes" ? 0 : x.half), x.v, kind))) { m.valoresFabricados++; m.detalhe += $"valor ausente: {vm.Value.Trim()}; "; }
                }
            }
            m.fabricacaoValor = m.valoresFabricados > 0 || m.quadrosFabricados > 0;

            // ── 5. Teto ausente tratado como estourado ─────────────────────────
            foreach (var f in findings)
            {
                string text = f.causa + "\n" + f.explicacao;
                if (c.tetoDrawCalls <= 0 && ClaimsOverrun(text, DRAW_WORDS)) m.tetoAusenteViolado = true;
                if (c.tetoMemoriaBytes <= 0 && ClaimsOverrun(text, MEM_WORDS)) m.tetoAusenteViolado = true;
            }

            // ── 6. Vazamento de escopo ─────────────────────────────────────────
            bool hasDraw = findings.Any(f => f.tipo == "DrawCalls");
            bool hasTips = !string.IsNullOrWhiteSpace(dicas);
            m.dicasIndevidas = hasTips && !hasDraw;
            m.dicasAusentes  = !hasTips && hasDraw;
            m.batchingEmGpu  = findings.Any(f => f.tipo == "GPU" && PrescribesBatching(f.causa + "\n" + f.explicacao + "\n" + f.codigo));
            m.vazamentoEscopo = m.dicasIndevidas || m.batchingEmGpu;

            // ── 7. Indeterminação e falso estouro ──────────────────────────────
            int indet = findings.Count(IsIndeterminate);
            bool isG5 = c.esperadoIndeterminacao;
            bool sufficient = !c.esperadoSemEstouro;   // G1–G4
            m.indeterminacaoCorreta  = isG5 && findings.Count > 0 && indet == findings.Count;
            m.indeterminacaoIndevida = sufficient && indet > 0;
            m.falsoEstouro = c.esperadoSemEstouro && findings.Any(f => ClaimsOverrun(f.causa + "\n" + f.explicacao, null));

            // ── 8. Válida porém errada ([7] do artigo) ─────────────────────────
            m.validaPoremErrada = m.formatoValido && (m.contagemErrada || m.tipoErrado || m.fabricacaoSimbolo
                || m.fabricacaoValor || m.tetoAusenteViolado || m.vazamentoEscopo || m.indeterminacaoIndevida
                || m.falsoEstouro || (isG5 && !m.indeterminacaoCorreta));
            return m;
        }

        private static string Str(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out object v)) throw new FormatException($"campo ausente: {key}");
            if (!(v is string s)) throw new FormatException($"campo {key} não é string");
            return s;
        }

        // ── Tipo ────────────────────────────────────────────────────────────

        /// <summary>
        /// Multiset of finding types equals multiset of sample origins. "Geral" may stand in
        /// for any sample only where indeterminacy is a correct answer (G5 and G6).
        /// </summary>
        /// <remarks>
        /// MOTIVO do multiconjunto: o contrato não leva identificador de amostra no achado,
        /// então não há como casar achado com amostra um a um. O que dá para conferir sem
        /// suposição é a contagem por tipo.
        /// </remarks>
        private static bool TypesMatch(ArtigoCase c, List<Finding> findings)
        {
            var expected = c.amostras.GroupBy(a => TypeOfArray(a.array)).ToDictionary(g => g.Key, g => g.Count());
            var got = findings.GroupBy(f => f.tipo).ToDictionary(g => g.Key, g => g.Count());
            got.TryGetValue("Geral", out int geral);
            bool geralAllowed = c.esperadoSemEstouro;
            int missing = 0;
            foreach (var kv in expected)
            {
                got.TryGetValue(kv.Key, out int n);
                if (n > kv.Value) return false;
                missing += kv.Value - n;
            }
            foreach (var kv in got)
                if (kv.Key != "Geral" && !expected.ContainsKey(kv.Key)) return false;
            if (geralAllowed) return geral == missing;
            return missing == 0 && geral == 0;
        }

        // ── Símbolos ────────────────────────────────────────────────────────

        private static readonly Regex IDENT = new Regex(@"[A-Za-z_][A-Za-z0-9_]*");

        /// <summary>
        /// Identifiers the code may legitimately use: those in the culprit stacks of the samples a
        /// finding of this type draws on, and those in the attached project scripts.
        /// </summary>
        private static HashSet<string> StackSymbols(ArtigoCase c, string tipo)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            // MOTIVO: com o código anexado, reescrever um método do script usa os campos e
            // métodos DELE — isso é ancorado, não inventado.
            if (c.fontes != null)
                foreach (var f in c.fontes)
                    foreach (Match t in IDENT.Matches(f.code ?? "")) set.Add(t.Value);
            foreach (var a in c.amostras)
            {
                if (tipo != "Geral" && TypeOfArray(a.array) != tipo) continue;
                if (string.IsNullOrEmpty(a.culpritStack)) continue;
                foreach (Match t in IDENT.Matches(a.culpritStack)) set.Add(t.Value);
            }
            return set;
        }

        private static readonly HashSet<string> KEYWORDS = new HashSet<string>(StringComparer.Ordinal)
        {
            "if","else","for","foreach","while","do","switch","case","return","new","typeof","nameof","sizeof",
            "using","catch","lock","fixed","checked","unchecked","default","base","this","var","void","int",
            "float","double","bool","string","long","short","byte","char","object","decimal","uint","ulong",
            "public","private","protected","internal","static","readonly","const","class","struct","interface",
            "enum","namespace","override","virtual","abstract","sealed","partial","in","out","ref","params",
            "true","false","null","try","finally","throw","yield","await","async","get","set","value","is","as",
            "where","when","nameof","stackalloc","operator","implicit","explicit","event","delegate","break","continue","goto"
        };

        private static string StripCode(string code)
        {
            code = Regex.Replace(code, @"//[^\n]*", " ");
            code = Regex.Replace(code, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            code = Regex.Replace(code, @"@?""(?:\\.|[^""\\])*""", "\"\"");
            code = Regex.Replace(code, @"'(?:\\.|[^'\\])'", "' '");
            return code;
        }

        /// <summary>
        /// Classes and methods the code refers to: called methods, types after new/class/:,
        /// generic arguments, and the receiver of a static-looking access (Type.Member).
        /// </summary>
        public static HashSet<string> CitedSymbols(string code)
        {
            code = StripCode(code);
            // MOTIVO: namespace não é classe nem método. "using UnityEngine;" e
            // "namespace PerformanceAdvisor.StressTest" contavam como símbolos fora da pilha —
            // e como inventados, porque namespace não é tipo nem membro. Visto nas respostas
            // reais: UnityEngine (35) e PerformanceAdvisor (23) eram os mais "inventados".
            code = Regex.Replace(code, @"\busing\s+(?:static\s+)?[\w.]+\s*;", " ");
            code = Regex.Replace(code, @"\bnamespace\s+[\w.]+", " ");
            var set = new HashSet<string>(StringComparer.Ordinal);
            var ns = Namespaces;
            void Add(string s)
            {
                if (string.IsNullOrEmpty(s) || KEYWORDS.Contains(s)) return;
                if (ns.Contains(s)) return;   // UnityEngine.Debug.Log: o "UnityEngine" é namespace
                set.Add(s);
            }
            foreach (Match x in Regex.Matches(code, @"\b([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(")) Add(x.Groups[1].Value);
            foreach (Match x in Regex.Matches(code, @"\b(?:new|class|struct|interface|enum)\s+([A-Za-z_]\w*)")) Add(x.Groups[1].Value);
            foreach (Match x in Regex.Matches(code, @":\s*([A-Z]\w*)")) Add(x.Groups[1].Value);
            foreach (Match x in Regex.Matches(code, @"<\s*([A-Z]\w*)\s*>")) Add(x.Groups[1].Value);
            foreach (Match x in Regex.Matches(code, @"\b([A-Z]\w*)\s*\.\s*[A-Za-z_]")) Add(x.Groups[1].Value);
            return set;
        }

        /// <summary>Names the code itself declares (classes, methods, fields, locals).</summary>
        private static HashSet<string> DeclaredSymbols(string code)
        {
            code = StripCode(code);
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match x in Regex.Matches(code, @"\b(?:class|struct|interface|enum)\s+([A-Za-z_]\w*)")) set.Add(x.Groups[1].Value);
            // tipo nome( → declaração de método; tipo nome = ; , ) → campo, variável ou parâmetro.
            // MOTIVO do filtro: "return Foo(" tem a mesma forma de "void Foo(" — sem ele, toda
            // chamada depois de return/new/await viraria "declarada" e escaparia da contagem.
            foreach (Match x in Regex.Matches(code, @"\b([A-Za-z_][\w<>\[\]]*)\s+([A-Za-z_]\w*)\s*(?:\(|=|;|,|\))"))
            {
                if (NOT_A_TYPE.Contains(x.Groups[1].Value)) continue;
                set.Add(x.Groups[2].Value);
            }
            return set;
        }

        private static readonly HashSet<string> NOT_A_TYPE = new HashSet<string>(StringComparer.Ordinal)
        {
            "return","new","await","throw","else","yield","case","in","is","as","out","ref","using","goto",
            "typeof","nameof","sizeof","when","where","select","from","lock","foreach","if","while","switch"
        };

        private static HashSet<string> _api;
        private static HashSet<string> _namespaces;

        /// <summary>
        /// Namespace segments that are not also type names (UnityEngine, PerformanceAdvisor...).
        /// </summary>
        private static HashSet<string> Namespaces
        {
            get
            {
                if (_namespaces != null) return _namespaces;
                var types = new HashSet<string>(StringComparer.Ordinal);
                var segs = new HashSet<string>(StringComparer.Ordinal) { "PerformanceAdvisor", "StressTest", "UnityEngine", "UnityEditor", "System" };
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] ts;
                    try { ts = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { ts = e.Types.Where(t => t != null).ToArray(); }
                    catch { continue; }
                    foreach (Type t in ts)
                    {
                        string tn = t.Name; int tick = tn.IndexOf('`'); if (tick > 0) tn = tn.Substring(0, tick);
                        types.Add(tn);
                        if (!string.IsNullOrEmpty(t.Namespace)) foreach (string s in t.Namespace.Split('.')) segs.Add(s);
                    }
                }
                segs.ExceptWith(types.Where(t => t != "UnityEngine" && t != "System"));
                return _namespaces = segs;
            }
        }

        /// <summary>
        /// Every type and member name in the loaded Unity, .NET and project assemblies, plus
        /// the MonoBehaviour messages (which are not reflected members).
        /// </summary>
        /// <remarks>
        /// MOTIVO: separa "fora da pilha" de "inventado". Um GetComponent ou um Debug.Log
        /// existe — viola a regra R4 ao pé da letra, mas não é alucinação. Um
        /// EnemyManager.SpawnPool() que não existe em nenhum assembly é. O artigo reporta
        /// as duas medidas.
        /// </remarks>
        private static HashSet<string> KnownApi
        {
            get
            {
                if (_api != null) return _api;
                var set = new HashSet<string>(StringComparer.Ordinal)
                {
                    "Awake","Start","Update","LateUpdate","FixedUpdate","OnEnable","OnDisable","OnDestroy",
                    "OnGUI","OnValidate","Reset","OnApplicationQuit","OnBecameVisible","OnBecameInvisible",
                    "OnCollisionEnter","OnTriggerEnter","OnRenderObject","OnPreRender","OnPostRender"
                };
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string n = asm.GetName().Name;
                    if (!(n.StartsWith("UnityEngine") || n.StartsWith("Unity.") || n.StartsWith("System") ||
                          n == "mscorlib" || n == "netstandard" || n == "StressTest")) continue;
                    Type[] types;
                    try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                    foreach (Type t in types)
                    {
                        string tn = t.Name; int tick = tn.IndexOf('`'); if (tick > 0) tn = tn.Substring(0, tick);
                        set.Add(tn);
                        try
                        {
                            foreach (MemberInfo mi in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                                set.Add(mi.Name);
                        }
                        catch { /* tipo que não reflete: ignora */ }
                    }
                }
                return _api = set;
            }
        }

        // ── Valores ─────────────────────────────────────────────────────────

        private static readonly Regex FRAME = new Regex(@"\b(?:frame|quadro)s?\s*(?:n[º°o.]\s*)?#?\s*(\d+)", RegexOptions.IgnoreCase);

        private static readonly Regex VALUE = new Regex(
            @"(?<!\w)(\d{1,3}(?:[.,]\d{3})+|\d+(?:[.,]\d+)?)\s*(ms|milissegundos?|GB|MB|KB|kB|bytes?|draw ?calls?|chamadas de (?:desenho|renderiza\w*))\b",
            RegexOptions.IgnoreCase);

        /// <summary>Numbers of the payload, as sent, with half of their last decimal place.</summary>
        /// <remarks>
        /// MOTIVO da meia casa do payload: o orçamento vai como "16.7ms/frame" e o modelo
        /// escreve "16,67 ms" (1000 ÷ 60) — o mesmo número com mais precisão do que foi enviado.
        /// A tolerância soma a precisão do valor citado e a do valor enviado.
        /// </remarks>
        private static List<(double v, double half)> PayloadNumbers(string userText)
        {
            var l = new List<(double, double)>();
            foreach (Match x in Regex.Matches(userText ?? "", @"\d+(?:\.\d+)?"))
                if (double.TryParse(x.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                {
                    int dec = x.Value.Contains('.') ? x.Value.Length - x.Value.IndexOf('.') - 1 : 0;
                    l.Add((d, 0.5 * Math.Pow(10, -dec)));
                }
            return l;
        }

        /// <summary>Parses a cited value into payload units (ms, bytes or count) with its rounding tolerance.</summary>
        private static bool TryValue(Match vm, out double v, out double tol, out string kind)
        {
            string num = vm.Groups[1].Value; string unit = vm.Groups[2].Value.ToLowerInvariant();
            v = 0; tol = 0; kind = "";
            bool integerUnit = unit.StartsWith("byte") || unit.StartsWith("draw") || unit.StartsWith("chamada");
            // MOTIVO: "15.177 draw calls" é milhar em português; "2,10 ms" é decimal.
            if (Regex.IsMatch(num, @"^\d{1,3}([.,]\d{3})+$") && (integerUnit || num.Count(ch => ch == '.' || ch == ',') > 1))
                num = num.Replace(".", "").Replace(",", "");
            num = num.Replace(',', '.');
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double x)) return false;
            int dec = num.Contains('.') ? num.Length - num.IndexOf('.') - 1 : 0;
            double half = 0.5 * Math.Pow(10, -dec);

            if (unit.StartsWith("ms") || unit.StartsWith("mili")) { kind = "ms"; v = x; tol = half + 0.01; return true; }
            if (integerUnit && !unit.StartsWith("byte")) { kind = "count"; v = x; tol = Math.Max(half, x * 0.005); return true; }
            double mult = unit == "gb" ? 1024d * 1024 * 1024 : unit == "mb" ? 1024d * 1024 : unit == "kb" ? 1024d : 1d;
            kind = "bytes"; v = x * mult; tol = half * mult + v * 0.025; return true;
        }

        private static bool Matches(double v, double tol, double x, string kind)
        {
            if (Math.Abs(v - x) <= tol) return true;
            // MOTIVO: MB e KB podem ter sido convertidos na base 1000 em vez de 1024.
            if (kind == "bytes") return Math.Abs(v / 1.048576 - x) <= tol || Math.Abs(v / 1.024 - x) <= tol;
            return false;
        }

        // ── Leitura de frases ───────────────────────────────────────────────

        private static readonly Regex OVERRUN = new Regex(
            @"estour|exced|ultrapass|extrapol|acima d[oae]s? (?:or[cç]amento|teto|limite|budget|meta)|al[eé]m d[oae]s? (?:or[cç]amento|teto|limite)|viola[çcr]",
            RegexOptions.IgnoreCase);

        private static readonly Regex NEGATION = new Regex(
            @"\bn[ãa]o\b|\bnenhum|\bsem teto|\bsem limite|\bdentro d[oae]s? (?:or[cç]amento|teto|limite)|\bindefinid|\bnunca\b",
            RegexOptions.IgnoreCase);

        // MOTIVO: a frase precisa afirmar estouro DO recurso, não só citá-lo. A primeira versão
        // aceitava "memória" e um verbo de estouro em qualquer lugar da frase, e as 26 marcações
        // da condição amador eram todas do tipo "alocação de memória ... estourando o orçamento
        // de CPU" — o estouro afirmado era da CPU, que tem teto. Agora conta só quando a frase
        // fala do teto do recurso ("teto de memória") ou quando o recurso é o sujeito do verbo,
        // até três palavras antes dele ("o consumo de memória ultrapassa").
        private static readonly Regex DRAW_WORDS = new Regex(
            @"(?:teto|limite|or[cç]amento|budget|cota)\s+(?:de\s+|da\s+|do\s+|dos\s+|das\s+)?(?:draw ?calls?|chamadas de (?:desenho|renderiza))" +
            @"|(?:draw ?calls?|chamadas de (?:desenho|renderiza)\w*)\s+(?:\S+\s+){0,3}?(?:estour|exced|ultrapass|extrapol|viol)",
            RegexOptions.IgnoreCase);
        private static readonly Regex MEM_WORDS = new Regex(
            @"(?:teto|limite|or[cç]amento|budget|cota)\s+(?:de\s+|da\s+|do\s+)?(?:mem[oó]ria|RAM)" +
            @"|(?:mem[oó]ria|mem_bytes|\bRAM\b)\s+(?:\S+\s+){0,3}?(?:estour|exced|ultrapass|extrapol|viol)",
            RegexOptions.IgnoreCase);

        private static IEnumerable<string> Sentences(string text) =>
            Regex.Split(text ?? "", @"(?<=[.!?;])\s+|\n+").Where(s => s.Trim().Length > 0);

        /// <summary>True when some sentence asserts an overrun (about the given resource, if any) without negating it.</summary>
        public static bool ClaimsOverrun(string text, Regex resource)
        {
            foreach (string s in Sentences(text))
            {
                if (!OVERRUN.IsMatch(s)) continue;
                if (resource != null && !resource.IsMatch(s)) continue;
                if (NEGATION.IsMatch(s)) continue;
                return true;
            }
            return false;
        }

        private static readonly Regex BATCHING = new Regex(
            @"batch|instanc|SRP Batcher|combin\w* (?:as |de )?malhas|reduz\w*[^.]{0,40}(?:draw ?calls?|chamadas de (?:desenho|renderiza))",
            RegexOptions.IgnoreCase);

        private static bool PrescribesBatching(string text)
        {
            foreach (string s in Sentences(text))
                if (BATCHING.IsMatch(s) && !NEGATION.IsMatch(s)) return true;
            return false;
        }

        private static readonly Regex UNDETERMINED = new Regex(
            @"n[ãa]o (?:[ée]|foi|seria) poss[ií]vel determinar|indetermin|dados insuficientes|n[ãa]o h[áa] dados suficientes|n[ãa]o permite[mn]? determinar",
            RegexOptions.IgnoreCase);

        private static bool IsIndeterminate(Finding f) =>
            string.IsNullOrWhiteSpace(f.codigo) && (f.tipo == "Geral" || UNDETERMINED.IsMatch(f.causa ?? ""));
    }
}
