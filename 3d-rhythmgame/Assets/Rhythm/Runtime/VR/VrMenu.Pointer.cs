using System;
using System.Collections.Generic;
using Arte.Rhythm.Gameplay;
using Arte.Rhythm.Song;
using Arte.Rhythm.Led;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Arte.Rhythm.VR
{
    // ホーム画面を置く・指す・押す，表示を毎フレーム直す（VrMenu の一部．VrMenu.cs と同じクラス）
    partial class VrMenu
    {
        void UpdateMenu()
        {
            pauseLabel.text = conductor != null && conductor.IsPaused ? "再開" : "一時停止";
            effectsLabel.text = $"演出を切り替える（今: {SongVisualSync.CurrentEffects}）";
            viewLabel.text = vr != null && vr.ChildView ? "自分の目の高さにする" : "子どもの目の高さにする";
            startLabel.text = conductor != null && conductor.IsStarted ? "曲を最初から" : "曲を始める";
            playersLabel.text = $"人数: {PlaySettings.Players} 人（曲を始めると変わる）";
            countLabel.text = $"数え方: {CountIn.Name(PlaySettings.CountBeats)}";
            latencyLabel.text = $"VR の遅れの補正 {VrLatency.Ms}ms\n（{VrLatency.Where}）" +
                (testOffsets.Count > 0 ? $"\nテストプレイのずれ {Median(testOffsets):+0;-0}ms（{testOffsets.Count} 回）" : "");

            // 頭の位置が取れて落ち着いてから目の前に置く．VR を始めた直後は頭の位置がまだ 0 だったり，
            // ViewerVR が中心に戻す・目の高さを合わせる前だったりして，最初のホーム画面だけ下の方に出ていた（2026-11-03）．
            // 左の ≡ で出したときと同じ置き方（ShowMenu）にそろえる
            if (!placed)
            {
                bool tracked = head.transform.localPosition.y > 0.3f;
                if (!tracked) settledAt = -1;
                else if (settledAt < 0) settledAt = Time.unscaledTime;
                bool settled = tracked && Time.unscaledTime - settledAt > 1f;
                if (!settled && Time.unscaledTime - shownAt < 6f) return;
                ShowMenu(true);
            }
            // 目の高さから大きく外れた（床の近くに出た・頭の位置があとから取れた）ら置き直す．
            // 起動直後に下の方に出て，上の段のボタン 2 つだけが見えていた（2026-11-03）
            else
            {
                var d = menu.transform.position - head.transform.position;
                float dy = d.y + 0.15f;
                d.y = 0;
                if (Mathf.Abs(dy) > 0.35f || d.magnitude > 2.5f) ShowMenu(true);
            }
            // コントローラが見つかった / 外れたらログに出す（押せないときに入力が来ているかを確かめるため）
            int found = 0;
            foreach (var p in pointers) found += p.aimPos.controls.Count + p.gripPos.controls.Count + p.trigger.controls.Count;
            if (found != lastFound)
            {
                lastFound = found;
                foreach (var p in pointers)
                    Debug.Log($"[VrMenu] 入力: 指す位置 {p.aimPos.controls.Count}，握る位置 {p.gripPos.controls.Count}，トリガー {p.trigger.controls.Count}（{p.trigger.bindings[0].path}）");
            }

            // 光線（左右どちらでも．指す向きが取れなければ握る位置から）．トリガーを半分くらい握ったら押す
            hovered = -1;
            var plane = new Plane(-menu.transform.forward, menu.transform.position);
            foreach (var p in pointers)
            {
                bool aim = p.aimPos.controls.Count > 0;
                var local = aim ? p.aimPos.ReadValue<Vector3>() : p.gripPos.ReadValue<Vector3>();
                var q = aim ? p.aimRot.ReadValue<Quaternion>() : p.gripRot.ReadValue<Quaternion>();
                if (q.x == 0 && q.y == 0 && q.z == 0 && q.w == 0)
                {
                    p.laser.enabled = false;
                    continue;
                }
                var origin = rig != null ? rig.TransformPoint(local) : local;
                var dir = (rig != null ? rig.rotation : Quaternion.identity) * q * Vector3.forward;
                Vector3 end = origin + dir * 2f;
                int hit = -1;
                if (plane.Raycast(new Ray(origin, dir), out float dist) && dist < 3f)
                {
                    end = origin + dir * dist;
                    hit = ButtonAt(end);
                }
                p.laser.enabled = true;
                p.laser.SetPosition(0, origin);
                p.laser.SetPosition(1, end);
                bool down = p.trigger.ReadValue<float>() > 0.5f;
                if (hit >= 0)
                {
                    hovered = hit;
                    if (!loggedHover)
                    {
                        loggedHover = true;
                        Debug.Log("[VrMenu] 光線がボタンに当たった");
                    }
                    if (down && !p.wasDown)
                    {
                        Debug.Log($"[VrMenu] ボタンを押した（{hit}）");
                        p.wasDown = true;
                        buttons[hit].action();
                        return;
                    }
                }
                p.wasDown = down;
            }
            for (int i = 0; i < buttons.Count; i++) buttons[i].image.color = i == hovered ? ButtonHover : ButtonColor;

            // 見つめて押す（コントローラも手も使えないとき．ボタンを 1.5 秒見つめると押す．押すまでの間はボタンの色が変わっていく）
            int gaze = -1;
            var eye = new Ray(head.transform.position, head.transform.forward);
            if (plane.Raycast(eye, out float gd) && gd < 3f) gaze = ButtonAt(eye.GetPoint(gd));
            if (gaze != gazeButton)
            {
                gazeButton = gaze;
                gazeSince = Time.unscaledTime;
            }
            if (gaze >= 0)
            {
                float t = (Time.unscaledTime - gazeSince) / GazeSec;
                if (gaze != hovered) buttons[gaze].image.color = Color.Lerp(ButtonColor, new Color(0.35f, 0.6f, 0.4f), Mathf.Clamp01(t));
                if (t >= 1)
                {
                    Debug.Log($"[VrMenu] 見つめて押した（{gaze}）");
                    gazeSince = Time.unscaledTime + 1f;     // 続けて押さない
                    buttons[gaze].action();
                    return;
                }
            }

            // 手の先でつつく（板から 2cm 以内に入ったら押す．続けて押さないよう 0.5 秒あける）
            pokeCooldown -= Time.unscaledDeltaTime;
            if (pokeCooldown > 0) return;
            foreach (var p in PokePoints)
            {
                float d = plane.GetDistanceToPoint(p);
                if (Mathf.Abs(d) > 0.02f) continue;
                int b = ButtonAt(p);
                if (b < 0) continue;
                buttons[b].action();
                pokeCooldown = 0.5f;
                break;
            }
        }

        int ButtonAt(Vector3 world)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                var rt = buttons[i].rect;
                Vector2 local = rt.InverseTransformPoint(world);
                if (rt.rect.Contains(local)) return i;
            }
            return -1;
        }
    }
}
