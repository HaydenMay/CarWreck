#!/bin/bash
# Runs the REAL deformation code (Assets/DamageLab/Scripts/Deformation) outside Unity, on a synthetic subdivided body, through a
# small UnityEngine stand-in (UnityShim.cs). Checks progressive crushing against the instant result. Not a Unity or WebGL benchmark.
# Needs Mono and a Roslyn csc.exe (C# 9), e.g. from the Microsoft.Net.Compilers.Toolset NuGet package:  CSC=/path/to/csc.exe ./run.sh
# Optional: STAGGER=0.5 ./run.sh   (default 0.3, the arena default)
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$HERE/../../.."
CSC="${CSC:?set CSC to a Roslyn csc.exe}"
OUT="$(mktemp -d)"
for f in "$ROOT"/Assets/DamageLab/Scripts/Deformation/*.cs; do
  b=$(basename "$f"); [ "$b" = WireframeUtility.cs ] && continue
  if iconv -f utf-8 -t utf-8 "$f" >/dev/null 2>&1; then cp "$f" "$OUT/"; else iconv -f cp1252 -t utf-8 "$f" > "$OUT/$b"; fi
done
cat > "$OUT/Wire.cs" <<'X'
namespace DamageLab.Deformation { public static class WireframeUtility { public static UnityEngine.Mesh BuildLineMesh(UnityEngine.Mesh s, int[] t, UnityEngine.Vector3[] v) => new UnityEngine.Mesh(); public static UnityEngine.GameObject CreateOverlay(UnityEngine.Transform p, UnityEngine.Mesh m, UnityEngine.Material mat) => new UnityEngine.GameObject(); } }
X
NETSTD=$(ls /usr/lib/mono/4.*-api/Facades/netstandard.dll | tail -1)
mono "$CSC" -nologo -optimize+ -nowarn:CS0162,CS0414,CS0649,CS0169 -langversion:9 -out:"$OUT/sim.exe" -r:"$NETSTD" "$HERE/UnityShim.cs" "$HERE/Program.cs" "$OUT"/*.cs
mono "$OUT/sim.exe"
