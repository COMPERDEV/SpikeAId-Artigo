// Análise 2×2 (prompt × código anexado) a partir de metricas_por_resposta.csv.
// Reproduz as regras do ArtigoAnalysis.cs: taxa por caso (entre respostas de formato válido,
// exceto a violação de formato), métricas por grupo, Wilcoxon pareado exato com postos médios,
// Holm sobre as métricas pré-registradas, r bisserial de postos.
const fs = require('fs');
const dir = __dirname;
const cells = { amador_sem: 'sem_amador.csv', profissional_sem: 'sem_profissional.csv', amador_com: 'com_amador.csv', profissional_com: 'com_profissional.csv' };

const ALL = g => true, G1to4 = g => !['G5_FALTANTE', 'G6_ENGANOSA'].includes(g), G5 = g => g === 'G5_FALTANTE',
      G5or6 = g => ['G5_FALTANTE', 'G6_ENGANOSA'].includes(g), G1or2 = g => ['G1_CPU', 'G2_GC'].includes(g);
const METRICS = [
  { k: 'formato_invalido', label: 'Format violation', col: r => r.formato_valido === '0', applies: ALL, valid: false },
  { k: 'contagem_errada', label: 'Wrong finding count', col: r => r.contagem_errada === '1', applies: ALL },
  { k: 'tipo_errado', label: 'Wrong type attribution', col: r => r.tipo_errado === '1', applies: ALL },
  { k: 'fabricacao_simbolo', label: 'Symbol fabrication (non-existent)', col: r => r.fabricacao_simbolo === '1', applies: ALL },
  { k: 'fabricacao_simbolo_estrita', label: 'Symbol outside the stack (strict R4)', col: r => r.fabricacao_simbolo_estrita === '1', applies: ALL },
  { k: 'fabricacao_valor', label: 'Value or frame fabrication', col: r => r.fabricacao_valor === '1', applies: ALL },
  { k: 'teto_ausente_violado', label: 'Missing ceiling treated as exceeded', col: r => r.teto_ausente_violado === '1', applies: ALL },
  { k: 'vazamento_escopo', label: 'Scope leakage', col: r => r.vazamento_escopo === '1', applies: ALL },
  { k: 'dicas_ausentes', label: 'Missing level-design tips with DrawCalls finding', col: r => r.dicas_ausentes === '1', applies: ALL },
  { k: 'indeterminacao_correta', label: 'Correct indeterminacy (G5)', col: r => r.indeterminacao_correta === '1', applies: G5, better: 'higher' },
  { k: 'indeterminacao_indevida', label: 'Undue indeterminacy (G1–G4)', col: r => r.indeterminacao_indevida === '1', applies: G1to4 },
  { k: 'falso_estouro', label: 'Overrun claimed without overrun (G5–G6)', col: r => r.falso_estouro === '1', applies: G5or6 },
  { k: 'valida_porem_errada', label: 'Valid but wrong', col: r => r.valida_porem_errada === '1', applies: ALL },
  { k: 'sem_codigo', label: 'Code abstention (G1–G2, post hoc)', col: r => r.sem_codigo === '1', applies: G1or2, posthoc: true },
];

function load(f) {
  const lines = fs.readFileSync(dir + '/' + f, 'utf8').replace(/^﻿/, '').split(/\r?\n/).filter(Boolean);
  const head = lines[0].split(';');
  return lines.slice(1).map(l => { const v = l.split(';'); const o = {}; head.forEach((h, i) => o[h] = v[i]); return o; });
}
// taxa[metric][case]
function rates(rows) {
  const out = {};
  const byCase = {};
  rows.filter(r => r.respondeu === '1').forEach(r => (byCase[r.caso] = byCase[r.caso] || []).push(r));
  for (const m of METRICS) {
    out[m.k] = {};
    for (const [c, rs] of Object.entries(byCase)) {
      if (!m.applies(rs[0].grupo)) continue;
      const use = m.valid === false ? rs : rs.filter(r => r.formato_valido === '1');
      if (!use.length) continue;
      out[m.k][c] = use.filter(m.col).length / use.length;
    }
  }
  return out;
}
function wilcoxon(diffs) {
  const d = diffs.filter(x => Math.abs(x) > 1e-12); const n = d.length;
  if (!n) return { n: 0, wp: 0, wm: 0, p: 1, r: 0 };
  const order = d.map((_, i) => i).sort((a, b) => Math.abs(d[a]) - Math.abs(d[b]));
  const rank2 = new Array(n);
  for (let i = 0; i < n;) { let j = i; while (j + 1 < n && Math.abs(Math.abs(d[order[j + 1]]) - Math.abs(d[order[i]])) < 1e-12) j++;
    const r2 = (i + 1) + (j + 1); for (let k = i; k <= j; k++) rank2[order[k]] = r2; i = j + 1; }
  const total2 = rank2.reduce((a, b) => a + b, 0); let w2 = 0; for (let i = 0; i < n; i++) if (d[i] > 0) w2 += rank2[i];
  const counts = new Array(total2 + 1).fill(0); counts[0] = 1;
  for (const r2 of rank2) for (let s = total2; s >= r2; s--) counts[s] += counts[s - r2];
  const all = Math.pow(2, n); let le = 0, ge = 0; for (let s = 0; s <= total2; s++) { if (s <= w2) le += counts[s]; if (s >= w2) ge += counts[s]; }
  const p = Math.min(1, 2 * Math.min(le, ge) / all), wp = w2 / 2, wm = total2 / 2 - wp;
  return { n, wp, wm, p, r: (wp - wm) / (wp + wm) };
}
function holm(ps) {
  const idx = ps.map((p, i) => i).filter(i => !isNaN(ps[i])).sort((a, b) => ps[a] - ps[b]);
  const adj = ps.map(() => NaN); let run = 0;
  idx.forEach((i, k) => { run = Math.max(run, Math.min(1, (idx.length - k) * ps[i])); adj[i] = run; });
  return adj;
}
const mean = o => { const v = Object.values(o); return v.length ? v.reduce((a, b) => a + b, 0) / v.length : NaN; };
const R = {}; for (const [k, f] of Object.entries(cells)) R[k] = rates(load(f));

// comparação pareada genérica: a − b por caso, nos casos presentes nos dois
function compare(a, b) {
  const rows = METRICS.map(m => {
    const ids = Object.keys(R[a][m.k]).filter(c => c in R[b][m.k]).sort();
    const w = wilcoxon(ids.map(c => R[a][m.k][c] - R[b][m.k][c]));
    return { m, cases: ids.length, ma: mean(R[a][m.k]), mb: mean(R[b][m.k]), ...w };
  });
  const adj = holm(rows.map(x => x.m.posthoc ? NaN : x.p));
  rows.forEach((x, i) => x.holm = adj[i]);
  return rows;
}
const pct = v => isNaN(v) ? '—' : (100 * v).toFixed(1) + '%';
const pv = v => isNaN(v) ? '—' : (v < 0.0001 ? '<0.0001' : v.toFixed(4));
function table(title, rows, la, lb) {
  let s = `\n### ${title}\n\n| Metric | Cases | ${la} | ${lb} | p exact | p Holm | r |\n|---|---|---|---|---|---|---|\n`;
  for (const x of rows) s += `| ${x.m.label} | ${x.cases} | ${pct(x.ma)} | ${pct(x.mb)} | ${pv(x.p)} | ${x.m.posthoc ? '—' : pv(x.holm)} | ${x.n ? x.r.toFixed(2) : '0.00'} |\n`;
  return s;
}
let md = '# Análise 2×2 — prompt × código anexado (gemini-3.8-flash, 30 casos × 5 repetições por célula)\n';
md += '\n## Taxas por célula\n\n| Metric | Amateur, no code | Professional, no code | Amateur, with code | Professional, with code |\n|---|---|---|---|---|\n';
for (const m of METRICS) md += `| ${m.label} | ${pct(mean(R.amador_sem[m.k]))} | ${pct(mean(R.profissional_sem[m.k]))} | ${pct(mean(R.amador_com[m.k]))} | ${pct(mean(R.profissional_com[m.k]))} |\n`;
md += '\n## Efeito do prompt (pré-registrado), dentro de cada condição de código\n';
md += table('Without attached code: amateur vs professional', compare('amador_sem', 'profissional_sem'), 'Amateur', 'Professional');
md += table('With attached code: amateur vs professional', compare('amador_com', 'profissional_com'), 'Amateur', 'Professional');
md += '\n## Efeito de anexar o código (exploratório, pós-hoc), dentro de cada prompt\n';
md += table('Amateur prompt: no code vs with code', compare('amador_sem', 'amador_com'), 'No code', 'With code');
md += table('Professional prompt: no code vs with code', compare('profissional_sem', 'profissional_com'), 'No code', 'With code');
// grupo × célula para as métricas centrais
const groups = ['G1_CPU', 'G2_GC', 'G3_DRAWCALLS', 'G4_GPU', 'G5_FALTANTE', 'G6_ENGANOSA'];
const gm = (cell, mk, g) => { const ids = Object.keys(R[cell][mk]).filter(c => c.startsWith(g.split('_')[0] + '_')); return ids.length ? ids.reduce((a, c) => a + R[cell][mk][c], 0) / ids.length : NaN; };
md += '\n## Válida porém errada, por grupo\n\n| Group | A no code | P no code | A with code | P with code |\n|---|---|---|---|---|\n';
for (const g of groups) md += `| ${g} | ${['amador_sem', 'profissional_sem', 'amador_com', 'profissional_com'].map(c => pct(gm(c, 'valida_porem_errada', g))).join(' | ')} |\n`;
fs.writeFileSync(dir + '/analise_2x2.md', md);
// CSV de taxas por caso, para o repositório
let csv = 'metrica;caso;amador_sem;profissional_sem;amador_com;profissional_com\n';
for (const m of METRICS) { const ids = [...new Set(Object.keys(cells).flatMap(c => Object.keys(R[c][m.k])))].sort();
  for (const id of ids) csv += [m.k, id, ...Object.keys(cells).map(c => id in R[c][m.k] ? R[c][m.k][id].toFixed(4) : '')].join(';') + '\n'; }
fs.writeFileSync(dir + '/taxas_por_caso_2x2.csv', csv);
console.log(md);
