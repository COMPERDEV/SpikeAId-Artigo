// Prepara a avaliação qualitativa do código refatorado (G1_CPU e G2_GC, as 4 células).
// Para cada achado com código: aplica o trecho ao script real como o desenvolvedor faria
// (classe inteira substitui o arquivo; só membros entram na classe original no lugar dos
// homônimos; outra classe entra como arquivo novo), grava a pasta para compilar e calcula
// os critérios lexicais (defeito plantado fora do caminho por quadro, campos públicos).
const fs = require('fs'), path = require('path');
const Q = __dirname, RESP = path.join(Q, '..', 'a2x2', 'resp');
const original = JSON.parse(fs.readFileSync(path.join(Q, 'casos', 'G1_CPU_01.json'), 'utf8')).fontes[0].code;
const PUBLIC_FIELDS = ['stressGc', 'stressCpu', 'stressDraw', 'testPrefab', 'spawnRadius', 'cubesPerFrame', 'useUniqueMaterials',
  'cpuScansPerFrame', 'emptyObjects', 'gcConcatIterations', 'gcArrayAllocs', 'overdrawLayers', 'overdrawMaterial', 'prepopulateCount'];

// Máscara: troca comentários e literais por espaços, preservando posições, para contar chaves.
function mask(src) {
  let out = '', i = 0;
  while (i < src.length) {
    const c = src[i], n = src[i + 1];
    if (c === '/' && n === '/') { while (i < src.length && src[i] !== '\n') { out += ' '; i++; } continue; }
    if (c === '/' && n === '*') { out += '  '; i += 2; while (i < src.length && !(src[i] === '*' && src[i + 1] === '/')) { out += src[i] === '\n' ? '\n' : ' '; i++; } out += '  '; i += 2; continue; }
    if (c === '"' || c === '\'') {
      const verb = src[i - 1] === '@'; out += ' '; i++;
      while (i < src.length && src[i] !== c) { if (!verb && src[i] === '\\') { out += ' '; i++; } out += src[i] === '\n' ? '\n' : ' '; i++; }
      out += ' '; i++; continue;
    }
    out += c; i++;
  }
  return out;
}
// Membros de nível superior de um trecho (profundidade relativa `base`): métodos e campos.
function members(src, base) {
  const m = mask(src), res = [];
  let depth = 0, start = 0;
  for (let i = 0; i < m.length; i++) {
    const ch = m[i];
    if (ch === '{') { if (depth === base) { const head = m.slice(start, i); const mm = /(\w+)\s*\([^()]*\)\s*$/.exec(head.trim()) ; if (mm) res.push({ kind: 'method', name: mm[1], from: start, open: i }); else res.push({ kind: 'block', head: head.trim(), from: start, open: i }); } depth++; }
    else if (ch === '}') { depth--; if (depth === base) { const last = res[res.length - 1]; if (last && last.close === undefined) last.close = i; start = i + 1; } }
    else if (ch === ';' && depth === base) { const head = m.slice(start, i); const mm = /(\w+)\s*(=[^;]*)?$/.exec(head.replace(/\([^)]*\)/g, '').trim()); if (mm && !/^\s*using\b/.test(head)) res.push({ kind: 'field', name: mm[1], from: start, close: i }); start = i + 1; }
  }
  return res;
}
// Corpo (texto) de cada método, em qualquer profundidade: nome → [corpos].
function methodBodies(src) {
  const m = mask(src), bodies = {};
  const re = /\b(\w+)\s*\([^()]*\)\s*\{/g; let mm;
  while ((mm = re.exec(m))) {
    const name = mm[1]; if (['if', 'for', 'foreach', 'while', 'switch', 'using', 'lock', 'catch', 'fixed'].includes(name)) continue;
    let d = 0, j = mm.index + mm[0].length - 1;
    for (; j < m.length; j++) { if (m[j] === '{') d++; else if (m[j] === '}') { d--; if (!d) break; } }
    (bodies[name] = bodies[name] || []).push(src.slice(mm.index + mm[0].length, j));
  }
  return bodies;
}
// Classe StressTestScene dentro do namespace: posição das chaves da classe no original.
function classSpan(src, cls) {
  const m = mask(src); const k = m.search(new RegExp('class\\s+' + cls + '\\b')); if (k < 0) return null;
  const open = m.indexOf('{', k); let d = 0, j = open;
  for (; j < m.length; j++) { if (m[j] === '{') d++; else if (m[j] === '}') { d--; if (!d) break; } }
  return { open, close: j };
}
function applySnippet(code) {
  const usings = (code.match(/^\s*using\s+[\w.]+\s*;/gm) || []).map(s => s.trim());
  if (/\bclass\s+StressTestScene\b/.test(mask(code))) return { form: 'classe_original', files: { 'StressTestScene.cs': code }, scene: code };
  if (/\bclass\s+\w+/.test(mask(code))) return { form: 'outra_classe', files: { 'StressTestScene.cs': original, 'Snippet.cs': code }, scene: original, own: code };
  // só membros: tira os using do topo e substitui os homônimos na classe original
  const body = code.replace(/^\s*using\s+[\w.]+\s*;\s*$/gm, '');
  const mem = members(body, 0);
  const span = classSpan(original, 'StressTestScene');
  const clsText = original.slice(span.open + 1, span.close);
  const orig = members(clsText, 0);
  const drop = new Set(mem.filter(x => x.kind !== 'block').map(x => x.name));
  let kept = '', cur = 0;
  for (const o of orig) { if (o.kind !== 'block' && drop.has(o.name)) { kept += clsText.slice(cur, o.from); cur = (o.close ?? o.from) + 1; } }
  kept += clsText.slice(cur);
  const merged = usings.join('\n') + '\n' + original.slice(0, span.open + 1) + kept + '\n// ---- trecho da IA ----\n' + body + '\n' + original.slice(span.close);
  const statementsOnly = mem.length === 0;
  return { form: statementsOnly ? 'trecho' : 'metodo', files: { 'StressTestScene.cs': merged }, scene: merged };
}
const CPU = /\bFind(?:Objects?OfType|ObjectsByType|AnyObjectByType|FirstObjectByType)\b/;
const CONCAT = /\+=\s*[^;]*\.ToString\s*\(|=\s*\w+\s*\+\s*\w+\.ToString\s*\(/;
const ARRAY = /new\s+object\s*\[/;
function perFrame(src) {
  const b = methodBodies(src), seen = new Set(), queue = ['Update', 'LateUpdate', 'FixedUpdate'];
  let text = '';
  while (queue.length) { const n = queue.shift(); if (seen.has(n) || !b[n]) continue; seen.add(n);
    for (const t of b[n]) { const mt = mask(t); text += mt + '\n';
      for (const call of mt.matchAll(/\b(\w+)\s*\(/g)) if (b[call[1]] && !['Start', 'Awake', 'OnEnable'].includes(call[1])) queue.push(call[1]); } }
  return text;
}
const casos = {}; for (const f of fs.readdirSync(path.join(Q, 'casos'))) { const c = JSON.parse(fs.readFileSync(path.join(Q, 'casos', f), 'utf8')); casos[c.id] = c; }
const items = [];
fs.rmSync(path.join(Q, 'comp'), { recursive: true, force: true });
for (const f of fs.readdirSync(RESP).sort()) {
  const j = JSON.parse(fs.readFileSync(path.join(RESP, f), 'utf8'));
  if (!/^G[12]_/.test(j.caso)) continue;
  const cell = (f.startsWith('arquivo') ? 'sem' : 'com') + '_' + j.condicao;
  let o; try { o = JSON.parse(j.texto); } catch (e) { continue; }
  const par = casos[j.caso].parametros;
  const defects = []; if (/stressCpu/.test(par)) defects.push(['cpu', CPU]); if (/stressGc/.test(par)) { defects.push(['concat', CONCAT]); defects.push(['array', ARRAY]); }
  (o.findings || []).forEach((fd, k) => {
    const code = (fd.codigo_refatorado || '').trim();
    const it = { id: `${cell}__${j.caso}__r${j.repeticao}__f${k + 1}`, cell, caso: j.caso, rep: j.repeticao, finding: k + 1, tipo: fd.tipo, hasCode: !!code, code };
    if (code) {
      const a = applySnippet(code); it.form = a.form;
      const dir = path.join(Q, 'comp', it.id); fs.mkdirSync(dir, { recursive: true });
      for (const [n, t] of Object.entries(a.files)) fs.writeFileSync(path.join(dir, n), t);
      const pf = perFrame(a.own || a.scene);
      it.defectsLeft = defects.filter(([, re]) => re.test(pf)).map(([n]) => n);
      it.defectRemoved = it.defectsLeft.length === 0;
      if (a.form !== 'outra_classe') {
        const ms = mask(a.scene);
        it.fieldsLost = PUBLIC_FIELDS.filter(n => !new RegExp('(public|\\[SerializeField\\])[^;{}()]*\\b' + n + '\\b\\s*(=|;)').test(ms));
        it.fieldsPreserved = it.fieldsLost.length === 0;
      }
    }
    items.push(it);
  });
}
fs.writeFileSync(path.join(Q, 'items.json'), JSON.stringify(items, null, 1));
const by = {}; for (const it of items) { const k = it.cell + ' ' + (it.form || 'vazio'); by[k] = (by[k] || 0) + 1; }
console.log(items.length, 'achados;', items.filter(i => i.hasCode).length, 'com código'); console.log(by);
