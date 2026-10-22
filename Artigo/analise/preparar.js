// Copia os dados de Artigo/ para as pastas de trabalho que os scripts de análise leem:
// a2x2/resp (as 600 respostas, nome achatado), a2x2/<célula>.csv (métricas do validador) e qual/casos.
// Rodar a partir de qualquer pasta: node Artigo/analise/preparar.js
const fs = require('fs'), path = require('path');
const AN = __dirname, ART = path.join(AN, '..');
const SEM = path.join(ART, 'arquivo', 'gemini-3.8-flash-sem-codigo');
const resp = path.join(AN, 'a2x2', 'resp');
fs.rmSync(resp, { recursive: true, force: true }); fs.mkdirSync(resp, { recursive: true });
// MOTIVO: o prefixo "arquivo__" marca a coleta sem código anexado; os scripts decidem a célula por ele.
for (const [base, prefix] of [[path.join(ART, 'respostas'), 'respostas'], [path.join(SEM, 'respostas'), 'arquivo__gemini-3.8-flash-sem-codigo__respostas']])
  for (const cond of ['amador', 'profissional'])
    for (const f of fs.readdirSync(path.join(base, cond)).filter(f => f.endsWith('.json')))
      fs.copyFileSync(path.join(base, cond, f), path.join(resp, `${prefix}__${cond}__${f}`));
for (const cond of ['amador', 'profissional']) {
  fs.copyFileSync(path.join(SEM, 'resultados', cond, 'metricas_por_resposta.csv'), path.join(AN, 'a2x2', `sem_${cond}.csv`));
  fs.copyFileSync(path.join(ART, 'resultados', cond, 'metricas_por_resposta.csv'), path.join(AN, 'a2x2', `com_${cond}.csv`));
}
const casos = path.join(AN, 'qual', 'casos');
fs.rmSync(casos, { recursive: true, force: true }); fs.mkdirSync(casos, { recursive: true });
for (const f of fs.readdirSync(path.join(ART, 'casos')).filter(f => /^G\d_.*\.json$/.test(f))) fs.copyFileSync(path.join(ART, 'casos', f), path.join(casos, f));
console.log(fs.readdirSync(resp).length, 'respostas;', fs.readdirSync(casos).length, 'casos');
