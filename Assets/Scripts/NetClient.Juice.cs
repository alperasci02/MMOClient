// NetClient.Juice.cs — Faz 5 (Sunum) his/juice dilimi: savaş ve olay geri bildirimini
// "hissedilir" kılan cila. TAMAMEN KOD — yeni asset yok, ağ/sunucu mantığına dokunmaz
// (Time.timeScale değiştirmez; yalnızca GÖRSEL yorumlamayı etkiler). Bkz. Docs/
// DECISION_LOG.md D-18 (his geçişi). İçerik: kamera sarsıntısı, kısa "hit-stop"
// (görsel donma), vuruşta ölçek-tokatı, ekran flaşları (seviye/ölüm/diriliş/bölge),
// ve zenginleştirilmiş yüzen hasar sayıları (pop + kontur + kayma).
using System.Collections.Generic;
using UnityEngine;

namespace MMO
{
    public partial class NetClient
    {
        // --- kamera sarsıntısı ---
        float shakeAmt = 0f;
        void AddShake(float amt) { shakeAmt = Mathf.Min(1.1f, Mathf.Max(shakeAmt, amt)); }
        // CameraRig.LateUpdate her karede bir kez çağırır: mevcut kare ofsetini döndürür + söndürür.
        Vector3 CameraShakeOffset()
        {
            if (shakeAmt <= 0.0008f) { shakeAmt = 0f; return Vector3.zero; }
            var off = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f) * 0.5f, Random.Range(-1f, 1f)) * shakeAmt;
            shakeAmt *= Mathf.Exp(-9f * Time.deltaTime);
            return off;
        }

        // --- hit-stop: kısa GÖRSEL donma (yalnızca varlık interpolasyonunu durdurur; sends akar) ---
        float hitStopUntil = 0f;
        bool HitStopped => Time.time < hitStopUntil;

        // --- vuruşta ölçek-tokatı (varlık başına) ---
        readonly Dictionary<ulong, float> hitPunch = new Dictionary<ulong, float>();
        readonly Dictionary<ulong, Vector3> baseScale = new Dictionary<ulong, Vector3>();
        readonly List<ulong> _punchKeys = new List<ulong>();
        const float PunchDur = 0.14f;

        // --- ekran flaşları (renk + zaman) ---
        Color flashColor;
        float flashTimer, flashDur;
        void ScreenFlash(Color c, float dur, float alpha) { flashColor = new Color(c.r, c.g, c.b, alpha); flashTimer = dur; flashDur = dur; }

        float levelUpFxTimer = 0f;
        GUIStyle floaterStyle, levelUpStyle;

        // Olay tetikleyicileri (ProtocolDispatch/ApplySnapshot'tan çağrılır)
        void TriggerLevelUpFx() { levelUpFxTimer = 1.4f; ScreenFlash(new Color(1f, 0.82f, 0.3f), 0.5f, 0.34f); AddShake(0.4f); }
        void TriggerDeathFx() { ScreenFlash(new Color(0.82f, 0.05f, 0.05f), 0.6f, 0.6f); AddShake(0.85f); }
        void TriggerRespawnFx() { ScreenFlash(Color.white, 0.35f, 0.5f); AddShake(0.2f); }
        void TriggerZoneFx() { ScreenFlash(new Color(0.5f, 0.8f, 1f), 0.35f, 0.26f); AddShake(0.15f); }

        // Vuruş anı: ölçek-tokatı + (kendine/hedefe) sarsıntı + kısa hit-stop.
        void HitJuice(ulong id, int dmg, bool onSelf, bool isMyTarget)
        {
            hitPunch[id] = PunchDur;
            if (onSelf) { AddShake(0.30f + Mathf.Clamp01(dmg / 40f) * 0.35f); hitStopUntil = Time.time + 0.05f; }
            else if (isMyTarget) { AddShake(0.12f); hitStopUntil = Time.time + 0.045f; }
            else AddShake(0.05f);
        }

        // Update() her karede: flaş/level-fx/tokat zamanlayıcılarını söndür.
        void TickJuice(float dt)
        {
            if (flashTimer > 0f) flashTimer -= dt;
            if (levelUpFxTimer > 0f) levelUpFxTimer -= dt;
            if (hitPunch.Count > 0)
            {
                _punchKeys.Clear(); _punchKeys.AddRange(hitPunch.Keys);
                foreach (var k in _punchKeys)
                {
                    float v = hitPunch[k] - dt;
                    if (v <= 0f)
                    {
                        hitPunch.Remove(k);
                        if (cubes.TryGetValue(k, out var g) && g != null && baseScale.TryGetValue(k, out var bs))
                            g.transform.localScale = bs; // tokat bitti -> taban ölçeğe dön
                    }
                    else hitPunch[k] = v;
                }
            }
        }

        // Update() cube döngüsünde çağrılır: tokat varsa taban ölçeğin üstüne pop uygula.
        void ApplyPunchScale(ulong id, Transform tr)
        {
            if (hitPunch.TryGetValue(id, out var pt) && pt > 0f && baseScale.TryGetValue(id, out var bs))
                tr.localScale = bs * (1f + 0.22f * (pt / PunchDur));
        }

        // OnGUI: tam-ekran flaş + seviye-atlama yazısı (dünya üstünde, panellerin altında).
        void DrawScreenFlash()
        {
            if (flashTimer > 0f && flashDur > 0f)
            {
                float a = flashColor.a * (flashTimer / flashDur);
                var prev = GUI.color;
                GUI.color = new Color(flashColor.r, flashColor.g, flashColor.b, a);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = prev;
            }
            if (levelUpFxTimer > 0f)
            {
                if (levelUpStyle == null)
                    levelUpStyle = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                float a = Mathf.Clamp01(levelUpFxTimer / 1.4f);
                float rise = (1.4f - levelUpFxTimer) * 18f;
                levelUpStyle.normal.textColor = new Color(1f, 0.85f, 0.35f, a);
                GUI.Label(new Rect(0, Screen.height * 0.26f - rise, Screen.width, 44), "SEVİYE ATLADIN!", levelUpStyle);
            }
        }

        // OnGUI: zenginleştirilmiş yüzen hasar/iyileşme sayıları (pop-ölçek + siyah kontur + kayma).
        void DrawFloaters(Camera wcam)
        {
            if (floaters.Count == 0) return;
            if (floaterStyle == null)
                floaterStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = false };
            foreach (var f in floaters)
            {
                Vector3 sp = wcam.WorldToScreenPoint(f.Pos);
                if (sp.z <= 0f) continue;
                float pop = Mathf.Lerp(1.5f, 1f, Mathf.Clamp01((1.1f - f.Life) / 0.14f));
                floaterStyle.fontSize = Mathf.RoundToInt(f.Size * pop);
                float a = Mathf.Clamp01(f.Life);
                var rect = new Rect(sp.x - 60f, Screen.height - sp.y - 16f, 120f, 30f);
                // siyah kontur (okunaklılık)
                floaterStyle.normal.textColor = new Color(0f, 0f, 0f, a * 0.7f);
                GUI.Label(new Rect(rect.x + 1.5f, rect.y, rect.width, rect.height), f.Text, floaterStyle);
                GUI.Label(new Rect(rect.x - 1.5f, rect.y, rect.width, rect.height), f.Text, floaterStyle);
                GUI.Label(new Rect(rect.x, rect.y + 1.5f, rect.width, rect.height), f.Text, floaterStyle);
                GUI.Label(new Rect(rect.x, rect.y - 1.5f, rect.width, rect.height), f.Text, floaterStyle);
                // ana renk
                var col = f.Col; col.a = a;
                floaterStyle.normal.textColor = col;
                GUI.Label(rect, f.Text, floaterStyle);
            }
        }
    }
}
