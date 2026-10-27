mkdir -p Recordings/eval
#!/bin/sh
# 使い方: sh EffectEval~/run.sh 名前 [演出の名前]  （3d-rhythmgame で）
name=$1; fx=$2
args=""; [ -n "$fx" ] && args="-effects $fx"
Builds/Play/Play.exe -screen-fullscreen 0 -screen-width 640 -screen-height 360 -record "Recordings/eval/$name.mp4" -record-from 0 -record-length 89 -record-fps 30 -record-size 1280x720 -record-canvas $args -logFile "Recordings/eval/$name.log" > /dev/null 2>&1
grep -E "Exception|無い" "Recordings/eval/$name.log" | head -3
PYTHONIOENCODING=utf-8 py EffectEval~/evaluate.py "Recordings/eval/$name" --audio Assets/GameMusic.wav --song Assets/Rhythm/Songs/kodaisai-2025/song.json --title "$name ${fx:-}" > /dev/null
