// NetClient.cs — sunucuya bağlan, hareket et, dövüş, loot, kalıcılık.
// Kontroller (PC) — SADECE oynanış tuşları; menüler fareyle alt araç çubuğundan açılır:
//   Sol tık boş yere -> oraya yürü            WASD  -> elle hareket
//   Sol tık mob'a    -> yaklaş + otomatik saldır   SPACE -> en yakın mob'a saldır
//   1/2/3            -> yetenekler (bara tıklayarak da) ESC -> menü/duraklat, Enter -> sohbet
// Menüler (Çanta/Karakter/Üretim/Pazar/Lonca/Meslek/Görev/Sıralama/Parti/Sohbet/Tamir):
//   ekranın altındaki araç çubuğundan tıklanarak — tuş ezberi yok (Faz 5, bkz. NetClient.UISkin.cs).
// Görsel: oyuncular kapsül (yeşil=sen, kırmızı=diğerleri), mob'lar mor küp (vurdukça küçülür).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MMO
{
    public partial class NetClient : MonoBehaviour
    {
        [Tooltip("Karakter ismi — aynı isimle girince altının/karakterin kayıtlı kalır")]
        public string playerName = "Kahraman";
        [Tooltip("Sunucu WebSocket adresi")]
        public string url = "ws://localhost:8080/ws";
        [Tooltip("Saniyede kaç hareket niyeti gönderilsin")]
        public float sendRate = 15f;
        [Tooltip("Bağlanamazsa en fazla kaç kez tekrar denensin (0 = sonsuz) — Faz 1 (Foundation)")]
        public int maxConnectRetries = 0;
        [Tooltip("Yeniden deneme aralığı üst sınırı (saniye) — üstel artar, bu tavana kadar")]
        public float maxRetryDelaySec = 8f;

        const float MobMaxHp = 50f;
        const float AttackReach = 2.7f; // toplama/mezar yakın menzili (sunucu 3.0)
        float myReach = 2.7f;           // silaha göre saldırı menzili (yay/asa uzun) — Faz 23

        NetConnection net; // Faz 2 (İstemci sağlığı) gün 1: soket/retry/reconnect NetConnection.cs'e taşındı
        int lastSeenConnectGen = 0; // Faz 2 gün 6: rejoin resync tespiti (bkz. ClearWorldState)

        ulong myId = 0;
        readonly Dictionary<ulong, GameObject> cubes = new Dictionary<ulong, GameObject>();
        List<Protocol.Entity> lastEnts;
        float sendTimer;

        long myGold = 0;
        long lastReward = 0;
        float rewardPopupTimer = 0f;
        int myHp = 100;
        int myMaxHp = 100;
        int myMana = 50;      // Faz D
        int myMaxMana = 50;
        int myLevel = 1;
        long myXp = 0;
        long myXpNext = 100;

        int myDamage = 10;
        string equippedWeapon = "";
        string equippedArmor = "";

        // Faz A: karakter attribute'ları + karakter sayfası (K tuşu)
        Protocol.AttrData attrData;
        bool hasAttr = false;
        bool showCharSheet = false;

        // Faz C: silah ustalığı uzmanlık detayı (karakter sayfasında gösterilir)
        Protocol.MasteryDetail masteryDetail;
        bool hasMasteryDetail = false;

        // Faz E: mob status effect göstergesi stili
        GUIStyle statusStyle;

        // Faz F: enchant bilgisi (karakter sayfasında büyüleme)
        Protocol.EnchantInfo enchantInfo;
        bool hasEnchant = false;

        // Faz J: gün/gece döngüsü — sunucudan gelen dünya saati (dakika 0..1439)
        int worldMinute = 12 * 60; // öğlen varsayılan
        Light sunLight;

        // Faz K: hizip itibarları (karakter sayfası)
        List<Protocol.RepRow> repList = new List<Protocol.RepRow>();

        // RomanGM/StatusTag: bkz. NetClient.HUD.cs (Faz 2 gün 5)

        // Faz 10: silah ustalığı (aktif sınıf)
        string masteryClass = "unarmed";
        int masteryLevel = 0;
        long masteryXp = 0;
        long masteryXpNext = 60;
        int masteryBonus = 0;
        float masteryPopupTimer = 0f;
        int masteryPopupLevel = 0;

        // Faz 11: açık dünya bölgeleri
        string zoneId = "meadow";
        string zoneName = "Başlangıç Çayırı";
        float zonePopupTimer = 0f;

        // Faz 13: durabilite + eşya gücü
        int weaponDur = 0, weaponMaxDur = 0;
        int armorDur = 0, armorMaxDur = 0;
        int itemPower = 0;
        float repairMsgTimer = 0f;
        string repairMsg = "";

        // Faz 14: mezar / tam-loot ölüm
        ulong corpseTarget = 0;
        bool corpseRequested = false;
        float deathMsgTimer = 0f;
        string deathMsg = "";

        // Faz 15: parti
        List<Protocol.PartyMember> partyMembers = new List<Protocol.PartyMember>();
        HashSet<ulong> partyIds = new HashSet<ulong>();
        string pendingInviteFrom = "";
        float pendingInviteTimer = 0f;
        string partyMsg = "";
        float partyMsgTimer = 0f;

        // Faz 18: genel bildirim toast
        string noticeMsg = "";
        float noticeTimer = 0f;

        // Faz 21: yetenekler
        List<Protocol.AbilityC> abilities = new List<Protocol.AbilityC>();
        float[] abilityReadyAt = new float[8]; // index başına yerel cooldown bitiş zamanı

        // Faz 24: görevler
        List<Protocol.QuestC> quests = new List<Protocol.QuestC>();
        bool showQuests = false;

        // Faz 26: chat
        readonly List<string> chatLog = new List<string>();
        bool chatOpen = false;
        string chatInput = "";

        // Faz 27: leaderboard
        List<Protocol.LeaderRow> leaderboard = new List<Protocol.LeaderRow>();
        bool showLeader = false;

        // Görsel/juice: yüzen yazı, vuruş flaşı, can çubuğu için durum
        struct FloatText { public Vector3 Pos; public string Text; public Color Col; public float Life; public float Size; public float VX; }
        readonly List<FloatText> floaters = new List<FloatText>();
        readonly Dictionary<ulong, int> prevHp = new Dictionary<ulong, int>();
        readonly Dictionary<ulong, float> hitFlash = new Dictionary<ulong, float>();
        readonly Dictionary<ulong, Color> baseCol = new Dictionary<ulong, Color>();
        readonly Dictionary<ulong, Vector3> targetPos = new Dictionary<ulong, Vector3>(); // akıcı hareket hedefi

        // SpawnFloat/EntityPrefab: bkz. NetClient.WorldView.cs (Faz 2 gün 3)
        // Asset entegrasyonu: Resources/Entities/<key> prefab'ı varsa onu kullan, yoksa primitive (fallback).
        // Kullanıcı şu isimlerle prefab koyar -> oyun otomatik gerçek modelleri kullanır:
        //   Assets/Resources/Entities/player, enemy, boss, node, portal, corpse
        readonly Dictionary<ulong, bool> usePrefab = new Dictionary<ulong, bool>();
        Animator playerAnim; // oyuncu modelinin Animator'ı (kuruluysa) — yürüme/saldırı animasyonu için
        GameObject weaponVisual; // sağ ele takılı gerçek silah modeli (sınıfa göre değişir)
        GameObject armorVisual;  // göğüs kemiğine takılı gerçek zırh modeli (tier'a göre değişir)
        readonly Dictionary<ulong, Animator> anims = new Dictionary<ulong, Animator>(); // tüm varlıkların Animator'ları (mob dahil)

        // Faz 17: meslek ustalığı (Destiny Board)
        struct ProfState { public int Level; public long Xp; public long XpNext; public int Bonus; }
        Dictionary<string, ProfState> professions = new Dictionary<string, ProfState>();
        bool showProf = false;

        // Faz 16: lonca
        bool showGuild = false;
        bool hasGuild = false;
        string guildName = "";
        bool guildIsLeader = false;
        long guildBankGold = 0;
        List<Protocol.GuildMemberC> guildMembers = new List<Protocol.GuildMemberC>();
        List<Protocol.GuildBankItem> guildBank = new List<Protocol.GuildBankItem>();
        string guildNameInput = "Loncam";
        string guildGoldInput = "100";

        List<Protocol.InvItem> inventory = new List<Protocol.InvItem>();
        bool showInventory = false;
        List<Protocol.RecipeInfo> recipes = new List<Protocol.RecipeInfo>();
        bool showCrafting = false;
        List<Protocol.MarketEntry> market = new List<Protocol.MarketEntry>();
        bool showMarket = false;
        string sellPriceStr = "25";
        string lootName = "";
        byte lootRarity = 0;
        int lootQty = 0;
        float lootPopupTimer = 0f;

        Vector2? moveTarget = null;  // tıkla-yürü hedefi (sunucu x,y düzleminde)
        ulong attackTarget = 0;      // tıklanan mob (menzile girince saldır)
        ulong gatherTarget = 0;      // tıklanan kaynak düğümü (menzile girince topla)
        bool gatherRequested = false;
        float attackCooldown = 0f;

        // Faz 1 (Foundation): isim ekranı + pause
        bool nameScreenActive = true;
        string nameInput = "Kahraman";
        string serverAddressInput = ""; // Faz 2 gün 6: LAN sunucu adresi (isim ekranında düzenlenebilir)
        bool paused = false;
        int prevMyHp = -1;        // -1 = henüz görülmedi (ilk snapshot'ta yanlış "yeniden doğdun" göstermesin)
        string respawnMsg = "";
        float respawnMsgTimer = 0f;

        // Faz 1 (Foundation) gün 6-7: ses paketi #1 (Kenney, CC0 — bkz. Assets/Resources/Audio/CREDITS.txt)
        AudioSource sfx;
        AudioClip[] footstepClips;
        AudioClip[] hitClips;
        AudioClip deathClip, lootClip, levelUpClip, uiClickClip;
        float footstepTimer = 0f;

        void LoadAudio()
        {
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f; // 2D — basit yerel his (ilk ses geçişi; 3D pan/mesafe sonraki cila)
            footstepClips = LoadClips("Audio/footstep_grass_00", "Audio/footstep_grass_01",
                "Audio/footstep_grass_02", "Audio/footstep_grass_03", "Audio/footstep_grass_04");
            hitClips = LoadClips("Audio/hit_00", "Audio/hit_01", "Audio/hit_02");
            deathClip = Resources.Load<AudioClip>("Audio/death");
            lootClip = Resources.Load<AudioClip>("Audio/loot");
            levelUpClip = Resources.Load<AudioClip>("Audio/levelup");
            uiClickClip = Resources.Load<AudioClip>("Audio/ui_click");
        }

        static AudioClip[] LoadClips(params string[] paths)
        {
            var list = new List<AudioClip>();
            foreach (var p in paths) { var c = Resources.Load<AudioClip>(p); if (c != null) list.Add(c); }
            return list.ToArray();
        }

        void PlaySfx(AudioClip clip, float vol = 1f)
        {
            if (clip != null && sfx != null) sfx.PlayOneShot(clip, vol);
        }

        void PlayRandomSfx(AudioClip[] clips, float vol = 1f)
        {
            if (clips != null && clips.Length > 0) PlaySfx(clips[UnityEngine.Random.Range(0, clips.Length)], vol);
        }

        void Start()
        {
            SetupScene();
            LoadAudio();
            LoadSettings(); // Faz 5: kalıcı ayarlar (sarsıntı gücü, ses) — bkz. NetClient.Settings.cs
            nameInput = string.IsNullOrEmpty(playerName) ? "Kahraman" : playerName;
            // Faz 2 gün 6: son kullanılan sunucu adresini hatırla (LAN'da arkadaşının IP'sini
            // her seferinde yeniden yazmasın). Inspector'daki `url` ilk-kurulum varsayılanı.
            serverAddressInput = PlayerPrefs.GetString(ServerAddressPrefKey, url);
            // net, StartConnecting()'te (adres onaylanınca) kurulur — bkz. aşağıda.
        }

        // İsim ekranından çağrılır: kalıcılık-modu kontrolünü ve bağlantı döngüsünü başlatır.
        const string ServerAddressPrefKey = "mmo_server_url";
        void StartConnecting()
        {
            nameScreenActive = false;
            string trimmed = nameInput == null ? "" : nameInput.Trim();
            playerName = trimmed.Length == 0 ? "Kahraman" : trimmed;

            string addr = serverAddressInput == null ? "" : serverAddressInput.Trim();
            url = addr.Length == 0 ? url : addr; // boşsa Inspector varsayılanında kal
            PlayerPrefs.SetString(ServerAddressPrefKey, url);

            net = new NetConnection(this, url, maxConnectRetries, maxRetryDelaySec);
            net.Begin(playerName);
        }

        void Update()
        {
            // İsim ekranındayken (henüz "Oyna"ya basılmadıysa) oyun mantığı çalışmaz.
            if (nameScreenActive) return;

            // Faz 2 gün 6: (yeniden) bağlanış tespiti -> eski varlık/görsel durumunu temizle
            // (rejoin resync — sunucu zaten taze envanter/ekipman/vb. gönderecek).
            if (net.ConnectGeneration != lastSeenConnectGen)
            {
                lastSeenConnectGen = net.ConnectGeneration;
                ClearWorldState();
            }

            // 1) gelen mesajlar (Faz 2 gün 2: dispatch NetClient.ProtocolDispatch.cs'e taşındı)
            ProcessInboundMessages();

            if (rewardPopupTimer > 0f) rewardPopupTimer -= Time.deltaTime;
            if (lootPopupTimer > 0f) lootPopupTimer -= Time.deltaTime;
            if (masteryPopupTimer > 0f) masteryPopupTimer -= Time.deltaTime;
            if (zonePopupTimer > 0f) zonePopupTimer -= Time.deltaTime;
            if (repairMsgTimer > 0f) repairMsgTimer -= Time.deltaTime;
            if (deathMsgTimer > 0f) deathMsgTimer -= Time.deltaTime;
            if (noticeTimer > 0f) noticeTimer -= Time.deltaTime;
            if (respawnMsgTimer > 0f) respawnMsgTimer -= Time.deltaTime;

            TickJuice(Time.deltaTime); // Faz 5 his: flaş/level-fx/tokat zamanlayıcıları

            // yüzen hasar yazıları yüksel + yana kay + söndür
            for (int i = floaters.Count - 1; i >= 0; i--)
            {
                var f = floaters[i];
                f.Life -= Time.deltaTime;
                f.Pos.y += Time.deltaTime * 1.6f;
                f.Pos.x += f.VX * Time.deltaTime;
                floaters[i] = f;
                if (f.Life <= 0f) floaters.RemoveAt(i);
            }

            // varlıkları her karede hedefe yumuşakça taşı (ağ 20Hz -> akıcı görüntü) + hareket
            // yönüne döndür + hareket hızından animasyon (mob dahil: yürüyen herkes yürür)
            // Faz 5 his: hit-stop sırasında görsel interpolasyon donar (moveK=0); sends etkilenmez.
            float moveK = HitStopped ? 0f : (1f - Mathf.Exp(-14f * Time.deltaTime));
            foreach (var kv in cubes)
            {
                if (!targetPos.TryGetValue(kv.Key, out var tp)) continue;
                var tr = kv.Value.transform;
                Vector3 flat = new Vector3(tp.x - tr.position.x, 0f, tp.z - tr.position.z);
                tr.position = Vector3.Lerp(tr.position, tp, moveK);
                if (flat.sqrMagnitude > 0.0009f) // anlamlı hareket -> modeli yönüne çevir
                    tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.LookRotation(flat), moveK);
                ApplyPunchScale(kv.Key, tr); // Faz 5 his: vuruş ölçek-tokatı
                if (anims.TryGetValue(kv.Key, out var av) && av != null && kv.Key != myId)
                    av.SetFloat("Speed", Mathf.Clamp01(flat.magnitude * 2.5f)); // hedefe uzaklık ~ hız
            }
            if (pendingInviteTimer > 0f) pendingInviteTimer -= Time.deltaTime;
            if (partyMsgTimer > 0f) partyMsgTimer -= Time.deltaTime;
            if (attackCooldown > 0f) attackCooldown -= Time.deltaTime;

            // Faz 1 (Foundation): ESC -> açık panel varsa onu kapat, yoksa duraklat/devam et.
            var kbEsc = Keyboard.current;
            if (kbEsc != null && kbEsc.escapeKey.wasPressedThisFrame)
            {
                if (showSettings) { showSettings = false; SaveSettings(); }
                else if (showInventory || showCrafting || showCharSheet || showMarket || showGuild || showProf || showQuests || showLeader)
                    showInventory = showCrafting = showCharSheet = showMarket = showGuild = showProf = showQuests = showLeader = false;
                else
                    paused = !paused;
            }
            if (paused)
            {
                if (net.SocketOpen) net.Outbound.Enqueue(Protocol.EncodeMove(0, 0));
                return;
            }

            if (!net.SocketOpen) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            // Faz 26: chat aç/gönder (Enter). Açıkken diğer girdiler yok sayılır.
            if (kb != null && kb.enterKey.wasPressedThisFrame)
            {
                if (chatOpen) { SendChat(); chatOpen = false; }
                else { chatOpen = true; chatInput = ""; }
            }
            if (chatOpen)
            {
                net.Outbound.Enqueue(Protocol.EncodeMove(0, 0)); // yazarken dur
                return;
            }

            // Faz 5 (Sunum): menüler artık TUŞLA açılmıyor — hepsi alt araç çubuğundan
            // (DrawToolbar, bkz. NetClient.UISkin.cs) ve panel-içi butonlardan fareyle
            // açılır/işletilir: Çanta / Karakter / Üretim / Pazar / Lonca / Meslek / Görev /
            // Sıralama / Parti (davet) / Sohbet / Tamir / Menü. Parti kabul/reddet ve ayrıl
            // da panel butonlarında. ESC hâlâ açık paneli kapatır / duraklatır, Enter hâlâ
            // sohbet açar (evrensel kısayollar). Oynanış tuşları (WASD/SPACE/1-2-3/fare)
            // menü değildir — aynen korundu.

            // 2) sol tık -> yürü / mob'a yürü+saldır
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                HandleClick(mouse.position.ReadValue());

            // 3) SPACE -> en yakın mob'a saldır
            if (kb != null && kb.spaceKey.wasPressedThisFrame)
                AttackNearestMob();

            // 3b) yetenekler 1/2/3 (Faz 21)
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) UseAbility(0);
                if (kb.digit2Key.wasPressedThisFrame) UseAbility(1);
                if (kb.digit3Key.wasPressedThisFrame) UseAbility(2);
            }

            // 4) hareket yönünü belirle: WASD öncelikli, yoksa tıkla-yürü hedefi
            Vector2 dir = Vector2.zero;
            var wasd = ReadInput(kb);
            if (wasd != Vector2.zero)
            {
                dir = wasd;
                moveTarget = null;
                attackTarget = 0;
                gatherTarget = 0; gatherRequested = false;
                corpseTarget = 0; corpseRequested = false;
            }
            else if (moveTarget.HasValue && cubes.TryGetValue(myId, out var meGo))
            {
                var me2 = new Vector2(meGo.transform.position.x, meGo.transform.position.z);
                var to = moveTarget.Value - me2;
                if (to.magnitude > 0.3f) dir = to.normalized;
                else moveTarget = null; // vardık
            }

            // Faz 1 (Foundation) gün 6-7: hareket ederken periyodik ayak sesi
            if (dir.sqrMagnitude > 0.01f)
            {
                footstepTimer -= Time.deltaTime;
                if (footstepTimer <= 0f) { PlayRandomSfx(footstepClips, 0.4f); footstepTimer = 0.38f; }
            }

            // 5) hedef mob menzildeyse otomatik saldır
            if (attackTarget != 0 && attackCooldown <= 0f && InRangeOfMob(attackTarget))
            {
                net.Outbound.Enqueue(Protocol.EncodeAttack(attackTarget));
                if (playerAnim != null) playerAnim.SetTrigger("Attack");
                attackCooldown = 0.3f;
            }

            // 5b) hedef kaynak düğümü menzildeyse topla
            if (gatherTarget != 0)
            {
                bool nodeFound = false; Vector2 nodePos = Vector2.zero;
                if (lastEnts != null)
                    foreach (var e in lastEnts)
                        if (e.Id == gatherTarget && e.Kind == Protocol.KindResource) { nodeFound = true; nodePos = new Vector2(e.X, e.Y); break; }
                if (!nodeFound) { gatherTarget = 0; gatherRequested = false; }
                else if (cubes.TryGetValue(myId, out var meNode))
                {
                    var meP = new Vector2(meNode.transform.position.x, meNode.transform.position.z);
                    if (Vector2.Distance(meP, nodePos) <= AttackReach)
                    {
                        moveTarget = null; // dur ve topla (hareket toplamayı iptal eder)
                        if (!gatherRequested) { net.Outbound.Enqueue(Protocol.EncodeGather(gatherTarget)); gatherRequested = true; }
                    }
                }
            }

            // 5c) hedef mezar menzildeyse yağmala (Faz 14)
            if (corpseTarget != 0)
            {
                bool found = false; Vector2 cPos = Vector2.zero;
                if (lastEnts != null)
                    foreach (var e in lastEnts)
                        if (e.Id == corpseTarget && e.Kind == Protocol.KindCorpse) { found = true; cPos = new Vector2(e.X, e.Y); break; }
                if (!found) { corpseTarget = 0; corpseRequested = false; }
                else if (cubes.TryGetValue(myId, out var meC))
                {
                    var meP = new Vector2(meC.transform.position.x, meC.transform.position.z);
                    if (Vector2.Distance(meP, cPos) <= AttackReach)
                    {
                        moveTarget = null;
                        if (!corpseRequested) { net.Outbound.Enqueue(Protocol.EncodeLootCorpse(corpseTarget)); corpseRequested = true; }
                    }
                }
            }

            // 6) hareket niyetini yolla (throttle)
            sendTimer += Time.deltaTime;
            if (sendTimer >= 1f / sendRate)
            {
                sendTimer = 0f;
                net.Outbound.Enqueue(Protocol.EncodeMove(dir.x, dir.y));
            }

            // animasyon: yürüme hızını Animator'a bildir (kurulunca otomatik yürür; yoksa no-op)
            if (playerAnim != null) playerAnim.SetFloat("Speed", Mathf.Clamp01(dir.magnitude));
        }

        Vector2 ReadInput(Keyboard kb)
        {
            float x = 0, y = 0;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1;
            }
            return new Vector2(x, y);
        }

        void HandleClick(Vector2 screenPos)
        {
            if (PointerOverUI(screenPos)) return; // UI (araç çubuğu/panel/yetenek) üstüne tıklama dünyaya gitmesin
            var cam = Camera.main;
            if (cam == null) return;
            if (!Physics.Raycast(cam.ScreenPointToRay(screenPos), out var hit, 1000f)) return;

            var n = hit.collider.transform.root.gameObject.name; // prefab'te collider alt-objede olabilir -> kök isim
            moveTarget = new Vector2(hit.point.x, hit.point.z); // her durumda oraya yürü
            attackTarget = 0; gatherTarget = 0; gatherRequested = false; corpseTarget = 0; corpseRequested = false;
            if (n.StartsWith("Mob_") && ulong.TryParse(n.Substring(4), out var mobId))
                attackTarget = mobId; // mob -> menzilde saldır
            else if (n.StartsWith("Node_") && ulong.TryParse(n.Substring(5), out var nodeId))
                gatherTarget = nodeId; // kaynak -> menzilde topla
            else if (n.StartsWith("Corpse_") && ulong.TryParse(n.Substring(7), out var corpseId))
                corpseTarget = corpseId; // mezar -> menzilde yağmala
        }

        bool InRangeOfMob(ulong mobId)
        {
            if (lastEnts == null || !cubes.TryGetValue(myId, out var meGo)) return false;
            var me2 = new Vector2(meGo.transform.position.x, meGo.transform.position.z);
            foreach (var e in lastEnts)
                if (e.Id == mobId && e.Kind == Protocol.KindMob)
                    return Vector2.Distance(me2, new Vector2(e.X, e.Y)) <= myReach; // Faz 23: silaha göre menzil
            attackTarget = 0; // mob yok (öldü) -> hedefi bırak
            return false;
        }

        void InviteNearest()
        {
            if (lastEnts == null) return;
            Vector2 me = Vector2.zero; bool haveMe = false;
            foreach (var e in lastEnts) if (e.Id == myId) { me = new Vector2(e.X, e.Y); haveMe = true; break; }
            if (!haveMe) return;
            ulong best = 0; float bestD = float.MaxValue;
            foreach (var e in lastEnts)
            {
                if (e.Kind != Protocol.KindPlayer || e.Id == myId) continue;
                float d = Vector2.Distance(me, new Vector2(e.X, e.Y));
                if (d < bestD) { bestD = d; best = e.Id; }
            }
            if (best != 0)
            {
                net.Outbound.Enqueue(Protocol.EncodePartyInvite(best));
                partyMsg = "Davet gönderildi"; partyMsgTimer = 2.5f;
            }
            else { partyMsg = "Yakında oyuncu yok"; partyMsgTimer = 2.5f; }
        }

        // UpdateDungeonEnv/UpdateDayNight: bkz. NetClient.CameraRig.cs (Faz 2 gün 4)

        void SendChat()
        {
            string t = chatInput == null ? "" : chatInput.Trim();
            chatInput = "";
            if (t.Length == 0) return;
            byte scope = 0; // bölge
            if (t.StartsWith("/g ")) { scope = 1; t = t.Substring(3); } // global
            if (t.Length > 0) net.Outbound.Enqueue(Protocol.EncodeChat(scope, t));
        }

        void UseAbility(int idx)
        {
            if (idx >= abilities.Count) return;
            if (Time.time < abilityReadyAt[idx]) { noticeMsg = abilities[idx].Name + " hazır değil"; noticeTimer = 1.5f; return; }
            var ab = abilities[idx];
            ulong target = 0;
            if (ab.Type != 2) // 2 = self-heal; diğerleri hedef ister
            {
                target = attackTarget != 0 ? attackTarget : NearestMobId();
                if (target == 0) { noticeMsg = "Hedef yok"; noticeTimer = 1.5f; return; }
            }
            net.Outbound.Enqueue(Protocol.EncodeAbility((byte)idx, target));
            if (playerAnim != null)
            {
                // yeteneğe özel animasyon: heal=Buff (savaş narası), 1.yetenek=Attack2 (combo), 2.=Attack3 (360 dönüş)
                string trig = ab.Type == 2 ? "Buff" : (idx == 0 ? "Attack2" : "Attack3");
                if (HasParam(playerAnim, trig)) playerAnim.SetTrigger(trig);
                else playerAnim.SetTrigger("Attack"); // eski controller'la geriye uyumlu
            }
            abilityReadyAt[idx] = Time.time + ab.CooldownMs / 1000f; // yerel cooldown (sunucu otoriter)
        }

        // UpdateWeaponVisual/UpdateArmorVisual: bkz. NetClient.WorldView.cs (Faz 2 gün 3)

        static bool HasParam(Animator an, string name)
        {
            foreach (var p in an.parameters) if (p.name == name) return true;
            return false;
        }

        ulong NearestMobId()
        {
            if (lastEnts == null) return 0;
            Vector2 me = Vector2.zero; bool haveMe = false;
            foreach (var e in lastEnts) if (e.Id == myId) { me = new Vector2(e.X, e.Y); haveMe = true; break; }
            if (!haveMe) return 0;
            ulong best = 0; float bestD = float.MaxValue;
            foreach (var e in lastEnts)
            {
                if (e.Kind != Protocol.KindMob) continue;
                float d = Vector2.Distance(me, new Vector2(e.X, e.Y));
                if (d < bestD) { bestD = d; best = e.Id; }
            }
            return best;
        }

        void AttackNearestMob()
        {
            if (lastEnts == null) return;
            Vector2 me = Vector2.zero;
            bool haveMe = false;
            foreach (var e in lastEnts)
                if (e.Id == myId) { me = new Vector2(e.X, e.Y); haveMe = true; break; }
            if (!haveMe) return;

            ulong best = 0;
            float bestD = float.MaxValue;
            foreach (var e in lastEnts)
            {
                if (e.Kind != Protocol.KindMob) continue;
                float d = Vector2.Distance(me, new Vector2(e.X, e.Y));
                if (d < bestD) { bestD = d; best = e.Id; }
            }
            if (best != 0) { net.Outbound.Enqueue(Protocol.EncodeAttack(best)); if (playerAnim != null) playerAnim.SetTrigger("Attack"); }
        }

        // ApplySnapshot: bkz. NetClient.WorldView.cs (Faz 2 gün 3)

        void SetupScene()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(8, 1, 8);
            SetColor(ground, new Color(0.16f, 0.30f, 0.15f)); // canlı çim tonu (gerçek çim modelleri üstüne oturur)

            // Güneş (yönlü ışık) + yumuşak gölge — derinlik hissi
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.84f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            sunLight = sun; // Faz J: gün/gece döngüsü bu ışığı sürer

            RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.58f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.12f, 0.14f, 0.18f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 55f;  // sis çok uzakta -> dünya görünür kalır
            RenderSettings.fogEndDistance = 140f;

            if (Camera.main != null)
            {
                Camera.main.transform.position = new Vector3(0, 16, -13);
                Camera.main.transform.rotation = Quaternion.Euler(52, 0, 0);
                Camera.main.backgroundColor = new Color(0.07f, 0.08f, 0.11f);
            }
        }

        // LateUpdate (kamera takibi): bkz. NetClient.CameraRig.cs (Faz 2 gün 4)

        // SetColor: bkz. NetClient.WorldView.cs (Faz 2 gün 3)

        // OnGUI + Draw*/format yardımcıları: bkz. NetClient.HUD.cs (Faz 2 gün 5)

        async void OnDestroy()
        {
            if (net != null) await net.CloseGracefully();
        }
    }
}
