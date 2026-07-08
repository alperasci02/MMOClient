// NetClient.cs — sunucuya bağlan, hareket et, dövüş, loot, kalıcılık.
// Kontroller (PC):
//   Sol tık boş yere -> oraya yürü
//   Sol tık mob'a    -> yanına yürü ve menzile girince otomatik saldır
//   WASD             -> elle hareket
//   SPACE            -> en yakın mob'a saldır
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

        // Faz G: GM rütbesi -> Roma rakamı
        static string RomanGM(int n)
        {
            switch (n) { case 1: return "I"; case 2: return "II"; case 3: return "III"; case 4: return "IV"; case 5: return "V"; default: return n.ToString(); }
        }

        // Faz E: status kodu -> etiket + renk
        static void StatusTag(byte code, out string txt, out Color col)
        {
            switch (code)
            {
                case 1: txt = "Yanma"; col = new Color(1f, 0.5f, 0.15f); break;
                case 2: txt = "Zehir"; col = new Color(0.5f, 0.9f, 0.3f); break;
                case 3: txt = "Kanama"; col = new Color(1f, 0.25f, 0.25f); break;
                case 4: txt = "Sersem"; col = new Color(1f, 0.9f, 0.3f); break;
                case 5: txt = "Donma"; col = new Color(0.5f, 0.85f, 1f); break;
                case 6: txt = "Yavas"; col = new Color(0.7f, 0.7f, 1f); break;
                default: txt = ""; col = Color.white; break;
            }
        }

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
        struct FloatText { public Vector3 Pos; public string Text; public Color Col; public float Life; }
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
            net = new NetConnection(this, url, maxConnectRetries, maxRetryDelaySec);
            nameInput = string.IsNullOrEmpty(playerName) ? "Kahraman" : playerName;
            // Bağlantı, isim ekranında oyuncu "Oyna"ya basınca başlar (bkz. StartConnecting).
        }

        // İsim ekranından çağrılır: kalıcılık-modu kontrolünü ve bağlantı döngüsünü başlatır.
        void StartConnecting()
        {
            nameScreenActive = false;
            string trimmed = nameInput == null ? "" : nameInput.Trim();
            playerName = trimmed.Length == 0 ? "Kahraman" : trimmed;
            net.Begin(playerName);
        }

        void Update()
        {
            // İsim ekranındayken (henüz "Oyna"ya basılmadıysa) oyun mantığı çalışmaz.
            if (nameScreenActive) return;

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

            // yüzen hasar yazıları yüksel + söndür
            for (int i = floaters.Count - 1; i >= 0; i--)
            {
                var f = floaters[i];
                f.Life -= Time.deltaTime;
                f.Pos.y += Time.deltaTime * 1.6f;
                floaters[i] = f;
                if (f.Life <= 0f) floaters.RemoveAt(i);
            }

            // varlıkları her karede hedefe yumuşakça taşı (ağ 20Hz -> akıcı görüntü) + hareket
            // yönüne döndür + hareket hızından animasyon (mob dahil: yürüyen herkes yürür)
            float moveK = 1f - Mathf.Exp(-14f * Time.deltaTime);
            foreach (var kv in cubes)
            {
                if (!targetPos.TryGetValue(kv.Key, out var tp)) continue;
                var tr = kv.Value.transform;
                Vector3 flat = new Vector3(tp.x - tr.position.x, 0f, tp.z - tr.position.z);
                tr.position = Vector3.Lerp(tr.position, tp, moveK);
                if (flat.sqrMagnitude > 0.0009f) // anlamlı hareket -> modeli yönüne çevir
                    tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.LookRotation(flat), moveK);
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
                if (showInventory || showCrafting || showCharSheet || showMarket || showGuild || showProf || showQuests || showLeader)
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

            if (kb != null && kb.iKey.wasPressedThisFrame) showInventory = !showInventory;
            if (kb != null && kb.cKey.wasPressedThisFrame) showCrafting = !showCrafting;
            if (kb != null && kb.kKey.wasPressedThisFrame) showCharSheet = !showCharSheet; // Faz A: karakter sayfası
            if (kb != null && kb.mKey.wasPressedThisFrame) { showMarket = !showMarket; if (showMarket) net.Outbound.Enqueue(Protocol.EncodeMarketBrowse()); }
            if (kb != null && kb.gKey.wasPressedThisFrame) { showGuild = !showGuild; if (showGuild) net.Outbound.Enqueue(Protocol.EncodeGuildInfoReq()); }
            if (kb != null && kb.jKey.wasPressedThisFrame) showProf = !showProf;
            if (kb != null && kb.tKey.wasPressedThisFrame) { showLeader = !showLeader; if (showLeader) net.Outbound.Enqueue(Protocol.EncodeLeaderReq()); }
            if (kb != null && kb.qKey.wasPressedThisFrame) { showQuests = !showQuests; if (showQuests) net.Outbound.Enqueue(Protocol.EncodeQuestList()); }
            if (kb != null && kb.rKey.wasPressedThisFrame)
            {
                if (zoneId == "meadow") { net.Outbound.Enqueue(Protocol.EncodeRepair()); repairMsg = "Tamir ediliyor..."; }
                else repairMsg = "Tamir sadece güvenli bölgede (Başlangıç Çayırı)";
                repairMsgTimer = 2.5f;
            }
            // Faz 15: parti — P davet, Y kabul, L ayrıl
            if (kb != null && kb.pKey.wasPressedThisFrame) InviteNearest();
            if (kb != null && kb.yKey.wasPressedThisFrame && pendingInviteTimer > 0f)
            {
                net.Outbound.Enqueue(Protocol.EncodePartyAccept());
                pendingInviteTimer = 0f; pendingInviteFrom = "";
                partyMsg = "Partiye katıldın"; partyMsgTimer = 2.5f;
            }
            if (kb != null && kb.lKey.wasPressedThisFrame && partyMembers.Count > 0)
            {
                net.Outbound.Enqueue(Protocol.EncodePartyLeave());
                partyMsg = "Partiden ayrıldın"; partyMsgTimer = 2.5f;
            }

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
                RenderSettings.ambientLight = new Color(0.30f, 0.27f, 0.24f); // loş zindan
                RenderSettings.fogColor = new Color(0.05f, 0.04f, 0.05f);
                RenderSettings.fogStartDistance = 18f; RenderSettings.fogEndDistance = 55f;
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

        // Kamera oyuncuyu yumuşakça takip eder (sabit kamera yerine — büyük his farkı)
        void LateUpdate()
        {
            UpdateDayNight(); // Faz J: gün/gece aydınlatması
            var cam = Camera.main;
            if (cam == null || !cubes.TryGetValue(myId, out var me)) return;
            Vector3 want = me.transform.position + new Vector3(0f, 16f, -13f);
            float camK = 1f - Mathf.Exp(-10f * Time.deltaTime); // kare-hızından bağımsız, takılmasız
            cam.transform.position = Vector3.Lerp(cam.transform.position, want, camK);
            cam.transform.rotation = Quaternion.Euler(52f, 0f, 0f);
        }

        // SetColor: bkz. NetClient.WorldView.cs (Faz 2 gün 3)

        void OnGUI()
        {
            // Faz 1 (Foundation): isim ekranı / bağlanma ekranı — geri kalan HUD henüz çizilmez.
            if (nameScreenActive) { DrawNameScreen(); return; }
            if (!net.IsConnected) { DrawConnectingScreen(); return; }

            // A-05: sunucu bellek modundaysa (DB'siz) ilerleme kaydedilmediğini belirgin göster.
            if (net.DbModeChecked && !net.DbPersistent)
            {
                var warn = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                warn.normal.textColor = new Color(1f, 0.3f, 0.3f);
                var wprev = GUI.color;
                GUI.color = new Color(0.3f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, 22), Texture2D.whiteTexture);
                GUI.color = wprev;
                GUI.Label(new Rect(0, 2, Screen.width, 20), "⚠ BELLEK MODU — ilerleme KAYDEDİLMİYOR (sunucu DB'ye bağlı değil)", warn);
            }

            int lowHp = Mathf.RoundToInt(myMaxHp * 0.3f);
            // az canlıyken kırmızı ekran uyarısı
            if (myHp > 0 && myHp < lowHp)
            {
                var prev = GUI.color;
                GUI.color = new Color(1f, 0f, 0f, 0.18f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = prev;
            }

            // --- dünya-uzayı: baş üstü can çubukları + yüzen hasar yazıları ---
            var wcam = Camera.main;
            if (wcam != null)
            {
                if (lastEnts != null)
                {
                    foreach (var e in lastEnts)
                    {
                        if (!(e.Kind == Protocol.KindMob || e.Kind == Protocol.KindPlayer) || e.MaxHp <= 0) continue;
                        float top = (e.Kind == Protocol.KindMob && e.MaxHp >= 300) ? 4f : 2.3f;
                        Vector3 sp = wcam.WorldToScreenPoint(new Vector3(e.X, top, e.Y));
                        if (sp.z <= 0f) continue;
                        float bw = 42f, bh = 5f, bx = sp.x - bw / 2f, by = Screen.height - sp.y;
                        float frac = Mathf.Clamp01((float)e.Hp / e.MaxHp);
                        var pc = GUI.color;
                        GUI.color = new Color(0f, 0f, 0f, 0.65f);
                        GUI.DrawTexture(new Rect(bx - 1, by - 1, bw + 2, bh + 2), Texture2D.whiteTexture);
                        GUI.color = e.Id == myId ? new Color(0.35f, 1f, 0.35f)
                            : e.Kind == Protocol.KindPlayer ? new Color(0.4f, 0.7f, 1f)
                            : new Color(1f, 0.35f, 0.3f);
                        GUI.DrawTexture(new Rect(bx, by, bw * frac, bh), Texture2D.whiteTexture);
                        GUI.color = pc;
                        // Faz E: status effect göstergesi (can çubuğunun üstünde renkli etiket)
                        if (e.Kind == Protocol.KindMob && e.Status != 0)
                        {
                            if (statusStyle == null)
                                statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                            StatusTag(e.Status, out string stxt, out Color scol);
                            statusStyle.normal.textColor = scol;
                            GUI.Label(new Rect(bx - 20, by - 18, bw + 40, 16), stxt, statusStyle);
                        }
                    }
                }
                var fst = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                foreach (var f in floaters)
                {
                    Vector3 sp = wcam.WorldToScreenPoint(f.Pos);
                    if (sp.z <= 0f) continue;
                    var c = f.Col; c.a = Mathf.Clamp01(f.Life);
                    fst.normal.textColor = c;
                    GUI.Label(new Rect(sp.x - 40f, Screen.height - sp.y - 14f, 80f, 22f), f.Text, fst);
                }
            }

            var gold = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            gold.normal.textColor = new Color(1f, 0.85f, 0.2f);
            GUI.Label(new Rect(15, 12, 500, 40), playerName + "  —  Altın: " + myGold, gold);

            // Faz 11: mevcut bölge (üst orta)
            var zn = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            zn.normal.textColor = new Color(0.55f, 0.9f, 1f);
            GUI.Label(new Rect(0, 12, Screen.width, 28), "◆ " + zoneName + " ◆", zn);
            // Faz J: dünya saati (bölge adının altında)
            var wt = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperCenter };
            bool nightNow = worldMinute < 6 * 60 || worldMinute >= 20 * 60;
            wt.normal.textColor = nightNow ? new Color(0.6f, 0.7f, 1f) : new Color(1f, 0.9f, 0.5f);
            GUI.Label(new Rect(0, 38, Screen.width, 20),
                (nightNow ? "☾ " : "☀ ") + (worldMinute / 60).ToString("00") + ":" + (worldMinute % 60).ToString("00"), wt);

            if (zonePopupTimer > 0f)
            {
                var zp = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                zp.normal.textColor = new Color(0.5f, 0.9f, 1f, Mathf.Clamp01(zonePopupTimer));
                GUI.Label(new Rect(0, Screen.height * 0.32f, Screen.width, 50), zoneName, zp);
            }

            if (deathMsgTimer > 0f)
            {
                var dp = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                dp.normal.textColor = new Color(1f, 0.25f, 0.25f, Mathf.Clamp01(deathMsgTimer));
                GUI.Label(new Rect(0, Screen.height * 0.42f, Screen.width, 50), deathMsg, dp);
            }
            else if (respawnMsgTimer > 0f)
            {
                var rsp = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                rsp.normal.textColor = new Color(0.4f, 1f, 0.5f, Mathf.Clamp01(respawnMsgTimer));
                GUI.Label(new Rect(0, Screen.height * 0.42f, Screen.width, 50), respawnMsg, rsp);
            }

            if (noticeTimer > 0f)
            {
                var nt = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                nt.normal.textColor = new Color(1f, 0.8f, 0.3f, Mathf.Clamp01(noticeTimer));
                GUI.Label(new Rect(0, Screen.height * 0.55f, Screen.width, 28), noticeMsg, nt);
            }

            var lvl = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            lvl.normal.textColor = new Color(0.6f, 0.8f, 1f);
            GUI.Label(new Rect(15, 42, 500, 30), "Seviye " + myLevel + "   XP " + myXp + " / " + myXpNext, lvl);

            var hp = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            hp.normal.textColor = myHp > lowHp ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.3f, 0.3f);
            GUI.Label(new Rect(15, 68, 170, 30), "Can: " + myHp + " / " + myMaxHp, hp);

            // Faz D: mana (mavi)
            var mana = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            mana.normal.textColor = new Color(0.45f, 0.65f, 1f);
            GUI.Label(new Rect(15, 94, 170, 28), "Mana: " + myMana + " / " + myMaxMana, mana);

            var dmg = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            dmg.normal.textColor = new Color(1f, 0.7f, 0.4f);
            GUI.Label(new Rect(195, 68, 300, 30), "Hasar: " + myDamage, dmg);

            // Faz 10: aktif silah sınıfı ustalığı
            var mst = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            mst.normal.textColor = new Color(0.85f, 0.7f, 1f);
            string mstTxt = ClassName(masteryClass) + " Ustalığı  Sv " + masteryLevel + "  (+" + masteryBonus + " hasar)";
            if (masteryXpNext > 0) mstTxt += "   " + masteryXp + " / " + masteryXpNext;
            else mstTxt += "   (MAX)";
            GUI.Label(new Rect(15, 96, 600, 28), mstTxt, mst);

            if (masteryPopupTimer > 0f)
            {
                var mp = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                mp.normal.textColor = new Color(0.8f, 0.6f, 1f);
                GUI.Label(new Rect(0, 174, Screen.width, 34), ClassName(masteryClass) + " Ustalığı " + masteryPopupLevel + "!", mp);
            }

            // Faz 13: eşya gücü + durabilite (kırıksa kırmızı)
            var gear = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            bool wBroken = weaponMaxDur > 0 && weaponDur <= 0;
            bool aBroken = armorMaxDur > 0 && armorDur <= 0;
            gear.normal.textColor = (wBroken || aBroken) ? new Color(1f, 0.35f, 0.35f) : new Color(0.75f, 0.85f, 0.95f);
            string wTxt = weaponMaxDur > 0 ? ("  Silah " + weaponDur + "/" + weaponMaxDur + (wBroken ? " KIRIK!" : "")) : "";
            string aTxt = armorMaxDur > 0 ? ("  Zırh " + armorDur + "/" + armorMaxDur + (aBroken ? " KIRIK!" : "")) : "";
            GUI.Label(new Rect(15, 120, 700, 26), "Eşya Gücü " + itemPower + wTxt + aTxt + "   (R: tamir)", gear);

            if (repairMsgTimer > 0f)
            {
                var rs = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                rs.normal.textColor = new Color(1f, 0.9f, 0.4f);
                GUI.Label(new Rect(0, 200, Screen.width, 28), repairMsg, rs);
            }

            if (rewardPopupTimer > 0f)
            {
                var pop = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
                pop.normal.textColor = new Color(0.3f, 1f, 0.3f);
                GUI.Label(new Rect(360, 64, 300, 34), "+" + lastReward + " altın!", pop);
            }

            if (lootPopupTimer > 0f)
            {
                var ls = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                ls.normal.textColor = RarityColor(lootRarity);
                GUI.Label(new Rect(0, 60, Screen.width, 30), "Buldun: +" + lootQty + " " + lootName, ls);
            }

            if (gatherTarget != 0)
            {
                var gs = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                gs.normal.textColor = new Color(0.95f, 0.85f, 0.35f);
                GUI.Label(new Rect(0, 88, Screen.width, 30), "Toplanıyor...", gs);
            }

            if (showInventory)
            {
                float w = 320;
                int rows = Mathf.Max(inventory.Count, 1);
                float h = 58 + rows * 24 + 8;
                float px = Screen.width - w - 20, py = 20;
                var prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.78f);
                GUI.DrawTexture(new Rect(px, py, w, h), Texture2D.whiteTexture);
                GUI.color = prev;

                var title = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
                title.normal.textColor = Color.white;
                GUI.Label(new Rect(px + 12, py + 6, w - 20, 24), "Çanta (" + inventory.Count + ")  —  tıkla=tak", title);

                var lbl = new GUIStyle(GUI.skin.label) { fontSize = 12 };
                lbl.normal.textColor = Color.white;
                GUI.Label(new Rect(px + 12, py + 32, 120, 20), "Satış fiyatı (1 adet):", lbl);
                sellPriceStr = GUI.TextField(new Rect(px + 135, py + 31, 70, 20), sellPriceStr, 9);

                if (inventory.Count == 0)
                {
                    lbl.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
                    GUI.Label(new Rect(px + 14, py + 58, w - 24, 20), "(boş)", lbl);
                }
                var btn = new GUIStyle(GUI.skin.button) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
                var sbtn = new GUIStyle(GUI.skin.button) { fontSize = 12 };
                for (int i = 0; i < inventory.Count; i++)
                {
                    var it = inventory[i];
                    bool equipped = (it.DefID == equippedWeapon || it.DefID == equippedArmor);
                    btn.normal.textColor = RarityColor(it.Rarity);
                    btn.hover.textColor = RarityColor(it.Rarity);
                    float ry = py + 56 + i * 24;
                    string label = (equipped ? "★ " : "") + it.Name + "   x" + it.Qty;
                    if (GUI.Button(new Rect(px + 8, ry, w - 86, 22), label))
                        net.Outbound.Enqueue(Protocol.EncodeEquip(it.DefID));
                    if (GUI.Button(new Rect(px + w - 74, ry, 66, 22), "Sat", sbtn))
                    {
                        long pr; long.TryParse(sellPriceStr, out pr);
                        if (pr > 0) net.Outbound.Enqueue(Protocol.EncodeMarketList(it.DefID, 1, pr));
                    }
                }
            }

            if (showCrafting)
            {
                float cw = 360;
                int cn = Mathf.Max(recipes.Count, 1);
                float ch = 34 + cn * 26 + 8;
                float cx = 15, cy = 175;
                var cprev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.8f);
                GUI.DrawTexture(new Rect(cx, cy, cw, ch), Texture2D.whiteTexture);
                GUI.color = cprev;

                var ctitle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                ctitle.normal.textColor = Color.white;
                GUI.Label(new Rect(cx + 10, cy + 6, cw - 20, 22), "Üretim (C)  —  malzeme varsa tıkla", ctitle);

                var cbtn = new GUIStyle(GUI.skin.button) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
                for (int i = 0; i < recipes.Count; i++)
                {
                    var r = recipes[i];
                    string inp = "";
                    for (int j = 0; j < r.Inputs.Count; j++) { if (j > 0) inp += ", "; inp += r.Inputs[j].Name + " x" + r.Inputs[j].Qty; }
                    cbtn.normal.textColor = RarityColor(r.OutRarity);
                    cbtn.hover.textColor = RarityColor(r.OutRarity);
                    if (GUI.Button(new Rect(cx + 8, cy + 30 + i * 26, cw - 16, 24), r.OutName + "  <-  " + inp))
                        net.Outbound.Enqueue(Protocol.EncodeCraft(r.Id));
                }
            }

            if (showMarket)
            {
                float mw = 470;
                int mn = Mathf.Max(market.Count, 1);
                float mh = 34 + mn * 24 + 8;
                float mx = Screen.width / 2f - mw / 2f, my = 60;
                var mprev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                GUI.DrawTexture(new Rect(mx, my, mw, mh), Texture2D.whiteTexture);
                GUI.color = mprev;

                var mtitle = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                mtitle.normal.textColor = Color.white;
                GUI.Label(new Rect(mx + 10, my + 6, mw - 20, 22), "Pazar (M)  —  satın almak için tıkla", mtitle);

                if (market.Count == 0)
                {
                    var em = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                    em.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
                    GUI.Label(new Rect(mx + 14, my + 32, mw - 24, 20), "(ilan yok — bir şey sat!)", em);
                }
                var mbtn = new GUIStyle(GUI.skin.button) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
                for (int i = 0; i < market.Count; i++)
                {
                    var e = market[i];
                    mbtn.normal.textColor = RarityColor(e.Rarity);
                    mbtn.hover.textColor = RarityColor(e.Rarity);
                    string label = e.Name + " x" + e.Qty + "   —   " + e.Price + " altın   (" + e.Seller + ")";
                    if (GUI.Button(new Rect(mx + 8, my + 30 + i * 24, mw - 16, 22), label))
                        net.Outbound.Enqueue(Protocol.EncodeMarketBuy(e.Id));
                }
            }

            // Faz 16: lonca paneli (G)
            if (showGuild)
            {
                float gw = 440, gx = Screen.width / 2f - gw / 2f, gy = 50;
                float gh = hasGuild ? (90 + guildMembers.Count * (guildIsLeader ? 24 : 18) + guildBank.Count * 24 + Mathf.Min(inventory.Count, 8) * 24 + 60) : 110;
                var gprev = GUI.color; GUI.color = new Color(0f, 0.05f, 0.1f, 0.9f);
                GUI.DrawTexture(new Rect(gx, gy, gw, gh), Texture2D.whiteTexture); GUI.color = gprev;
                var gt = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                gt.normal.textColor = new Color(0.6f, 0.85f, 1f);
                var gl2 = new GUIStyle(GUI.skin.label) { fontSize = 13 }; gl2.normal.textColor = Color.white;
                var gbtn = new GUIStyle(GUI.skin.button) { fontSize = 12 };

                if (!hasGuild)
                {
                    GUI.Label(new Rect(gx + 12, gy + 6, gw - 20, 22), "LONCA (G)  —  bir loncan yok", gt);
                    GUI.Label(new Rect(gx + 12, gy + 34, 70, 22), "İsim:", gl2);
                    guildNameInput = GUI.TextField(new Rect(gx + 70, gy + 33, 180, 22), guildNameInput, 24);
                    if (GUI.Button(new Rect(gx + 258, gy + 33, 80, 22), "Kur (100)", gbtn))
                        net.Outbound.Enqueue(Protocol.EncodeGuildCreate(guildNameInput));
                    if (GUI.Button(new Rect(gx + 342, gy + 33, 80, 22), "Katıl", gbtn))
                        net.Outbound.Enqueue(Protocol.EncodeGuildJoin(guildNameInput));
                }
                else
                {
                    GUI.Label(new Rect(gx + 12, gy + 6, gw - 20, 22),
                        "LONCA: " + guildName + (guildIsLeader ? " (lider)" : "") + "   —   Kasa: " + guildBankGold + " altın", gt);
                    if (GUI.Button(new Rect(gx + gw - 70, gy + 6, 60, 20), "Ayrıl", gbtn))
                        net.Outbound.Enqueue(Protocol.EncodeGuildLeave());
                    float yy = gy + 30;
                    if (!guildIsLeader)
                    {
                        GUI.Label(new Rect(gx + 12, yy, gw - 20, 18), "Üyeler: " + MembersStr(), gl2); yy += 20;
                    }
                    else
                    {
                        // lider: üye yönetimi (terfi/indir/kov) — kendisi ve liderler hariç
                        var sbtn = new GUIStyle(GUI.skin.button) { fontSize = 10 };
                        for (int i = 0; i < guildMembers.Count; i++)
                        {
                            var m = guildMembers[i];
                            string tag = m.Rank == 2 ? " ★" : (m.Rank == 1 ? " ◆" : "");
                            GUI.Label(new Rect(gx + 12, yy, 200, 20), m.Name + tag, gl2);
                            if (m.Name != playerName && m.Rank != 2)
                            {
                                if (m.Rank == 0 && GUI.Button(new Rect(gx + 215, yy, 62, 20), "Terfi", sbtn)) net.Outbound.Enqueue(Protocol.EncodeGuildPromote(m.Name));
                                if (m.Rank == 1 && GUI.Button(new Rect(gx + 215, yy, 62, 20), "İndir", sbtn)) net.Outbound.Enqueue(Protocol.EncodeGuildDemote(m.Name));
                                if (GUI.Button(new Rect(gx + 281, yy, 50, 20), "Kov", sbtn)) net.Outbound.Enqueue(Protocol.EncodeGuildKick(m.Name));
                            }
                            yy += 22;
                        }
                    }
                    // altın
                    GUI.Label(new Rect(gx + 12, yy, 60, 22), "Altın:", gl2);
                    guildGoldInput = GUI.TextField(new Rect(gx + 64, yy, 80, 22), guildGoldInput, 9);
                    long gamt; long.TryParse(guildGoldInput, out gamt);
                    if (GUI.Button(new Rect(gx + 150, yy, 80, 22), "Yatır", gbtn) && gamt > 0)
                        net.Outbound.Enqueue(Protocol.EncodeGuildDepositGold(gamt));
                    if (GUI.Button(new Rect(gx + 234, yy, 80, 22), "Çek", gbtn) && gamt > 0)
                        net.Outbound.Enqueue(Protocol.EncodeGuildWithdrawGold(gamt));
                    yy += 28;
                    // kasa eşyaları (çek)
                    GUI.Label(new Rect(gx + 12, yy, gw - 20, 18), "Kasa eşyaları (tıkla=çek):", gl2); yy += 20;
                    for (int i = 0; i < guildBank.Count; i++)
                    {
                        var bi = guildBank[i];
                        gbtn.normal.textColor = RarityColor(bi.Rarity); gbtn.hover.textColor = RarityColor(bi.Rarity);
                        if (GUI.Button(new Rect(gx + 16, yy, gw - 32, 22), bi.Name + " x" + bi.Qty))
                            net.Outbound.Enqueue(Protocol.EncodeGuildWithdrawItem(bi.InstID));
                        yy += 24;
                    }
                    gbtn.normal.textColor = Color.white; gbtn.hover.textColor = Color.white;
                    // çantadan yatır
                    yy += 4;
                    GUI.Label(new Rect(gx + 12, yy, gw - 20, 18), "Çantandan yatır (tıkla):", gl2); yy += 20;
                    for (int i = 0; i < inventory.Count && i < 8; i++)
                    {
                        var it = inventory[i];
                        if (GUI.Button(new Rect(gx + 16, yy, gw - 32, 22), it.Name + " x" + it.Qty))
                            net.Outbound.Enqueue(Protocol.EncodeGuildDepositItem(it.DefID, it.Qty));
                        yy += 24;
                    }
                }
            }

            // Faz 17: meslek (Destiny Board) paneli (J)
            if (showProf)
            {
                string[] order = { "mining", "woodcutting", "herbalism", "fishing", "blacksmith", "refinery", "leatherworker", "alchemist" };
                float jw = 380, jx = 20, jy = 175, jh = 36 + order.Length * 22 + 8;
                var jprev = GUI.color; GUI.color = new Color(0.05f, 0.08f, 0f, 0.88f);
                GUI.DrawTexture(new Rect(jx, jy, jw, jh), Texture2D.whiteTexture); GUI.color = jprev;
                var jt = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
                jt.normal.textColor = new Color(0.7f, 1f, 0.6f);
                GUI.Label(new Rect(jx + 10, jy + 6, jw - 20, 22), "MESLEKLER (J)  —  kullandıkça gelişir", jt);
                var jl = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                for (int i = 0; i < order.Length; i++)
                {
                    string key = order[i];
                    ProfState st; professions.TryGetValue(key, out st);
                    bool craft = i >= 4;
                    jl.normal.textColor = st.Level > 0 ? Color.white : new Color(0.55f, 0.55f, 0.55f);
                    string eff = craft ? ("+%" + st.Bonus + " iade") : ("+" + st.Bonus + " verim");
                    string xp = st.XpNext > 0 ? (st.Xp + "/" + st.XpNext) : "MAX";
                    GUI.Label(new Rect(jx + 12, jy + 30 + i * 22, jw - 20, 20),
                        ProfName(key) + "  Sv " + st.Level + "   " + xp + "   " + eff, jl);
                }
            }

            // Faz 24: görev paneli (Q)
            if (showQuests)
            {
                float qw = 460, qx = Screen.width / 2f - qw / 2f, qy = 70;
                float qh = 36 + quests.Count * 30 + 8;
                var qprev = GUI.color; GUI.color = new Color(0.1f, 0.07f, 0f, 0.9f);
                GUI.DrawTexture(new Rect(qx, qy, qw, qh), Texture2D.whiteTexture); GUI.color = qprev;
                var qt = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                qt.normal.textColor = new Color(1f, 0.85f, 0.4f);
                GUI.Label(new Rect(qx + 10, qy + 6, qw - 20, 22), "GÖREVLER (Q)", qt);
                var ql = new GUIStyle(GUI.skin.label) { fontSize = 12 }; ql.normal.textColor = Color.white;
                var qbtn = new GUIStyle(GUI.skin.button) { fontSize = 12 };
                for (int i = 0; i < quests.Count; i++)
                {
                    var q = quests[i];
                    float ry = qy + 32 + i * 30;
                    string suffix = q.State == 1 ? ("  " + q.Progress + "/" + q.Count) : "";
                    ql.normal.textColor = q.State == 3 ? new Color(0.5f, 0.8f, 0.5f) : (q.State == 2 ? new Color(1f, 0.9f, 0.3f) : Color.white);
                    GUI.Label(new Rect(qx + 12, ry, qw - 130, 28), q.Name + suffix, ql);
                    if (q.State == 0) { if (GUI.Button(new Rect(qx + qw - 110, ry, 100, 24), "Kabul Et", qbtn)) net.Outbound.Enqueue(Protocol.EncodeQuestAccept(q.Id)); }
                    else if (q.State == 2) { if (GUI.Button(new Rect(qx + qw - 110, ry, 100, 24), "Ödül Al", qbtn)) net.Outbound.Enqueue(Protocol.EncodeQuestClaim(q.Id)); }
                    else if (q.State == 3) { GUI.Label(new Rect(qx + qw - 110, ry, 100, 24), "✓ Tamam", ql); }
                }
            }

            // Faz 15: parti paneli (sağ-orta) + davet/mesaj
            if (partyMembers.Count > 0)
            {
                var hpById = new Dictionary<ulong, int>();
                if (lastEnts != null) foreach (var e in lastEnts) if (e.Kind == Protocol.KindPlayer) hpById[e.Id] = e.Hp;
                float pw = 230, px = Screen.width - pw - 20, py = 250;
                float ph = 30 + partyMembers.Count * 22 + 8;
                var pprev = GUI.color; GUI.color = new Color(0f, 0f, 0.15f, 0.75f);
                GUI.DrawTexture(new Rect(px, py, pw, ph), Texture2D.whiteTexture); GUI.color = pprev;
                var pt = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
                pt.normal.textColor = new Color(0.4f, 0.7f, 1f);
                GUI.Label(new Rect(px + 10, py + 5, pw - 20, 22), "PARTİ (" + partyMembers.Count + "/3)  —  L: ayrıl", pt);
                var pl = new GUIStyle(GUI.skin.label) { fontSize = 13 };
                pl.normal.textColor = Color.white;
                for (int i = 0; i < partyMembers.Count; i++)
                {
                    var m = partyMembers[i];
                    string hpTxt = hpById.TryGetValue(m.Id, out var h) ? (h + "/" + m.MaxHp) : "—";
                    string self = m.Id == myId ? " (sen)" : "";
                    GUI.Label(new Rect(px + 12, py + 28 + i * 22, pw - 20, 20), m.Name + self + "  Sv" + m.Level + "  " + hpTxt, pl);
                }
            }
            if (pendingInviteTimer > 0f)
            {
                var inv = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                inv.normal.textColor = new Color(0.5f, 1f, 0.6f);
                GUI.Label(new Rect(0, Screen.height - 90, Screen.width, 26), pendingInviteFrom + " seni partiye davet etti  —  Y: kabul et", inv);
            }
            if (partyMsgTimer > 0f)
            {
                var pm2 = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                pm2.normal.textColor = new Color(0.6f, 0.85f, 1f);
                GUI.Label(new Rect(0, Screen.height - 64, Screen.width, 24), partyMsg, pm2);
            }

            // Faz 21: yetenek barı (alt-orta)
            if (abilities.Count > 0)
            {
                float bw = 150, total = abilities.Count * (bw + 8), bx = Screen.width / 2f - total / 2f, by = Screen.height - 56;
                for (int i = 0; i < abilities.Count; i++)
                {
                    float x = bx + i * (bw + 8);
                    bool ready = Time.time >= abilityReadyAt[i];
                    var prev = GUI.color;
                    GUI.color = ready ? new Color(0.15f, 0.2f, 0.35f, 0.9f) : new Color(0.3f, 0.1f, 0.1f, 0.9f);
                    GUI.DrawTexture(new Rect(x, by, bw, 44), Texture2D.whiteTexture);
                    GUI.color = prev;
                    var st = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                    st.normal.textColor = ready ? Color.white : new Color(1f, 0.6f, 0.6f);
                    string cd = ready ? "" : "  (" + Mathf.CeilToInt(abilityReadyAt[i] - Time.time) + "s)";
                    GUI.Label(new Rect(x, by, bw, 44), "[" + (i + 1) + "] " + abilities[i].Name + cd, st);
                }
            }

            // Faz 26: chat log (sol-alt) + giriş kutusu
            var clog = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            clog.normal.textColor = new Color(0.9f, 0.95f, 0.8f);
            for (int i = 0; i < chatLog.Count; i++)
                GUI.Label(new Rect(15, Screen.height - 110 - (chatLog.Count - i) * 16, 600, 16), chatLog[i], clog);
            if (chatOpen)
            {
                GUI.SetNextControlName("chatField");
                var cf = new GUIStyle(GUI.skin.textField) { fontSize = 13 };
                chatInput = GUI.TextField(new Rect(15, Screen.height - 104, 460, 22), chatInput, 200, cf);
                GUI.FocusControl("chatField");
                var hint = new GUIStyle(GUI.skin.label) { fontSize = 11 };
                hint.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
                GUI.Label(new Rect(480, Screen.height - 103, 320, 20), "Enter=gönder  (/g = global)", hint);
            }

            // Faz 27: leaderboard paneli (T)
            if (showLeader)
            {
                float lw = 300, lx = Screen.width / 2f - lw / 2f, ly = 80;
                float lh = 36 + leaderboard.Count * 22 + 8;
                var lprev = GUI.color; GUI.color = new Color(0.05f, 0.05f, 0.1f, 0.92f);
                GUI.DrawTexture(new Rect(lx, ly, lw, lh), Texture2D.whiteTexture); GUI.color = lprev;
                var lt = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
                lt.normal.textColor = new Color(1f, 0.85f, 0.3f);
                GUI.Label(new Rect(lx + 10, ly + 6, lw - 20, 22), "SIRALAMA — En Yüksek Seviye (T)", lt);
                var ll = new GUIStyle(GUI.skin.label) { fontSize = 13 }; ll.normal.textColor = Color.white;
                for (int i = 0; i < leaderboard.Count; i++)
                    GUI.Label(new Rect(lx + 14, ly + 32 + i * 22, lw - 24, 20),
                        (i + 1) + ". " + leaderboard[i].Name + "   Sv " + leaderboard[i].Level, ll);
            }

            if (showCharSheet) DrawCharSheet();

            var help = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            help.normal.textColor = Color.white;
            GUI.Label(new Rect(15, 148, 1300, 30),
                "Sol tık: yürü/saldır/topla | WASD | SPACE | 1/2/3 | I çanta | C üretim | K karakter | M pazar | G lonca | J meslek | Q görev | T sıralama | Enter sohbet | R tamir | P/Y/L parti | ESC duraklat", help);

            if (paused) DrawPauseMenu();
        }

        // Faz 1 (Foundation): ilk-oynanış isim ekranı — bağlantı Inspector'dan değil buradan başlar.
        void DrawNameScreen()
        {
            float w = 420, h = 190;
            float x = Screen.width / 2f - w / 2f, y = Screen.height / 2f - h / 2f;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = new Color(1f, 0.85f, 0.3f);
            GUI.Label(new Rect(x, y + 16, w, 36), "PROJECT ANATOLIA", title);

            var lbl = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            lbl.normal.textColor = Color.white;
            GUI.Label(new Rect(x + 24, y + 70, 90, 28), "İsim:", lbl);

            GUI.SetNextControlName("nameField");
            var nf = new GUIStyle(GUI.skin.textField) { fontSize = 16 };
            nameInput = GUI.TextField(new Rect(x + 100, y + 68, w - 140, 30), nameInput, 24, nf);
            GUI.FocusControl("nameField");

            var hint = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            hint.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(new Rect(x + 24, y + 104, w - 48, 20), "Aynı isimle girersen karakterin/altının kalıcı kalır.", hint);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold };
            bool enterPressed = Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
            if (GUI.Button(new Rect(x + w / 2f - 70, y + 138, 140, 36), "Oyna", btn) || enterPressed)
            {
                PlaySfx(uiClickClip);
                StartConnecting();
            }
        }

        // Faz 1 (Foundation): "Oyna"dan sonra, WS bağlantısı kurulana kadar (retry dahil) gösterilir.
        void DrawConnectingScreen()
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = Color.white;
            GUI.Label(new Rect(0, Screen.height / 2f - 20, Screen.width, 40),
                string.IsNullOrEmpty(net.Status) ? "Bağlanılıyor..." : net.Status, st);
        }

        // Faz 1 (Foundation): ESC ile aç/kapa — devam et / çıkış.
        void DrawPauseMenu()
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            float w = 260, h = 140;
            float x = Screen.width / 2f - w / 2f, y = Screen.height / 2f - h / 2f;
            GUI.color = new Color(0.05f, 0.05f, 0.08f, 0.95f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = Color.white;
            GUI.Label(new Rect(x, y + 12, w, 30), "DURAKLATILDI", title);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            if (GUI.Button(new Rect(x + 30, y + 55, w - 60, 32), "Devam Et", btn))
            {
                PlaySfx(uiClickClip);
                paused = false;
            }
            if (GUI.Button(new Rect(x + 30, y + 95, w - 60, 32), "Çıkış", btn))
            {
                PlaySfx(uiClickClip);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        }

        // Faz A: Karakter Sayfası — 6 attribute + harcanmamış puan (+ ile dağıt) + derived stats.
        static readonly string[] attrNames = { "Güç (STR)", "Çeviklik (DEX)", "Zeka (INT)", "Dayanıklılık (VIT)", "Bilgelik (WIS)", "Şans (LUCK)" };
        void DrawCharSheet()
        {
            float w = 340, h = 830, x = Screen.width - w - 20, y = 70;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var title = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(1f, 0.85f, 0.4f);
            GUI.Label(new Rect(x + 16, y + 10, w - 30, 26), "KARAKTER  (Sv " + myLevel + ")", title);

            var lab = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            lab.normal.textColor = Color.white;
            var pts = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            pts.normal.textColor = attrData.Points > 0 ? new Color(0.4f, 1f, 0.5f) : new Color(0.6f, 0.6f, 0.6f);

            if (!hasAttr) { GUI.Label(new Rect(x + 16, y + 44, w - 30, 24), "yükleniyor...", lab); return; }

            GUI.Label(new Rect(x + 16, y + 40, w - 30, 22), "Dağıtılabilir puan: " + attrData.Points, pts);

            int[] vals = { attrData.Str, attrData.Dex, attrData.Int, attrData.Vit, attrData.Wis, attrData.Luck };
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
            for (int i = 0; i < 6; i++)
            {
                float ry = y + 70 + i * 34;
                GUI.Label(new Rect(x + 16, ry, 210, 26), attrNames[i] + ":  " + vals[i], lab);
                GUI.enabled = attrData.Points > 0;
                if (GUI.Button(new Rect(x + w - 52, ry - 2, 34, 28), "+", btn))
                    net.Outbound.Enqueue(Protocol.EncodeSpendAttr((byte)i)); // sunucu doğrular, yeni paket döner
                GUI.enabled = true;
            }

            var sh = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            sh.normal.textColor = new Color(0.75f, 0.85f, 1f);
            float sy = y + 288;
            GUI.Label(new Rect(x + 16, sy, w - 30, 22), "— Türetilmiş İstatistikler —", title);
            string[] derived = {
                "Can (HP): " + myMaxHp,
                "Hasar: " + myDamage,
                "Mana: " + attrData.MaxMana,
                "Savunma: " + attrData.Defense,
                "Kritik şans: %" + attrData.CritChance.ToString("0.0"),
                "Saldırı hızı: %" + attrData.AttackSpeed.ToString("0.0"),
                "Bekleme azaltma: %" + attrData.CooldownRed.ToString("0.0"),
                "Nadir loot: %" + attrData.LootFind.ToString("0.0"),
            };
            for (int i = 0; i < derived.Length; i++)
                GUI.Label(new Rect(x + 16, sy + 26 + i * 20, w - 30, 20), derived[i], sh);

            // Faz C: silah ustalığı uzmanlık bölümü
            float my = sy + 26 + derived.Length * 20 + 10;
            GUI.Label(new Rect(x + 16, my, w - 30, 22), "— Silah Ustalığı —", title);
            if (!hasMasteryDetail) { GUI.Label(new Rect(x + 16, my + 26, w - 30, 20), "silah kuşan (ustalık yok)", sh); return; }
            // aile + seviye + (varsa) Grandmaster rütbesi
            string gmTxt = masteryDetail.GM > 0 ? "  ★ GM " + RomanGM(masteryDetail.GM) : "";
            GUI.Label(new Rect(x + 16, my + 26, w - 30, 20),
                masteryDetail.Family + "  (Sv " + masteryDetail.Level + ")" + gmTxt, lab);
            // Legend rank (hesap prestiji) — varsa altın renkte
            if (masteryDetail.Legend > 0)
            {
                var lg = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
                lg.normal.textColor = new Color(1f, 0.82f, 0.2f);
                GUI.Label(new Rect(x + 200, my + 26, w - 210, 20), "Legend " + masteryDetail.Legend, lg);
            }
            if (masteryDetail.Level < masteryDetail.UnlockLevel)
            {
                GUI.Label(new Rect(x + 16, my + 48, w - 30, 20),
                    "Uzmanlık Sv " + masteryDetail.UnlockLevel + "'de açılır", sh);
                return;
            }
            // uzmanlık seçimi (level >= 30)
            var sbtn = new GUIStyle(GUI.skin.button) { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            for (int i = 0; i < masteryDetail.Options.Count; i++)
            {
                var o = masteryDetail.Options[i];
                bool active = o.Id == masteryDetail.ActiveSpec;
                float by = my + 50 + i * 34;
                var prev = GUI.color;
                if (active) GUI.color = new Color(0.4f, 1f, 0.5f); // aktif = yeşil
                if (GUI.Button(new Rect(x + 16, by, w - 32, 30),
                    (active ? "✔ " : "") + o.Name + "  (+" + o.DmgBonus + " hasar)", sbtn))
                {
                    if (!active) net.Outbound.Enqueue(Protocol.EncodeSelectSpec(o.Id)); // sunucu doğrular
                }
                GUI.color = prev;
            }

            // Faz F: Enchant (büyüleme) bölümü
            float ey = my + 50 + masteryDetail.Options.Count * 34 + 12;
            GUI.Label(new Rect(x + 16, ey, w - 30, 22), "— Büyüleme (güvenli bölge) —", title);
            if (hasEnchant)
            {
                var ebtn = new GUIStyle(GUI.skin.button) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
                GUI.Label(new Rect(x + 16, ey + 26, w - 30, 20), "Silah +" + enchantInfo.WLvl + "   Zırh +" + enchantInfo.ALvl, lab);
                if (GUI.Button(new Rect(x + 16, ey + 50, (w - 40) / 2, 30),
                    "Silah +" + (enchantInfo.WLvl + 1) + "\n" + enchantInfo.WCost + "g %" + enchantInfo.WPct, ebtn))
                    net.Outbound.Enqueue(Protocol.EncodeEnchant(0));
                if (GUI.Button(new Rect(x + 26 + (w - 40) / 2, ey + 50, (w - 40) / 2, 30),
                    "Zırh +" + (enchantInfo.ALvl + 1) + "\n" + enchantInfo.ACost + "g %" + enchantInfo.APct, ebtn))
                    net.Outbound.Enqueue(Protocol.EncodeEnchant(1));
            }

            // Faz K: hizip itibarları
            float ry2 = ey + 88;
            GUI.Label(new Rect(x + 16, ry2, w - 30, 22), "— İtibar —", title);
            for (int i = 0; i < repList.Count && i < 4; i++)
            {
                var r = repList[i];
                GUI.Label(new Rect(x + 16, ry2 + 24 + i * 20, w - 30, 20),
                    r.Faction + ":  " + r.Level + "  (" + r.Points + ")", sh);
            }
        }

        string MembersStr()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < guildMembers.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(guildMembers[i].Name);
                if (guildMembers[i].Rank == 2) sb.Append("★");
                else if (guildMembers[i].Rank == 1) sb.Append("◆");
            }
            return sb.ToString();
        }

        static string ProfName(string p)
        {
            switch (p)
            {
                case "mining": return "Madencilik";
                case "woodcutting": return "Oduncu";
                case "herbalism": return "Şifacılık";
                case "fishing": return "Balıkçılık";
                case "blacksmith": return "Demircilik";
                case "refinery": return "Rafineri";
                case "leatherworker": return "Dericilik";
                case "alchemist": return "Simyacılık";
                default: return p;
            }
        }

        // ClassName: silah sınıfı kodunu Türkçe görünen isme çevirir.
        static string ClassName(string cls)
        {
            switch (cls)
            {
                case "blade": return "Kılıç";
                case "unarmed": return "Silahsız";
                case "bow": return "Yay";
                case "staff": return "Asa";
                default: return cls;
            }
        }

        static Color RarityColor(byte r)
        {
            switch (r)
            {
                case 1: return new Color(0.4f, 1f, 0.4f);     // uncommon yeşil
                case 2: return new Color(0.3f, 0.6f, 1f);     // rare mavi
                case 3: return new Color(0.8f, 0.4f, 1f);     // epic mor
                case 4: return new Color(1f, 0.6f, 0.1f);     // legendary turuncu
                case 5: return new Color(1f, 0.2f, 0.3f);     // mythic kırmızı
                case 6: return new Color(0.2f, 0.95f, 0.9f);  // ancient turkuaz
                case 7: return new Color(1f, 0.85f, 0.2f);    // relic altın
                case 8: return new Color(1f, 1f, 1f);         // divine beyaz-parlak
                case 9: return new Color(0.5f, 0.5f, 0.5f);   // poor gri
                default: return new Color(0.82f, 0.82f, 0.82f); // common
            }
        }

        async void OnDestroy()
        {
            if (net != null) await net.CloseGracefully();
        }
    }
}
