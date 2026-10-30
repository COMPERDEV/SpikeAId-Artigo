// Rubrica H1–H4: junta as notas dos avaliadores de IA com a chave (laudo → caso, célula),
// calcula a mediana por laudo entre os três avaliadores independentes do desenho (GPT),
// resume por célula, mede concordância (kappa ponderado quadrático) e compara células por caso.
const fs = require('fs'), path = require('path');
const Q = __dirname, Z = process.argv[2], CL = process.argv[3];
const parse = f => { const L = fs.readFileSync(f, 'utf8').replace(/^﻿/, '').split(/\r?\n/).filter(Boolean);
  return L.slice(1).map(l => { const v = l.split(';').map(s => s.replace(/^"|"$/g, '')); return { n: +v[0], H1: v[1] ? +v[1] : null, H2: v[2] ? +v[2] : null, H3: v[3] ? +v[3] : null, H4: v[4] ? +v[4] : null }; }); };
const key = {}; for (const l of fs.readFileSync(path.join(Q, 'chave_rubrica.csv'), 'utf8').split(/\r?\n/).slice(1).filter(Boolean)) { const [n, caso, cell] = l.split(';'); key[+n] = { caso, cell }; }
// CL = pasta da rubrica: notas do Claude que conhece o desenho e dos três Claude cegos
const R = { astra: parse(path.join(Z, 'notas_astra_medium.csv')), sol: parse(path.join(Z, 'notas_sol_medium.csv')), luna: parse(path.join(Z, 'notas_luna_high.csv')),
  opus: parse(path.join(CL, 'notas_claude-opus-5.5_cego.csv')), sonnet: parse(path.join(CL, 'notas_claude-sonnet-5.5_cego.csv')), haiku: parse(path.join(CL, 'notas_claude-haiku-5.5_cego.csv')),
  claude: parse(path.join(CL, 'notas_claude-opus-5.5.csv')) };
for (const [k, v] of Object.entries(R)) if (v.length !== 40) throw new Error(k + ' tem ' + v.length + ' linhas');
// MOTIVO: "claude" (a sessão que montou o experimento) fica fora da mediana — não é independente do desenho.
const C = ['H1', 'H2', 'H3', 'H4'], GPT = ['astra', 'sol', 'luna', 'opus', 'sonnet', 'haiku'];
const med = a => { const s = a.filter(x => x != null).sort((x, y) => x - y); if (!s.length) return null; const m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };
const get = (r, n, c) => R[r].find(x => x.n === n)[c];
// mediana GPT por laudo
const M = {}; for (let n = 1; n <= 40; n++) { M[n] = {}; for (const c of C) M[n][c] = med(GPT.map(r => get(r, n, c))); }
const CELLS = ['sem_amador', 'sem_profissional', 'com_amador', 'com_profissional'];
const fmt = v => v == null || isNaN(v) ? '—' : v.toFixed(1).replace('.', ',');
let md = '# Rubrica H1–H4 — 40 laudos (G1/G2, repetição 1)\n\nMediana dos seis avaliadores independentes (3 GPT, 3 Claude cegos) por laudo; por célula, média e mediana dessas medianas (10 laudos).\n\n| Critério | Ref. sem código | Estr. sem código | Ref. com código | Estr. com código |\n|---|---|---|---|---|\n';
for (const c of C) md += `| ${c} | ${CELLS.map(cell => { const v = Object.keys(M).filter(n => key[n].cell === cell).map(n => M[n][c]).filter(x => x != null); return v.length ? `${fmt(v.reduce((a, b) => a + b, 0) / v.length)} (md ${fmt(med(v))}, n=${v.length})` : 'n/a'; }).join(' | ')} |\n`;
md += '\n## Mesmo resumo com as notas do Claude (não independente do desenho)\n\n| Critério | Ref. sem código | Estr. sem código | Ref. com código | Estr. com código |\n|---|---|---|---|---|\n';
for (const c of C) md += `| ${c} | ${CELLS.map(cell => { const v = Object.keys(M).filter(n => key[n].cell === cell).map(n => get('claude', +n, c)).filter(x => x != null); return v.length ? fmt(v.reduce((a, b) => a + b, 0) / v.length) : 'n/a'; }).join(' | ')} |\n`;
// kappa ponderado quadrático entre pares (escala 1..5)
function qwk(a, b) { const p = a.map((x, i) => [x, b[i]]).filter(([x, y]) => x != null && y != null); const N = p.length, K = 5;
  const O = Array.from({ length: K }, () => Array(K).fill(0)); p.forEach(([x, y]) => O[x - 1][y - 1]++);
  const ra = O.map(r => r.reduce((s, v) => s + v, 0)), cb = O[0].map((_, j) => O.reduce((s, r) => s + r[j], 0));
  let num = 0, den = 0; for (let i = 0; i < K; i++) for (let j = 0; j < K; j++) { const w = (i - j) ** 2 / (K - 1) ** 2; num += w * O[i][j]; den += w * ra[i] * cb[j] / N; }
  return { k: den ? 1 - num / den : NaN, N }; }
const ALL = [...GPT, 'claude'];
const kp = (a, b, c) => qwk([...Array(40)].map((_, n) => get(a, n + 1, c)), [...Array(40)].map((_, n) => get(b, n + 1, c))).k;
md += '\n## Concordância entre pares (kappa ponderado quadrático)\n\n| Par | H1 | H2 | H3 | H4 |\n|---|---|---|---|---|\n';
for (let i = 0; i < ALL.length; i++) for (let j = i + 1; j < ALL.length; j++)
  md += `| ${ALL[i]} × ${ALL[j]} | ${C.map(c => kp(ALL[i], ALL[j], c).toFixed(2).replace('.', ',')).join(' | ')} |\n`;
// resumo: média do kappa dentro de cada fornecedor e entre fornecedores (só os seis independentes)
const OAI = ['astra', 'sol', 'luna'], ANT = ['opus', 'sonnet', 'haiku'];
const pairs = (A, B) => { const o = []; for (const a of A) for (const b of B) if (a !== b && (A !== B || a < b)) o.push([a, b]); return o; };
const meanK = (P, c) => P.reduce((s, [a, b]) => s + kp(a, b, c), 0) / P.length;
md += '\n## Kappa médio por grupo de pares (seis avaliadores independentes)\n\n| Grupo | H1 | H2 | H3 | H4 |\n|---|---|---|---|---|\n';
for (const [nome, P] of [['dentro da OpenAI', pairs(OAI, OAI)], ['dentro da Anthropic', pairs(ANT, ANT)], ['OpenAI × Anthropic', pairs(OAI, ANT)]])
  md += `| ${nome} | ${C.map(c => meanK(P, c).toFixed(2).replace('.', ',')).join(' | ')} |\n`;
// laudos com amplitude ≥ 2 entre os seis
md += '\n## Laudos com amplitude de 2 ou mais entre os seis\n\n';
const div = []; for (let n = 1; n <= 40; n++) { const cs = C.filter(c => { const v = GPT.map(r => get(r, n, c)).filter(x => x != null); return v.length && Math.max(...v) - Math.min(...v) >= 2; }); if (cs.length) div.push(`${n} (${cs.join(', ')})`); }
md += div.join('; ') + '\n';
fs.writeFileSync(path.join(Q, 'rubrica_medianas.json'), JSON.stringify(M));
// comparação pareada por caso (Wilcoxon exato) entre células, na mediana GPT
function wilcoxon(d0) { const d = d0.filter(x => Math.abs(x) > 1e-12), n = d.length; if (!n) return { p: 1, n: 0 };
  const o = d.map((_, i) => i).sort((a, b) => Math.abs(d[a]) - Math.abs(d[b])), r2 = Array(n);
  for (let i = 0; i < n;) { let j = i; while (j + 1 < n && Math.abs(Math.abs(d[o[j + 1]]) - Math.abs(d[o[i]])) < 1e-12) j++; for (let k = i; k <= j; k++) r2[o[k]] = i + j + 2; i = j + 1; }
  const T = r2.reduce((a, b) => a + b, 0); let w = 0; d.forEach((x, i) => { if (x > 0) w += r2[i]; });
  const cnt = Array(T + 1).fill(0); cnt[0] = 1; for (const r of r2) for (let s = T; s >= r; s--) cnt[s] += cnt[s - r];
  let le = 0, ge = 0; for (let s = 0; s <= T; s++) { if (s <= w) le += cnt[s]; if (s >= w) ge += cnt[s]; }
  return { p: Math.min(1, 2 * Math.min(le, ge) / 2 ** n), n }; }
const byCase = (cell, c) => { const o = {}; for (const n of Object.keys(M)) if (key[n].cell === cell) o[key[n].caso] = M[n][c]; return o; };
md += '\n## Comparações pareadas por caso (mediana dos seis; Wilcoxon exato, sem ajuste)\n\n| Comparação | H1 | H2 | H3 | H4 |\n|---|---|---|---|---|\n';
for (const [a, b] of [['com_amador', 'com_profissional'], ['sem_amador', 'sem_profissional'], ['sem_amador', 'com_amador'], ['sem_profissional', 'com_profissional']]) {
  md += `| ${a} × ${b} | ${C.map(c => { const A = byCase(a, c), B = byCase(b, c); const ids = Object.keys(A).filter(k => A[k] != null && B[k] != null); if (ids.length < 3) return 'n/a'; const w = wilcoxon(ids.map(k => A[k] - B[k])); const ma = ids.reduce((s, k) => s + A[k], 0) / ids.length, mb = ids.reduce((s, k) => s + B[k], 0) / ids.length; return `${fmt(ma)} × ${fmt(mb)} (p=${w.p.toFixed(3).replace('.', ',')})`; }).join(' | ')} |\n`; }
fs.writeFileSync(path.join(Q, 'rubrica_resultados.md'), md);
console.log(md);
