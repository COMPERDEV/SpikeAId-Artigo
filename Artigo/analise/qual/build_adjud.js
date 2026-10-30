// Página de adjudicação humana dos 8 laudos em que os avaliadores de IA mais divergiram.
// Mostra telemetria, laudo, fatos objetivos do código (Q1–Q3) e as notas/comentários das IAs,
// sem revelar a condição. Exporta adjudicacao_autor.csv.
const fs = require('fs'), path = require('path');
const Q = __dirname, RESP = path.join(Q, '..', 'a2x2', 'resp');
const [Z, CL, OUT] = process.argv.slice(2);
const LAUDOS = [7, 9, 15, 24, 26, 28, 29, 40];
const parse = f => { const L = fs.readFileSync(f, 'utf8').replace(/^﻿/, '').split(/\r?\n/).filter(Boolean);
  const o = {}; for (const l of L.slice(1)) { const v = l.split(';').map(s => s.replace(/^"|"$/g, '')); o[+v[0]] = { H1: v[1], H2: v[2], H3: v[3], H4: v[4], c: v.slice(5).join(', ') }; } return o; };
const raters = [['GPT astra', parse(path.join(Z, 'notas_astra_medium.csv'))], ['GPT sol', parse(path.join(Z, 'notas_sol_medium.csv'))], ['GPT luna', parse(path.join(Z, 'notas_luna_high.csv'))],
  ['Claude Opus (cego)', parse(path.join(CL, 'notas_claude-opus-5.5_cego.csv'))], ['Claude Sonnet (cego)', parse(path.join(CL, 'notas_claude-sonnet-5.5_cego.csv'))], ['Claude Haiku (cego)', parse(path.join(CL, 'notas_claude-haiku-5.5_cego.csv'))],
  ['Claude que montou o experimento', parse(path.join(CL, 'notas_claude-opus-5.5.csv'))]];
const key = {}; for (const l of fs.readFileSync(path.join(Q, 'chave_rubrica.csv'), 'utf8').split(/\r?\n/).slice(1).filter(Boolean)) { const [n, caso, cell] = l.split(';'); key[+n] = { caso, cell }; }
const items = {}; for (const it of JSON.parse(fs.readFileSync(path.join(Q, 'items_avaliados.json'), 'utf8'))) items[it.id] = it;
const casos = {}; for (const f of fs.readdirSync(path.join(Q, 'casos'))) { const c = JSON.parse(fs.readFileSync(path.join(Q, 'casos', f), 'utf8')); casos[c.id] = c; }
const original = casos['G1_CPU_01'].fontes[0].code;
const data = LAUDOS.map(n => {
  const { caso, cell } = key[n]; const [cod, cond] = cell.split('_');
  const fname = (cod === 'sem' ? 'arquivo__gemini-3.8-flash-sem-codigo__respostas__' : 'respostas__') + cond + '__' + caso + '_r1.json';
  const j = JSON.parse(fs.readFileSync(path.join(RESP, fname), 'utf8')); const o = JSON.parse(j.texto);
  const c = casos[caso];
  return { n, caso, cena: c.parametros + (c.origem !== 'captura' ? ' (' + c.origem + ')' : ''), telemetria: c.userText.replace(/\s*script_sources:[\s\S]*$/, ''),
    findings: o.findings.map((f, k) => { const it = items[`${cell}__${caso}__r1__f${k + 1}`] || {};
      return { tipo: f.tipo, causa: f.causa_raiz, codigo: f.codigo_refatorado || '', expl: f.explicacao_didatica,
        q: it.hasCode ? { forma: it.form, compila: it.compiles, defeito: it.defectRemoved, restou: it.defectsLeft || [], campos: it.fieldsPreserved, perdidos: it.fieldsLost || [], escapado: it.escaped } : null }; }),
    ias: raters.map(([nome, r]) => ({ nome, ...r[n] })) };
});
const tpl = fs.readFileSync(path.join(Q, 'adjud_template.html'), 'utf8');
fs.writeFileSync(OUT, tpl.replace('/*__DATA__*/null', JSON.stringify({ laudos: data, original }).replace(/</g, '\\u003c')));
console.log('ok', (fs.statSync(OUT).size / 1024).toFixed(0), 'KB');
