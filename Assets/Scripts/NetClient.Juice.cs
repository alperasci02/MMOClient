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
        // --- kamera sarsıntısı: trauma-tabanlı, Perlin-yumuşak, DÖNME ağırlıklı ---
        // Ham Random titremesi yerine: "trauma" [0..1] birikir, sönümlenir; sarsıntı = trauma²
        // (küçük olaylar neredeyse hiç, büyük olaylar sert — sabit titreme yok). Perlin gürültüsü
        // sürekli/organik bir salınım verir; ağırlık POZİSYON değil DÖNME'de olduğu için tepeden
        // kamerada "kayma" değil gerçek "sarsılma" hissi olur. Inspector'dan shakeStrength ile
        // canlı ayarlanır (0 = kapalı).
        [Range(0f, 1.5f)] public float shakeStrength = 1f;
        float trauma = 0f;
        float _seedX, _seedY, _seedZ; bool _seedInit;
        void AddShake(float amt) { trauma = Mathf.Clamp01(trauma + amt); }

        // CameraRig.LateUpdate her karede bir kez çağırır (taban konum/dönme ayarlandıktan sonra).
        void ApplyCameraShake(Camera cam, Quaternion baseRot)
        {
            if (trauma > 0f) trauma = Mathf.Max(0f, trauma - 1.4f * Time.deltaTime); // lineer sön
            float shake = trauma * trauma * Mathf.Max(0f, shakeStrength); // karesel yanıt
            if (shake <= 0.0001f) { cam.transform.rotation = baseRot; return; }

            if (!_seedInit) { _seedX = Random.value * 1000f; _seedY = Random.value * 1000f; _seedZ = Random.value * 1000f; _seedInit = true; }
            float t = Time.time * 26f; // Perlin frekansı (yumuşak ama canlı)
            float nx = Mathf.PerlinNoise(_seedX, t) * 2f - 1f;
            float ny = Mathf.PerlinNoise(_seedY, t) * 2f - 1f;
            float nz = Mathf.PerlinNoise(_seedZ, t) * 2f - 1f;

            const float maxAngle = 4f;  // derece — asıl his burada (dönme jolt'u)
            const float maxPos = 0.5f;  // küçük konum tekmesi (dönmeyi tamamlar)
            cam.transform.rotation = baseRot * Quaternion.Euler(ny * maxAngle * shake, nx * maxAngle * shake, nz * maxAngle * shake);
            cam.transform.position += new Vector3(nx, nz * 0.4f, ny) * (maxPos * shake);
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
        GUIStyle floaterStyle, levelUpStyle, deathVeilStyle;

        // --- ölüm perdesi: ölünce koyu-kırmızıya solar + tutar, dirilince içeri solar ---
        float deathVeil = 0f;

        // --- darbe kıvılcımı: vuruş noktasında kısa, genişleyip sönen flaş (asset yok) ---
        struct Spark { public Vector3 Pos; public float Life; public Color Col; }
        readonly List<Spark> sparks = new List<Spark>();
        const float SparkLife = 0.18f;
        void SpawnSpark(float x, float y, float z, Color c) { sparks.Add(new Spark { Pos = new Vector3(x, y, z), Life = SparkLife, Col = c }); }

        // Olay tetikleyicileri (ProtocolDispatch/ApplySnapshot'tan çağrılır)
        void TriggerLevelUpFx() { levelUpFxTimer = 1.4f; ScreenFlash(new Color(1f, 0.82f, 0.3f), 0.5f, 0.34f); AddShake(0.5f); }
        void TriggerDeathFx() { ScreenFlash(new Color(0.82f, 0.05f, 0.05f), 0.6f, 0.6f); AddShake(0.75f); }
        void TriggerRespawnFx() { ScreenFlash(Color.white, 0.35f, 0.5f); AddShake(0.3f); }
        void TriggerZoneFx() { ScreenFlash(new Color(0.5f, 0.8f, 1f), 0.35f, 0.26f); AddShake(0.2f); }

        // Vuruş anı: ölçek-tokatı + kısa hit-stop. SARSINTI yalnızca HASAR ALINCA (isabet atarken
        // değil) — sürekli dövüşte kamera titremesin diye. İsabetin hissi tokat + hit-stop + sayıda.
        void HitJuice(ulong id, int dmg, bool onSelf, bool isMyTarget)
        {
            hitPunch[id] = PunchDur;
            if (onSelf)
            {
                AddShake(0.28f + Mathf.Clamp01(dmg / 40f) * 0.32f);
                hitStopUntil = Time.time + 0.05f;
                ScreenFlash(new Color(0.85f, 0.12f, 0.12f), 0.22f, 0.16f); // hasar aldın: kısa kırmızı nabız
            }
            else if (isMyTarget) hitStopUntil = Time.time + 0.045f; // isabet: hit-stop + tokat var, sarsıntı yok
        }

        // Update() her karede: flaş/level-fx/tokat zamanlayıcılarını söndür.
        void TickJuice(float dt)
        {
            if (flashTimer > 0f) flashTimer -= dt;
            if (levelUpFxTimer > 0f) levelUpFxTimer -= dt;
            // ölüm perdesi: ölünce ~0.8s'de dolar, dirilince ~0.5s'de boşalır
            deathVeil = Mathf.MoveTowards(deathVeil, myHp <= 0 ? 1f : 0f, dt / (myHp <= 0 ? 0.8f : 0.5f));
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
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                var s = sparks[i]; s.Life -= dt; sparks[i] = s;
                if (s.Life <= 0f) sparks.RemoveAt(i);
            }
        }

        // OnGUI: ölüm perdesi (koyu-kırmızı örtü + kalıcı ölüm mesajı). Dünya üstünde, panellerin altında.
        void DrawDeathVeil()
        {
            if (deathVeil <= 0.001f) return;
            var prev = GUI.color;
            GUI.color = new Color(0.14f, 0f, 0f, deathVeil * 0.72f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;
            if (deathVeil > 0.5f && myHp <= 0)
            {
                if (deathVeilStyle == null)
                    deathVeilStyle = new GUIStyle(GUI.skin.label) { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                float a = (deathVeil - 0.5f) * 2f;
                deathVeilStyle.normal.textColor = new Color(1f, 0.32f, 0.3f, a);
                GUI.Label(new Rect(0, Screen.height * 0.40f, Screen.width, 44),
                    string.IsNullOrEmpty(deathMsg) ? "ÖLDÜN" : deathMsg, deathVeilStyle);
            }
        }

        // OnGUI: darbe kıvılcımları (genişleyen, sönen kare flaş — dünya üstünde, sayıların altında).
        void DrawSparks(Camera wcam)
        {
            if (sparks.Count == 0) return;
            var prev = GUI.color;
            foreach (var s in sparks)
            {
                Vector3 sp = wcam.WorldToScreenPoint(s.Pos);
                if (sp.z <= 0f) continue;
                float t = Mathf.Clamp01((SparkLife - s.Life) / SparkLife);
                float size = Mathf.Lerp(12f, 52f, t);
                GUI.color = new Color(s.Col.r, s.Col.g, s.Col.b, (1f - t) * 0.6f);
                GUI.DrawTexture(new Rect(sp.x - size / 2f, Screen.height - sp.y - size / 2f, size, size), Texture2D.whiteTexture);
            }
            GUI.color = prev;
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
