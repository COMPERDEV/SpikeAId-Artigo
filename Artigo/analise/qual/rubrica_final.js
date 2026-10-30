// Tabela 4 final: mediana dos seis avaliadores de IA independentes, com a adjudicação humana do
// autor substituindo a mediana nos 8 laudos mais divergentes ("não sei" mantém a mediana).
const fs = require('fs'), path = require('path');
const Q = __dirname, ADJ = process.argv[2];
const M = JSON.parse(fs.readFileSync(path.join(Q, 'rubrica_medianas.json'), 'utf8'));
const key = {}; for (const l of fs.readFileSync(path.join(Q, 'chave_rubrica.csv'), 'utf8').split(/\r?\n/).slice(1).filter(Boolean)) { const [n, caso, cell] = l.split(';'); key[+n] = { caso, cell }; }
const C = ['H1', 'H2', 'H3', 'H4'], CELLS = ['sem_amador', 'sem_profissional', 'com_amador', 'com_profissional'];
const adj = {}; for (const l of fs.readFileSync(ADJ, 'utf8').replace(/^﻿/, '').split(/\r?\n/).slice(1).filter(Boolean)) { const v = l.split(';'); adj[+v[0]] = { H1: v[1], H2: v[2], H3: v[3], H4: v[4] }; }
const F = JSON.parse(JSON.stringify(M)); const diffs = { H1: [], H2: [], H3: [], H4: [] };
for (const [n, a] of Object.entries(adj)) for (const c of C) { const v = a[c]; if (v && /^[1-5]$/.test(v)) { if (M[n][c] != null) diffs[c].push(+v - M[n][c]); F[n][c] = +v; } }
const fmt = v => v == null || isNaN(v) ? '—' : v.toFixed(1).replace('.', ',');
const mean = a => a.reduce((s, x) => s + x, 0) / a.length;
let md = '# Tabela 4 final — mediana de seis IAs com adjudicação humana em 8 laudos\n\n| Critério | Ref. sem código | Estr. sem código | Ref. com código | Estr. com código |\n|---|---|---|---|---|\n';
for (const c of C) md += `| ${c} | ${CELLS.map(cell => { const v = Object.keys(F).filter(n => key[n].cell === cell).map(n => F[n][c]).filter(x => x != null); return v.length ? fmt(mean(v)) : 'n/a'; }).join(' | ')} |\n`;
md += '\n## Adjudicação: nota do autor menos a mediana das seis IAs, nos 8 laudos\n\n| Critério | n | diferença média | |diferença| média |\n|---|---|---|---|\n';
for (const c of C) if (diffs[c].length) md += `| ${c} | ${diffs[c].length} | ${fmt(mean(diffs[c]))} | ${fmt(mean(diffs[c].map(Math.abs)))} |\n`;
function wilcoxon(d0) { const d = d0.filter(x => Math.abs(x) > 1e-12), n = d.length; if (!n) return 1;
  const o = d.map((_, i) => i).sort((a, b) => Math.abs(d[a]) - Math.abs(d[b])), r2 = Array(n);
  for (let i = 0; i < n;) { let j = i; while (j + 1 < n && Math.abs(Math.abs(d[o[j + 1]]) - Math.abs(d[o[i]])) < 1e-12) j++; for (let k = i; k <= j; k++) r2[o[k]] = i + j + 2; i = j + 1; }
  const T = r2.reduce((a, b) => a + b, 0); let w = 0; d.forEach((x, i) => { if (x > 0) w += r2[i]; });
  const cnt = Array(T + 1).fill(0); cnt[0] = 1; for (const r of r2) for (let s = T; s >= r; s--) cnt[s] += cnt[s - r];
  let le = 0, ge = 0; for (let s = 0; s <= T; s++) { if (s <= w) le += cnt[s]; if (s >= w) ge += cnt[s]; }
  return Math.min(1, 2 * Math.min(le, ge) / 2 ** n); }
const byCase = (cell, c) => { const o = {}; for (const n of Object.keys(F)) if (key[n].cell === cell) o[key[n].caso] = F[n][c]; return o; };
md += '\n## Comparações pareadas por caso (Wilcoxon exato, sem ajuste)\n\n| Comparação | H1 | H2 | H3 | H4 |\n|---|---|---|---|---|\n';
for (const [a, b] of [['com_amador', 'com_profissional'], ['sem_amador', 'sem_profissional'], ['sem_amador', 'com_amador'], ['sem_profissional', 'com_profissional']])
  md += `| ${a} × ${b} | ${C.map(c => { const A = byCase(a, c), B = byCase(b, c); const ids = Object.keys(A).filter(k => A[k] != null && B[k] != null); if (ids.length < 3) return 'n/a';
    return `${fmt(mean(ids.map(k => A[k])))} × ${fmt(mean(ids.map(k => B[k])))} (p=${wilcoxon(ids.map(k => A[k] - B[k])).toFixed(3).replace('.', ',')})`; }).join(' | ')} |\n`;
fs.writeFileSync(path.join(Q, 'rubrica_final.md'), md); console.log(md);
