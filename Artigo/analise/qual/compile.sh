#!/bin/bash
# Compila cada trecho aplicado (qual/comp/<id>/*.cs) com o Roslyn que vem no Editor da Unity,
# contra os assemblies do motor, e grava qual/compile.tsv (id, código de saída, nº de erros, 1º erro).
# Uso: UNITY="C:/Program Files/Unity/Hub/Editor/6000.4.6f1/Editor" bash qual/compile.sh   (Git Bash)
Q="$(cd "$(dirname "$0")" && pwd)"
UNITY="${UNITY:-/c/Program Files/Unity/Hub/Editor/6000.4.6f1/Editor}"
D="$UNITY/Data"
w() { cygpath -w "$1"; }
# MOTIVO: as mesmas referências da compilação do projeto (módulos do motor + perfil .NET 4.8 da Unity),
# sem os pacotes do projeto, que os trechos não usam.
{
  cat "$Q/csc_opcoes.rsp"
  for f in "$D/Managed/UnityEngine/"*.dll; do case "$(basename "$f")" in UnityEditor*) ;; *) echo "-r:\"$(w "$f")\"";; esac; done
  for f in "$D/UnityReferenceAssemblies/unity-4.8-api/"*.dll "$D/UnityReferenceAssemblies/unity-4.8-api/Facades/"*.dll; do echo "-r:\"$(w "$f")\""; done
} > "$Q/base.rsp"
: > "$Q/compile.tsv"
for d in "$Q"/comp/*/; do
  id=$(basename "$d"); files=""
  for f in "$d"*.cs; do files="$files \"$(w "$f")\""; done
  { echo "-out:\"$(w "$Q")\\out.dll\""; echo "$files"; } > "$Q/one.rsp"
  out=$("$D/NetCoreRuntime/dotnet.exe" "$D/DotNetSdkRoslyn/csc.dll" "@$(w "$Q/base.rsp")" "@$(w "$Q/one.rsp")" 2>&1); rc=$?
  err=$(echo "$out" | grep -m1 -E "error CS" | sed 's/.*error /error /' | tr '\t' ' ')
  nerr=$(echo "$out" | grep -c -E "error CS")
  printf "%s\t%s\t%s\t%s\n" "$id" "$rc" "$nerr" "$err" >> "$Q/compile.tsv"
done
rm -f "$Q/out.dll" "$Q/one.rsp"
echo "$(wc -l < "$Q/compile.tsv") trechos compilados"
