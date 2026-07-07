using UnityEngine;

namespace MMO
{
    // FAZ J (Constitution Part 9): Ambient yaşam — dekoratif hayvan/NPC gezinmesi.
    // SADECE GÖRSEL: oynanışa etkisi yok, o yüzden client-side güvenli (sunucu-yetkili kural
    // yalnız oynanış varlıkları için). Doğum noktası etrafında rastgele dolaşır, durur, otlar.
    public class AmbientWander : MonoBehaviour
    {
        [Tooltip("Doğum noktasından en fazla bu kadar uzaklaşır")]
        public float radius = 4f;
        [Tooltip("Hareket hızı (m/sn)")]
        public float speed = 0.8f;

        Vector3 origin;
        Vector3 target;
        float waitUntil;

        void Start()
        {
            origin = transform.position;
            target = origin;
            // sürüyü senkron bozmak için rastgele başlangıç beklemesi
            waitUntil = Time.time + Random.Range(0f, 4f);
        }

        void Update()
        {
            if (Time.time < waitUntil) return;

            Vector3 flat = target - transform.position; flat.y = 0f;
            if (flat.magnitude < 0.15f)
            {
                // hedefe vardı -> otla/dur (2-6 sn), sonra yeni hedef seç
                waitUntil = Time.time + Random.Range(2f, 6f);
                Vector2 r = Random.insideUnitCircle * radius;
                target = origin + new Vector3(r.x, 0f, r.y);
                return;
            }
            // hedefe yürü + yönüne dön (yumuşak)
            Vector3 dir = flat.normalized;
            transform.position += dir * speed * Time.deltaTime;
            var want = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, 4f * Time.deltaTime);
        }
    }
}
