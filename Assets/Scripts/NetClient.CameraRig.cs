// NetClient.CameraRig.cs — Faz 2 (İstemci sağlığı) gün 4 dilimi.
// NetClient.cs'ten çıkarılan kamera takibi + gün/gece döngüsü + bölge görsel
// ortamı (ambient/sis/dekor). Davranış birebir korunuyor; sadece taşındı.
using UnityEngine;

namespace MMO
{
    public partial class NetClient
    {
        // Bölge görsel ortamı: meadow=doğa+pazar, forest=karanlık orman, dungeon-N=dekorlu oda.
        // Ambiyans (ışık/sis) da bölgeye göre değişir.
        GameObject dungeonEnv;
        void UpdateDungeonEnv()
        {
            if (dungeonEnv != null) { Destroy(dungeonEnv); dungeonEnv = null; }
            if (zoneId == null) return;
            var root = new GameObject("ZoneEnv");
            void Add(string res)
            {
                var pf = Resources.Load<GameObject>("Entities/" + res);
                if (pf != null) Instantiate(pf, Vector3.zero, Quaternion.identity, root.transform);
            }
            if (zoneId.StartsWith("dungeon-"))
            {
                int n = 0; int.TryParse(zoneId.Substring(8), out n);
                Add("dungeon_env_" + (n % 3));
                // Faz 3 (gün 12-14): "Buz Kuyusu" aynı modüler zindan geometrisini kullanır,
                // yalnızca soğuk ışık/sis profiliyle ayrışır (yeni 3D varlık yok — bkz. D-15).
                if (zoneName == "Buz Kuyusu")
                {
                    RenderSettings.ambientLight = new Color(0.55f, 0.62f, 0.72f); // soğuk mavi-beyaz
                    RenderSettings.fogColor = new Color(0.65f, 0.78f, 0.88f);     // buzlu, açık sis
                    RenderSettings.fogStartDistance = 10f; RenderSettings.fogEndDistance = 40f;
                }
                else // Kaçakçı Mağarası (eski "Haydut İni")
                {
                    RenderSettings.ambientLight = new Color(0.30f, 0.27f, 0.24f); // loş zindan
                    RenderSettings.fogColor = new Color(0.05f, 0.04f, 0.05f);
                    RenderSettings.fogStartDistance = 18f; RenderSettings.fogEndDistance = 55f;
                }
            }
            else if (zoneId == "forest")
            {
                Add("nature_forest");
                RenderSettings.ambientLight = new Color(0.30f, 0.36f, 0.34f); // karanlık orman
                RenderSettings.fogColor = new Color(0.06f, 0.10f, 0.08f);
                RenderSettings.fogStartDistance = 25f; RenderSettings.fogEndDistance = 80f;
            }
            else if (zoneId == "coast")
            {
                Add("nature_coast");
                RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.66f); // parlak deniz havası
                RenderSettings.fogColor = new Color(0.35f, 0.48f, 0.60f);     // açık mavi pus
                RenderSettings.fogStartDistance = 60f; RenderSettings.fogEndDistance = 160f;
            }
            else // meadow (başlangıç): doğa + pazar meydanı
            {
                Add("nature_meadow");
                Add("market_meadow");
                RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.58f);
                RenderSettings.fogColor = new Color(0.12f, 0.14f, 0.18f);
                RenderSettings.fogStartDistance = 55f; RenderSettings.fogEndDistance = 140f;
            }
            dungeonEnv = root;
            zoneBaseAmbient = RenderSettings.ambientLight; // Faz J: gün/gece bu tabanı ölçekler
        }

        // Faz J: bölgenin taban ambient'i (gün/gece çarpanı bunun üstünde çalışır)
        Color zoneBaseAmbient = new Color(0.5f, 0.52f, 0.58f);

        // Faz J: sunucu saatine göre güneş açısı/yoğunluk/ambient (yumuşak geçiş, iç mekânda kapalı)
        void UpdateDayNight()
        {
            if (sunLight == null) return;
            bool interior = zoneId != null && zoneId.StartsWith("dungeon-");
            float hour = worldMinute / 60f;
            // gündüz 6-20 arası: güneş yükselir/alçalır; gece: ufkun altı + düşük ışık
            float dayFrac = Mathf.Clamp01((hour - 6f) / 14f);
            float elev = Mathf.Sin(dayFrac * Mathf.PI) * 65f;
            bool night = hour < 6f || hour >= 20f;
            float targetIntensity = interior ? 0.55f : (night ? 0.18f : Mathf.Lerp(0.35f, 1.15f, Mathf.Sin(dayFrac * Mathf.PI)));
            float targetElev = night ? -20f : Mathf.Max(8f, elev);
            // gün doğumu/batımında sıcak turuncu ton
            Color dayCol = new Color(1f, 0.96f, 0.84f);
            Color duskCol = new Color(1f, 0.62f, 0.36f);
            Color nightCol = new Color(0.55f, 0.62f, 0.9f);
            float duskness = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Sin(dayFrac * Mathf.PI)) * 2f);
            Color targetCol = night ? nightCol : Color.Lerp(dayCol, duskCol, duskness);

            float k = 1f - Mathf.Exp(-1.5f * Time.deltaTime); // yumuşak geçiş
            sunLight.intensity = Mathf.Lerp(sunLight.intensity, targetIntensity, k);
            sunLight.color = Color.Lerp(sunLight.color, targetCol, k);
            var rot = Quaternion.Euler(targetElev, -35f, 0f);
            sunLight.transform.rotation = Quaternion.Slerp(sunLight.transform.rotation, rot, k);
            if (!interior)
            {
                float ambScale = night ? 0.35f : Mathf.Lerp(0.5f, 1f, Mathf.Sin(dayFrac * Mathf.PI));
                RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, zoneBaseAmbient * ambScale, k);
            }
        }

        // Kamera oyuncuyu yumuşakça takip eder (sabit kamera yerine — büyük his farkı)
        void LateUpdate()
        {
            UpdateDayNight(); // Faz J: gün/gece aydınlatması
            var cam = Camera.main;
            if (cam == null || !cubes.TryGetValue(myId, out var me)) return;
            Vector3 want = me.transform.position + new Vector3(0f, 16f, -13f);
            float camK = 1f - Mathf.Exp(-10f * Time.deltaTime); // kare-hızından bağımsız, takılmasız
            cam.transform.position = Vector3.Lerp(cam.transform.position, want, camK);
            cam.transform.position += CameraShakeOffset(); // Faz 5 his: kamera sarsıntısı (söner)
            cam.transform.rotation = Quaternion.Euler(52f, 0f, 0f);
        }
    }
}
