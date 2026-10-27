#!/bin/sh
# 演出案を組み立て → ビルド → 3 案を録画して評価（3d-rhythmgame で）．sh EffectEval~/cycle.sh v2
v=$1
U="/c/Program Files/Unity/Hub/Editor/6000.0.55f1/Editor/Unity.exe"
"$U" -batchmode -quit -nographics -projectPath . -logFile Builds/proposals.log -executeMethod Arte.Rhythm.Editor.EffectProposals.Build
grep -E "\[EffectProposals\]|error CS" Builds/proposals.log | head -3
"$U" -batchmode -quit -nographics -projectPath . -logFile Builds/build.log -executeMethod Arte.Rhythm.Editor.PlaySceneBuilder.BuildPlayer
grep -E "Shader error|error CS" Builds/build.log | head -3
for p in A_星座 B_夜明け C_鼓動; do sh EffectEval~/run.sh "${v}_${p%%_*}" "AI案$p"; done
py EffectEval~/compare.py baseline ${v}_A ${v}_B ${v}_C
