# SpikeAId — reproducibility package

Data, code and Unity project behind the article *Prompt engineering for reducing hallucinations
when integrating an LLM with the Unity Engine via a REST API* (Anderson Comper de Araujo,
Universidade do Vale do Itajaí — UNIVALI, 2026).

The study compares two system prompts for an LLM that writes performance reports from Unity
telemetry — the project's original prompt (*amador*, the reference) and a layered prompt
(*profissional*, the structured one) — with and without the source code of the scripts in the
call stack attached: a 2×2 design, 30 test cases with known answers, 5 repetitions per case,
600 responses from `gemini-3.8-flash` with a native response schema and temperature 0.

## What is here

| Path | Content |
|---|---|
| `Assets/PerformanceAdvisor/` | the Unity Editor plugin **exactly as used in the experiment**. It was called *Performance Advisor* then and was later renamed **SpikeAId**; namespaces and menus keep the old name. |
| `Assets/PerformanceAdvisor/Editor/Experiment/` | experiment harness: case capture, batch collection, validator (hallucination metrics), analysis, self-test |
| `Assets/PerformanceAdvisor/Runtime/` | stress-test script (`StressTestScene.cs`, intentionally bad code), prefab and overdraw shader |
| `Assets/PerformanceAdvisor/StressScenes/` | stress scenes built with *Tools → Performance Advisor → Build … Stress Scene* |
| `Artigo/` | the experiment: prompts, response schema, configuration, 30 cases, 600 raw responses, results ([`Artigo/README.md`](Artigo/README.md), in Portuguese) |
| `Artigo/analise/` | 2×2 analysis, code-quality criteria, side-by-side viewer and blind rubric packet ([`Artigo/analise/README.md`](Artigo/analise/README.md)) |

The prompts, the telemetry payload and the reports are in Portuguese: the tool targets
Brazilian beginner developers, and the language is a property of the experiment.

## Reproducing

**Without Unity or an API key** — recompute every table of the article from the raw responses
(Node.js 18+; compiling the suggested code also needs Unity 6000.4.6f1):

```
node Artigo/analise/preparar.js
node Artigo/analise/a2x2/analise2x2.js
```

See `Artigo/analise/README.md` for the full sequence. Open `Artigo/analise/Revisao_resultados.html`
in a browser to compare the four conditions side by side, case by case.

**With Unity** — open the project in **Unity 6000.4.6f1** (URP). The condition is selected by
`Artigo/condicao.txt` (`amador` or `profissional`); in the original repository the two conditions
lived in two branches that differ only in this file. Menu *Tools → Performance Advisor → Artigo*
captures the cases, collects the responses (resuming where it stopped) and computes the metrics;
the same steps run from the command line as described in `Artigo/README.md`.

Collecting responses calls the Gemini API with **your own key**, entered in the plugin's
settings tab. The key is stored in the Editor's `EditorPrefs` on your machine and never in the
project. API usage is billed by Google to your account; the full collection cost about
US$ 5 at list prices in October 2026. Recapturing the cases measures *your* machine, so new
cases will differ from the published ones; the analysis of the published responses does not
depend on that.

## Provenance

Exported from the development repository (COMPERDEV/SpikeAId), branch
`artigo-prompt-profissional`, commit `4808ce4`, with the account identifiers removed from
`ProjectSettings` and the Unity template tutorial assets left out. Each response file records
the model version declared by the API, the full HTTP body and SHA-256 digests of the prompt, the
user turn and the generation config that were sent.

## License

[TO BE DEFINED]

## How to cite

[Article reference — pending publication.]
