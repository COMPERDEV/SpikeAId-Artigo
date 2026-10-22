// Critérios objetivos de qualidade do código refatorado (G1_CPU e G2_GC), por célula do 2×2.
// Taxa por caso entre os achados com código; comparação pareada por caso (Wilcoxon exato, Holm).
const fs = require('fs'), path = require('path');
const Q = __dirname;
const items = JSON.parse(fs.readFileSync(path.join(Q, 'items.json'), 'utf8'));
const comp = {};
for (const l of fs.readFileSync(path.join(Q, 'compile.tsv'), 'utf8').split(/\r?\n/).filter(Boolean)) {
  const [id, rc, nerr, err] = l.split('\t'); comp[id] = { ok: rc === '0', nerr: +nerr, err: err || '' };
}
// MOTIVO: o laudo lê o JSON uma vez (JsonUtility); código cujas quebras de linha vieram escapadas
// duas vezes chega ao usuário numa linha só, com "\n" literal — e uma linha com // comenta o resto.
const BS_N = String.fromCharCode(92) + 'n';
for (const it of items) if (it.hasCode) { const c = comp[it.id]; it.compiles = c ? c.ok : null; it.compileError = c ? c.err : ''; it.applicable = it.compiles && it.defectRemoved && it.fieldsPreserved !== false;
  it.escaped = it.code.includes(BS_N) && it.code.split('\n').length < 4; }
fs.writeFileSync(path.join(Q, 'items_avaliados.json'), JSON.stringify(items, null, 1));

const CELLS = ['sem_amador', 'sem_profissional', 'com_amador', 'com_profissional'];
const CRIT = [
  { k: 'hasCode', label: 'Achado traz código', over: 'all', desc: true },
  { k: 'escaped', label: 'Código numa linha só, com quebras escapadas', over: 'code', desc: true },
  { k: 'compiles', label: 'Compila aplicado ao projeto', over: 'code' },
  { k: 'defectRemoved', label: 'Tira o defeito plantado do caminho por quadro', over: 'code' },
  { k: 'fieldsPreserved', label: 'Preserva os campos públicos do script', over: 'rewrite' },
  { k: 'applicable', label: 'Correção aplicável (as três)', over: 'code' },
  { k: 'applicable', label: 'Achados com correção aplicável (sobre todos os achados)', over: 'all', desc: true },
];
function rate(cell, cr) {
  const out = {};
  const byCase = {};
  for (const it of items.filter(i => i.cell === cell)) (byCase[it.caso] = byCase[it.caso] || []).push(it);
  for (const [c, its] of Object.entries(byCase)) {
    let use = its;
    if (cr.over === 'code') use = its.filter(i => i.hasCode);
    if (cr.over === 'rewrite') use = its.filter(i => i.hasCode && i.form !== 'outra_classe');
    if (!use.length) continue;
    out[c] = use.filter(i => i[cr.k] === true).length / use.length;
  }
  return out;
}
function wilcoxon(diffs) {
  const d = diffs.filter(x => Math.abs(x) > 1e-12); const n = d.length;
  if (!n) return { n: 0, p: 1, r: 0 };
  const order = d.map((_, i) => i).sort((a, b) => Math.abs(d[a]) - Math.abs(d[b]));
  const rank2 = new Array(n);
  for (let i = 0; i < n;) { let j = i; while (j + 1 < n && Math.abs(Math.abs(d[order[j + 1]]) - Math.abs(d[order[i]])) < 1e-12) j++;
    const r2 = (i + 1) + (j + 1); for (let k = i; k <= j; k++) rank2[order[k]] = r2; i = j + 1; }
  const total2 = rank2.reduce((a, b) => a + b, 0); let w2 = 0; for (let i = 0; i < n; i++) if (d[i] > 0) w2 += rank2[i];
  const counts = new Array(total2 + 1).fill(0); counts[0] = 1;
  for (const r2 of rank2) for (let s = total2; s >= r2; s--) counts[s] += counts[s - r2];
  const all = Math.pow(2, n); let le = 0, ge = 0; for (let s = 0; s <= total2; s++) { if (s <= w2) le += counts[s]; if (s >= w2) ge += counts[s]; }
  const p = Math.min(1, 2 * Math.min(le, ge) / all), wp = w2 / 2, wm = total2 / 2 - wp;
  return { n, p, r: (wp - wm) / (wp + wm) };
}
function holm(ps) {
  const idx = ps.map((p, i) => i).filter(i => !isNaN(ps[i])).sort((a, b) => ps[a] - ps[b]);
  const adj = ps.map(() => NaN); let run = 0;
  idx.forEach((i, k) => { run = Math.max(run, Math.min(1, (idx.length - k) * ps[i])); adj[i] = run; });
  return adj;
}
const mean = o => { const v = Object.values(o); return v.length ? v.reduce((a, b) => a + b, 0) / v.length : NaN; };
const pct = v => isNaN(v) ? '—' : (100 * v).toFixed(1) + '%';
const pv = v => isNaN(v) ? '—' : (v < 0.0001 ? '<0,0001' : v.toFixed(4).replace('.', ','));
const R = {}; for (const c of CELLS) R[c] = CRIT.map(cr => rate(c, cr));

let md = '# Critérios objetivos de qualidade do código — G1_CPU e G2_GC (10 casos × 5 repetições por célula)\n\n';
const n = c => items.filter(i => i.cell === c).length, nc = c => items.filter(i => i.cell === c && i.hasCode).length;
md += '| Critério | A sem código | P sem código | A com código | P com código |\n|---|---|---|---|---|\n';
md += `| Achados (com código) | ${CELLS.map(c => `${n(c)} (${nc(c)})`).join(' | ')} |\n`;
CRIT.forEach((cr, k) => { md += `| ${cr.label} | ${CELLS.map(c => pct(mean(R[c][k]))).join(' | ')} |\n`; });
function compare(a, b, la, lb) {
  const rows = CRIT.map((cr, k) => { const ids = Object.keys(R[a][k]).filter(c => c in R[b][k]).sort();
    return { cr, cases: ids.length, ma: mean(R[a][k]), mb: mean(R[b][k]), ...wilcoxon(ids.map(c => R[a][k][c] - R[b][k][c])) }; });
  // família do Holm: só os quatro critérios testados (compila, defeito, campos, aplicável)
  const adj = holm(rows.map(x => x.cases && !x.cr.desc ? x.p : NaN)); rows.forEach((x, i) => x.holm = adj[i]);
  let s = `\n### ${la} × ${lb}\n\n| Critério | Casos | ${la} | ${lb} | p exato | p Holm | r |\n|---|---|---|---|---|---|---|\n`;
  for (const x of rows) s += `| ${x.cr.label} | ${x.cases} | ${pct(x.ma)} | ${pct(x.mb)} | ${x.cases ? pv(x.p) : '—'} | ${x.cr.desc ? '—' : pv(x.holm)} | ${x.n ? x.r.toFixed(2).replace('.', ',') : '0,00'} |\n`;
  return s;
}
md += compare('com_amador', 'com_profissional', 'A com código', 'P com código');
md += compare('sem_amador', 'com_amador', 'A sem código', 'A com código');
// formas e erros de compilação mais comuns
md += '\n## Forma do código entregue\n\n| Célula | ' + ['classe_original', 'metodo', 'trecho', 'outra_classe'].join(' | ') + ' |\n|---|---|---|---|---|\n';
for (const c of CELLS) md += `| ${c} | ${['classe_original', 'metodo', 'trecho', 'outra_classe'].map(f => items.filter(i => i.cell === c && i.form === f).length).join(' | ')} |\n`;
md += '\n## Erros de compilação (primeiro erro de cada trecho)\n\n';
const errs = {}; for (const it of items.filter(i => i.hasCode && !i.compiles)) { const code = (/error (CS\d+)/.exec(it.compileError) || [])[1] || '?'; const k = it.cell + ' ' + code; errs[k] = (errs[k] || 0) + 1; }
for (const [k, v] of Object.entries(errs).sort()) md += `- ${k}: ${v}\n`;
md += '\n## Defeitos que ficaram no caminho por quadro\n\n';
const left = {}; for (const it of items.filter(i => i.hasCode && !i.defectRemoved)) { const k = it.cell + ' ' + it.defectsLeft.join('+'); left[k] = (left[k] || 0) + 1; }
for (const [k, v] of Object.entries(left).sort()) md += `- ${k}: ${v}\n`;
md += '\n## Campos públicos perdidos (quando o código reescreve StressTestScene)\n\n';
const lost = {}; for (const it of items.filter(i => i.fieldsLost && i.fieldsLost.length)) for (const f of it.fieldsLost) { const k = it.cell + ' ' + f; lost[k] = (lost[k] || 0) + 1; }
for (const [k, v] of Object.entries(lost).sort()) md += `- ${k}: ${v}\n`;
fs.writeFileSync(path.join(Q, 'qualidade_codigo.md'), md);
console.log(md);
