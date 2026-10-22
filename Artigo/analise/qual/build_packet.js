// Pacote cego da rubrica para avaliadores de IA: mesmos 40 laudos e mesma numeração do
// Revisao_resultados.html (semente 20261022), sem a condição. A chave fica num arquivo à parte.
const fs = require('fs'), path = require('path');
const Q = __dirname, RESP = path.join(Q, '..', 'a2x2', 'resp');
const outDir = process.argv[2];
const CELLS = ['sem_amador', 'sem_profissional', 'com_amador', 'com_profissional'];
const casos = {};
for (const f of fs.readdirSync(path.join(Q, 'casos'))) { const c = JSON.parse(fs.readFileSync(path.join(Q, 'casos', f), 'utf8')); casos[c.id] = c; }
const resp = {};
for (const f of fs.readdirSync(RESP)) { const j = JSON.parse(fs.readFileSync(path.join(RESP, f), 'utf8'));
  resp[`${(f.startsWith('arquivo') ? 'sem' : 'com')}_${j.condicao}__${j.caso}__r${j.repeticao}`] = JSON.parse(j.texto); }
// MOTIVO: mesma ordem e mesmo embaralhamento do visualizador, para as notas se juntarem por número.
const sample = [];
for (const id of Object.keys(casos).sort()) if (/^G[12]_/.test(id)) for (const k of CELLS) sample.push({ key: `${k}__${id}__r1`, caso: id, cell: k });
let s = 20261022; const rnd = () => { s |= 0; s = s + 0x6D2B79F5 | 0; let t = Math.imul(s ^ s >>> 15, 1 | s); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; };
for (let i = sample.length - 1; i > 0; i--) { const j = Math.floor(rnd() * (i + 1)); [sample[i], sample[j]] = [sample[j], sample[i]]; }
sample.forEach((x, i) => x.n = i + 1);
const original = casos['G1_CPU_01'].fontes[0].code;
const BS_N = String.fromCharCode(92) + 'n';
let md = `# Avaliação cega de laudos de desempenho — rubrica para avaliador de IA

Você é um avaliador independente num estudo sobre laudos de diagnóstico de desempenho gerados por um
modelo de linguagem para projetos Unity. Avalie os **40 laudos** abaixo com a rubrica. Não há
informação sobre como cada laudo foi produzido; não tente adivinhar, e julgue cada um só pelo que
está nele e pela telemetria que o acompanha.

## Contexto

Um plugin de Editor da Unity mede o jogo, captura os quadros que excederam o orçamento de hardware e
envia ao modelo a telemetria (o "turno do usuário"). Em parte dos laudos o modelo recebeu também o
código-fonte do script citado na pilha de chamadas; em outros, não. O laudo deve ter um achado por
amostra, com a causa raiz, o código C# refatorado (ou vazio, se não houver como corrigir sem inventar)
e uma explicação para um desenvolvedor **iniciante**.

O script real do projeto (StressTestScene.cs) é o abaixo — é o mesmo em todos os casos. É um script
de teste que cria gargalos de propósito; julgue as correções como se ele fosse código de jogo:
o comportamento a preservar é o que o script faz e expõe (campos do Inspector, métodos), e a
correção ideal reduz o custo apontado pela telemetria mudando só o necessário. Atenção: os valores
dos campos do Inspector de cada cena **não** vão na telemetria — o caso informa, em
"Cena do caso", quais opções estavam ligadas, mas o modelo que escreveu o laudo não sabia disso.

\`\`\`csharp
${original}
\`\`\`

## Rubrica (notas inteiras de 1 a 5; 2 e 4 são intermediárias)

| Critério | 1 | 3 | 5 |
|---|---|---|---|
| **H1 Pertinência da correção** (só se o laudo traz código) | corrige outra coisa, ou nada | ataca a causa, mas incompleta ou com efeito colateral | ataca exatamente a causa que a telemetria aponta e reduziria o custo medido |
| **H2 Preservação do script** (só se o laudo traz código) | reescreve o script e perde funções ou campos | mantém o essencial, com mudanças não pedidas | muda só o necessário |
| **H3 Clareza didática** | jargão sem explicação, confuso ou errado | compreensível com esforço | um iniciante entende o porquê e o que fazer |
| **H4 Utilidade para o iniciante (global)** | levaria a erro: prescrição errada ou inventada | neutro: não ajuda nem atrapalha | o iniciante resolveria o problema com este laudo |

- Se o laudo **não traz código** em nenhum achado, deixe H1 e H2 vazios.
- Se o laudo tem vários achados, avalie o laudo inteiro (uma nota por critério).
- Um laudo que se abstém do código com motivo correto não é penalizado em H3; em H4, julgue se
  a abstenção ajuda mais ou menos do que uma correção ajudaria.
- Código entregue numa linha só, com \`\\n\` literal no lugar das quebras de linha, é como o
  usuário o receberia: considere isso em H1/H4 (ele não compila colado assim).

## Formato da resposta (obrigatório)

Responda **apenas** com um CSV com separador ponto e vírgula, cabeçalho e exatamente 40 linhas,
nesta ordem de colunas (comentário curto, sem ponto e vírgula):

\`\`\`
laudo;H1;H2;H3;H4;comentario
1;4;5;4;4;corrige a varredura e mantém os campos
2;;;3;2;sem código, diagnóstico não sustentado
\`\`\`

Salve como \`notas_<nome-do-avaliador>.csv\` (ex.: \`notas_codex.csv\`) e informe o modelo usado.

---
`;
for (const x of sample) {
  const c = casos[x.caso], o = resp[x.key];
  md += `\n## Laudo ${x.n}\n\n**Caso:** ${x.caso} · **Cena do caso:** ${c.parametros}${c.origem !== 'captura' ? ' (' + c.origem + ')' : ''}\n\n`;
  md += `**Telemetria enviada** (sem o código anexado, quando houve):\n\n\`\`\`\n${c.userText.replace(/\s*script_sources:[\s\S]*$/, '')}\n\`\`\`\n\n`;
  o.findings.forEach((f, i) => {
    md += `### Laudo ${x.n} · achado ${i + 1} · tipo ${f.tipo}\n\n**Causa raiz:** ${f.causa_raiz}\n\n**Código refatorado:**${f.codigo_refatorado.trim() ? '' : ' (vazio)'}\n\n`;
    if (f.codigo_refatorado.trim()) { const single = f.codigo_refatorado.includes(BS_N) && f.codigo_refatorado.split('\n').length < 4;
      md += (single ? '_(entregue numa linha só, como abaixo)_\n\n' : '') + '```csharp\n' + f.codigo_refatorado + '\n```\n\n'; }
    md += `**Explicação didática:** ${f.explicacao_didatica}\n\n`;
  });
  md += `**Dicas de level design:** ${o.dicas_level_design == null ? 'null' : o.dicas_level_design}\n\n---\n`;
}
fs.mkdirSync(outDir, { recursive: true });
fs.writeFileSync(path.join(outDir, 'rubrica_cega_para_IA.md'), md);
// chave (não vai para os avaliadores)
fs.writeFileSync(path.join(Q, 'chave_rubrica.csv'), 'laudo;caso;celula\n' + sample.map(x => `${x.n};${x.caso};${x.cell}`).join('\n') + '\n');
console.log('ok', (md.length / 1024).toFixed(0), 'KB');
