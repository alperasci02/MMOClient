// NetClient.WorldView.cs — Faz 2 (İstemci sağlığı) gün 3 dilimi.
// NetClient.cs'ten çıkarılan varlık/görsel yönetimi: sunucu snapshot'ını sahne
// nesnelerine uygulama, prefab seçimi, silah/zırh görselleri, yüzen yazılar,
// renk/vuruş-flaşı boyama. Alan tanımları (cubes, targetPos, floaters, vb.)
// bilinçli olarak NetClient.cs'te bırakıldı — partial class olduğu için erişim
// sorunu yok; bu dosya sadece metodları barındırıyor. Davranış birebir korunuyor.
using System.Collections.Generic;
using UnityEngine;

namespace MMO
{
    public partial class NetClient
    {
        // Faz 2 gün 6: (yeniden) bağlanış sonrası eski varlıkları temizler — rejoin resync.
        // Sunucu zaten taze envanter/ekipman/mastery/vb. gönderir; burada sadece bu client'ın
        // SAHNEDEKİ eski GameObject'lerini ve id-eşlemelerini atıyoruz ki bir sonraki snapshot
        // her şeyi sıfırdan doğru kursun (eski/yetim nesne veya yanlış id eşleşmesi kalmasın).
        void ClearWorldState()
        {
            foreach (var kv in cubes) if (kv.Value != null) Destroy(kv.Value);
            cubes.Clear();
            targetPos.Clear();
            prevHp.Clear();
            hitFlash.Clear();
            baseCol.Clear();
            usePrefab.Clear();
            anims.Clear();
            hitPunch.Clear();     // Faz 5 his
            baseScale.Clear();
            sparks.Clear();
            deathVeil = 0f;
            lastEnts = null;
            myId = 0;
            prevMyHp = -1;
            if (weaponVisual != null) { Destroy(weaponVisual); weaponVisual = null; }
            if (armorVisual != null) { Destroy(armorVisual); armorVisual = null; }
            playerAnim = null;
        }

        void SpawnFloat(float x, float y, float z, string txt, Color c, float size = 16f)
        {
            floaters.Add(new FloatText
            {
                Pos = new Vector3(x, y, z), Text = txt, Col = c, Life = 1.1f,
                Size = size, VX = Random.Range(-0.5f, 0.5f) // hafif yatay kayma (üst üste binmesin)
            });
        }

        // Asset entegrasyonu: Resources/Entities/<key> prefab'ı varsa onu kullan, yoksa primitive (fallback).
        // Kullanıcı şu isimlerle prefab koyar -> oyun otomatik gerçek modelleri kullanır:
        //   Assets/Resources/Entities/player, enemy, boss, node, portal, corpse
        static GameObject EntityPrefab(byte kind, bool isBoss, byte subKind)
        {
            string key;
            if (kind == Protocol.KindMob)
            {
                if (isBoss) key = "boss";                              // dragon
                else if (subKind == Protocol.SubWolf) key = "mob_wolf"; // kurt (Animals)
                else if (subKind == Protocol.SubSlime) key = "mob_slime";
                else if (subKind == Protocol.SubBat) key = "mob_bat";
                else key = "enemy";                                    // iskelet/haydut (varsayılan)
            }
            else if (kind == Protocol.KindResource)
            {
                // kaynak türüne göre görsel: kaya/kütük/bitki/balık/kristal (sunucu subKind)
                key = subKind == 1 ? "node_wood"
                    : subKind == 2 ? "node_herb"
                    : subKind == 3 ? "node_fish"
                    : subKind == 4 ? "node_mithril"
                    : "node_ore";
            }
            else key = kind == Protocol.KindPortal ? "portal"
                : kind == Protocol.KindCorpse ? "corpse"
                : "player";
            var pf = Resources.Load<GameObject>("Entities/" + key);
            if (pf == null && kind == Protocol.KindMob)
                pf = Resources.Load<GameObject>("Entities/enemy"); // alt-tür prefabı yoksa iskelete düş
            if (pf == null && kind == Protocol.KindResource)
                pf = Resources.Load<GameObject>("Entities/node"); // tür prefabı yoksa genel kayaya düş
            if (pf != null && pf.GetComponentInChildren<Renderer>() == null)
                return null; // bozuk/boş prefab -> primitive'e düş (görünmez varlık olmasın)
            return pf;
        }

        // Sınıfa göre elde gerçek silah modeli (blade/bow/staff) — Resources/Entities/weapon_<sınıf>.
        // Silahsızsa (unarmed) hiçbir görsel takılmaz. playerAnim yoksa (henüz doğmadıysa) sessizce çıkar.
        void UpdateWeaponVisual()
        {
            if (weaponVisual != null) { Destroy(weaponVisual); weaponVisual = null; }
            if (playerAnim == null) return;
            var hand = playerAnim.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand == null) return; // model humanoid değil (fallback primitive) -> silah takılamaz
            // Önce kuşanılan gerçek silah defID'sinin modeli (tier ilerlemesi), yoksa sınıf yedeği.
            GameObject pf = null;
            if (!string.IsNullOrEmpty(equippedWeapon))
                pf = Resources.Load<GameObject>("Entities/weapon_" + equippedWeapon);
            if (pf == null)
            {
                string key = masteryClass == "blade" ? "weapon_blade"
                    : masteryClass == "bow" ? "weapon_bow"
                    : masteryClass == "staff" ? "weapon_staff"
                    : null;
                if (key == null) return; // unarmed
                pf = Resources.Load<GameObject>("Entities/" + key);
            }
            if (pf == null) return;
            weaponVisual = Instantiate(pf, hand);
            weaponVisual.transform.localPosition = Vector3.zero;
            weaponVisual.transform.localRotation = Quaternion.identity;
        }

        // Sunucudan gelen armorDef id'sine göre (armor_leather/plate/mithril) gövdeye gerçek zırh
        // takar — Albion tarzı "görünür gear" hissi. Zırh yoksa/tanınmıyorsa görsel kalkar.
        void UpdateArmorVisual()
        {
            if (armorVisual != null) { Destroy(armorVisual); armorVisual = null; }
            if (playerAnim == null || string.IsNullOrEmpty(equippedArmor)) return;
            var chest = playerAnim.GetBoneTransform(HumanBodyBones.Chest) ?? playerAnim.GetBoneTransform(HumanBodyBones.Spine);
            if (chest == null) return;
            var pf = Resources.Load<GameObject>("Entities/gear_" + equippedArmor);
            if (pf == null) return; // T1/T2 gibi görseli olmayan zırhlarda sessizce atla
            armorVisual = Instantiate(pf, chest);
            armorVisual.transform.localPosition = Vector3.zero;
            armorVisual.transform.localRotation = Quaternion.identity;
        }

        void ApplySnapshot(List<Protocol.Entity> ents)
        {
            var seen = new HashSet<ulong>();
            foreach (var e in ents)
            {
                seen.Add(e.Id);
                bool isMob = e.Kind == Protocol.KindMob;
                bool isNode = e.Kind == Protocol.KindResource;
                bool isPortal = e.Kind == Protocol.KindPortal;
                bool isCorpse = e.Kind == Protocol.KindCorpse;
                bool isNew = false;
                if (e.Id == myId)
                {
                    // Faz 1 (Foundation): ölüp (hp<=0) yeniden hp>0 olma geçişini tespit et -> kısa mesaj.
                    if (prevMyHp != -1 && prevMyHp <= 0 && e.Hp > 0)
                    {
                        respawnMsg = "Yeniden doğdun.";
                        respawnMsgTimer = 2.5f;
                        TriggerRespawnFx(); // Faz 5 his: diriliş flaşı
                    }
                    prevMyHp = e.Hp;
                    myHp = e.Hp;
                }
                if (!cubes.TryGetValue(e.Id, out var go))
                {
                    isNew = true;
                    bool isBoss = isMob && e.MaxHp >= 300;
                    var pf = EntityPrefab(e.Kind, isBoss, e.SubKind);
                    bool pfUsed = pf != null;
                    if (pfUsed)
                    {
                        go = Instantiate(pf);
                        // tıklama hedefleme için collider yoksa ekle
                        if (go.GetComponentInChildren<Collider>() == null)
                        {
                            var cc = go.AddComponent<CapsuleCollider>();
                            cc.height = 2f; cc.radius = 0.5f; cc.center = new Vector3(0, 1f, 0);
                        }
                    }
                    else
                    {
                        go = GameObject.CreatePrimitive(isMob || isCorpse ? PrimitiveType.Cube
                            : (isNode || isPortal) ? PrimitiveType.Cylinder : PrimitiveType.Capsule);
                    }
                    usePrefab[e.Id] = pfUsed;
                    // boss için özel prefab yoksa (enemy fallback) bir kez büyüt; gerçek boss.prefab zaten büyük
                    if (pfUsed && isBoss && pf.name != "boss") go.transform.localScale = Vector3.one * 2.2f;
                    // Animator kur (oyuncu + mob + diğer oyuncular): controller yoksa player_controller yükle
                    var an = go.GetComponentInChildren<Animator>();
                    if (an != null)
                    {
                        if (an.runtimeAnimatorController == null)
                        {
                            var rc = Resources.Load<RuntimeAnimatorController>("Entities/player_controller");
                            if (rc != null) an.runtimeAnimatorController = rc;
                            else an = null; // controller yok -> animasyon sürme (uyarı basma)
                        }
                        if (an != null) { an.applyRootMotion = false; anims[e.Id] = an; }
                    }
                    if (e.Id == myId) { playerAnim = an; UpdateWeaponVisual(); UpdateArmorVisual(); } // model geç doğduysa gear'ı senkronla
                    go.name = (isMob ? "Mob_" : isNode ? "Node_" : isPortal ? "Portal_" : isCorpse ? "Corpse_" : "Player_") + e.Id;
                    Color col = isCorpse ? new Color(0.55f, 0.1f, 0.1f)
                        : isPortal ? new Color(0.3f, 0.85f, 1f)
                        : isNode ? new Color(0.9f, 0.75f, 0.2f)
                        : isBoss ? new Color(0.5f, 0f, 0.12f)
                        : isMob ? new Color(0.6f, 0.2f, 0.85f)
                        : (e.Id == myId ? Color.green : (partyIds.Contains(e.Id) ? new Color(0.3f, 0.6f, 1f) : new Color(0.9f, 0.3f, 0.3f)));
                    if (!pfUsed) SetColor(go, col); // gerçek modelin kendi materyali korunur
                    cubes[e.Id] = go;
                    baseCol[e.Id] = col;
                }
                bool pref = usePrefab.TryGetValue(e.Id, out var _pf) && _pf;

                float yOff;
                if (pref)
                {
                    yOff = 0f; // gerçek model: kendi ölçeği/pivotu (ayak hizası)
                }
                else if (isMob)
                {
                    float maxHp = e.MaxHp > 0 ? e.MaxHp : 50f;
                    float s = 0.6f + 0.9f * Mathf.Clamp01((float)e.Hp / maxHp); // can azaldıkça küçülür
                    if (e.MaxHp >= 300) s *= 2.4f; // boss daha büyük
                    go.transform.localScale = new Vector3(s, s, s);
                    yOff = s * 0.5f;
                }
                else if (isNode)
                {
                    float maxC = e.MaxHp > 0 ? e.MaxHp : 5f;
                    float s = 0.6f + 0.7f * Mathf.Clamp01((float)e.Hp / maxC); // şarj azaldıkça küçülür
                    go.transform.localScale = new Vector3(s, s, s);
                    yOff = s; // silindir 2 birim -> merkez y = s
                }
                else if (isPortal)
                {
                    go.transform.localScale = new Vector3(1.6f, 1.6f, 1.6f); // geniş, uzun ışık sütunu
                    yOff = 1.6f;
                }
                else if (isCorpse)
                {
                    go.transform.localScale = new Vector3(1.1f, 0.4f, 1.1f); // yassı yağma çantası
                    yOff = 0.2f;
                }
                else
                {
                    yOff = 1f; // kapsül 2 birim boyunda -> merkez y=1
                }
                var target = new Vector3(e.X, yOff, e.Y);
                targetPos[e.Id] = target;               // her kare yumuşakça gidilecek hedef (akıcılık)
                if (isNew) go.transform.position = target; // yeni varlık origin'den kaymasın

                // juice: HP değişiminden yüzen hasar/iyileşme + vuruş flaşı
                if (isMob || e.Kind == Protocol.KindPlayer)
                {
                    int ph;
                    if (prevHp.TryGetValue(e.Id, out ph) && e.Hp != ph)
                    {
                        int delta = e.Hp - ph;
                        if (delta < 0)
                        {
                            int dmg = -delta;
                            bool big = dmg >= 25; // büyük vuruş: daha iri + altın + "!"
                            float fsize = 16f + Mathf.Clamp(dmg * 0.5f, 0f, 24f);
                            Color dcol = big ? new Color(1f, 0.82f, 0.25f) : new Color(1f, 0.55f, 0.15f);
                            SpawnFloat(e.X, yOff + 1.4f, e.Y, "-" + dmg + (big ? "!" : ""), dcol, fsize);
                            SpawnSpark(e.X, yOff + 1f, e.Y, big ? new Color(1f, 0.9f, 0.5f) : new Color(1f, 0.85f, 0.7f)); // Faz 5 his: darbe kıvılcımı
                            hitFlash[e.Id] = Time.time + 0.18f;
                            PlayRandomSfx(hitClips, 0.5f);
                            HitJuice(e.Id, dmg, e.Id == myId, e.Id == attackTarget); // Faz 5 his: tokat + sarsıntı + hit-stop
                        }
                        else if (e.Id == myId) SpawnFloat(e.X, yOff + 1.4f, e.Y, "+" + delta, new Color(0.4f, 1f, 0.4f), 16f);
                    }
                    prevHp[e.Id] = e.Hp;
                }

                // base renk (oyuncular parti-duyarlı; prefab mob/oyuncu ton alır) + vuruş flaşı.
                // Kendi karakterin (prefab) orijinal görünümünde kalır — sadece flaş yer.
                {
                    Color bc;
                    if (e.Kind == Protocol.KindPlayer && e.Id != myId)
                        bc = partyIds.Contains(e.Id) ? new Color(0.3f, 0.6f, 1f) : new Color(0.9f, 0.3f, 0.3f);
                    else
                        bc = baseCol.TryGetValue(e.Id, out var existing) ? existing : Color.gray;
                    baseCol[e.Id] = bc;
                    bool selfPrefab = pref && e.Id == myId;
                    float fend;
                    bool flashing = hitFlash.TryGetValue(e.Id, out fend) && Time.time < fend;
                    if (flashing)
                    {
                        float t = (fend - Time.time) / 0.18f;
                        // kendin: hasar alınca KIRMIZI flaş (beyaz taban); diğerleri: beyaza flaş
                        SetColor(go, selfPrefab ? Color.Lerp(Color.white, new Color(1f, 0.35f, 0.35f), t)
                                                : Color.Lerp(bc, Color.white, t));
                    }
                    else if (!selfPrefab)
                        SetColor(go, bc);
                    else if (hitFlash.Remove(e.Id))
                        SetColor(go, Color.white); // flaş bitti -> varsayılan görünüm
                }

                // Faz 5 his: tokat-ölçeği için taban ölçeği sakla (tokat yokken; prefab'te bake-in olmasın)
                if (!hitPunch.ContainsKey(e.Id)) baseScale[e.Id] = go.transform.localScale;
            }

            var remove = new List<ulong>();
            foreach (var kv in cubes) if (!seen.Contains(kv.Key)) remove.Add(kv.Key);
            foreach (var id in remove) { Destroy(cubes[id]); cubes.Remove(id); prevHp.Remove(id); baseCol.Remove(id); hitFlash.Remove(id); targetPos.Remove(id); usePrefab.Remove(id); anims.Remove(id); hitPunch.Remove(id); baseScale.Remove(id); }
        }

        static void SetColor(GameObject go, Color c)
        {
            // FBX modellerde renderer alt objelerdedir -> hepsini boya
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", c);
                else r.material.color = c;
            }
        }
    }
}
