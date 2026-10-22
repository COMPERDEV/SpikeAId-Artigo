// Gera Revisao_resultados.html: comparação lado a lado das 4 células (2×2), avaliação às cegas
// com rubrica e tabelas de resultado. Tudo embutido num arquivo só, para abrir no navegador.
const fs = require('fs'), path = require('path');
const Q = __dirname, A = path.join(Q, '..', 'a2x2'), RESP = path.join(A, 'resp');
const out = process.argv[2];
const CELLS = ['sem_amador', 'sem_profissional', 'com_amador', 'com_profissional'];
const csvFile = { sem_amador: 'sem_amador.csv', sem_profissional: 'sem_profissional.csv', com_amador: 'com_amador.csv', com_profissional: 'com_profissional.csv' };
const FLAGS = ['contagem_errada', 'tipo_errado', 'fabricacao_simbolo', 'fabricacao_simbolo_estrita', 'fabricacao_valor', 'teto_ausente_violado',
  'vazamento_escopo', 'dicas_ausentes', 'indeterminacao_correta', 'indeterminacao_indevida', 'falso_estouro', 'valida_porem_errada'];
const flags = {};
for (const c of CELLS) {
  const lines = fs.readFileSync(path.join(A, csvFile[c]), 'utf8').replace(/^﻿/, '').split(/\r?\n/).filter(Boolean);
  const head = lines[0].split(';');
  for (const l of lines.slice(1)) { const v = l.split(';'); const o = {}; head.forEach((h, i) => o[h] = v[i]);
    const f = {}; for (const k of FLAGS) f[k] = o[k] === '1' ? 1 : 0; f.detalhe = (o.detalhe || '').replace(/^"|"$/g, ''); f.grupo = o.grupo;
    flags[`${c}__${o.caso}__r${o.repeticao}`] = f; }
}
const items = {}; for (const it of JSON.parse(fs.readFileSync(path.join(Q, 'items_avaliados.json'), 'utf8'))) items[it.id] = it;
const casos = {};
for (const f of fs.readdirSync(path.join(Q, 'casos'))) {
  const c = JSON.parse(fs.readFileSync(path.join(Q, 'casos', f), 'utf8'));
  casos[c.id] = { id: c.id, grupo: c.grupo, parametros: c.parametros, origem: c.origem,
    telemetria: c.userText.replace(/\s*script_sources:[\s\S]*$/, ''), temCodigo: /script_sources:/.test(c.userText),
    esperado: { achados: c.esperadoAchados, indeterminacao: c.esperadoIndeterminacao, semEstouro: c.esperadoSemEstouro } };
}
const original = JSON.parse(fs.readFileSync(path.join(Q, 'casos', 'G1_CPU_01.json'), 'utf8')).fontes[0].code;
const resp = {};
for (const f of fs.readdirSync(RESP)) {
  const j = JSON.parse(fs.readFileSync(path.join(RESP, f), 'utf8'));
  const cell = (f.startsWith('arquivo') ? 'sem' : 'com') + '_' + j.condicao;
  const key = `${cell}__${j.caso}__r${j.repeticao}`;
  let o = null; try { o = JSON.parse(j.texto); } catch (e) { }
  resp[key] = { findings: (o && o.findings || []).map((fd, k) => { const it = items[`${key}__f${k + 1}`] || {};
      return { tipo: fd.tipo, causa: fd.causa_raiz, codigo: fd.codigo_refatorado || '', expl: fd.explicacao_didatica,
        q: it.hasCode ? { forma: it.form, compila: it.compiles, erro: it.compileError, defeito: it.defectRemoved, restou: it.defectsLeft, campos: it.fieldsPreserved, perdidos: it.fieldsLost, aplicavel: it.applicable } : null }; }),
    dicas: o ? o.dicas_level_design : null, flags: flags[key] || {}, seg: Math.round(j.duracaoMs / 100) / 10, custo: j.custoUsd };
}
const md = f => fs.readFileSync(f, 'utf8');
const data = { casos, resp, original, tabelas: [md(path.join(A, 'analise_2x2.md')), md(path.join(Q, 'qualidade_codigo.md'))] };
const tpl = fs.readFileSync(path.join(Q, 'review_template.html'), 'utf8');
fs.writeFileSync(out, tpl.replace('/*__DATA__*/null', JSON.stringify(data).replace(/</g, '\\u003c')));
console.log('ok', (fs.statSync(out).size / 1e6).toFixed(2), 'MB');
