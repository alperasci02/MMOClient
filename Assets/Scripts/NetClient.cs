// NetClient.cs — sunucuya bağlan, hareket et, dövüş, loot, kalıcılık.
// Kontroller (PC):
//   Sol tık boş yere -> oraya yürü
//   Sol tık mob'a    -> yanına yürü ve menzile girince otomatik saldır
//   WASD             -> elle hareket
//   SPACE            -> en yakın mob'a saldır
// Görsel: oyuncular kapsül (yeşil=sen, kırmızı=diğerleri), mob'lar mor küp (vurdukça küçülür).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MMO
{
    public class NetClient : MonoBehaviour
    {
        [Tooltip("Karakter ismi — aynı isimle girince altının/karakterin kayıtlı kalır")]
        public string playerName = "Kahraman";
        [Tooltip("Sunucu WebSocket adresi")]
        public string url = "ws://localhost:8080/ws";
        [Tooltip("Saniyede kaç hareket niyeti gönderilsin")]
        public float sendRate = 15f;

        const float MobMaxHp = 50f;
        const float AttackReach = 2.7f; // toplama/mezar yakın menzili (sunucu 3.0)
        float myReach = 2.7f;           // silaha göre saldırı menzili (yay/asa uzun) — Faz 23

        ClientWebSocket ws;
        CancellationTokenSource cts;
        readonly ConcurrentQueue<byte[]> inbound = new ConcurrentQueue<byte[]>();
        readonly ConcurrentQueue<byte[]> outbound = new ConcurrentQueue<byte[]>();

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

        void SpawnFloat(float x, float y, float z, string txt, Color c)
        {
            floaters.Add(new FloatText { Pos = new Vector3(x, y, z), Text = txt, Col = c, Life = 1.1f });
        }

        // Asset entegrasyonu: Resources/Entities/<key> prefab'ı varsa onu kullan, yoksa primitive (fallback).
        // Kullanıcı şu isimlerle prefab koyar -> oyun otomatik gerçek modelleri kullanır:
        //   Assets/Resources/Entities/player, enemy, boss, node, portal, corpse
        readonly Dictionary<ulong, bool> usePrefab = new Dictionary<ulong, bool>();
        Animator playerAnim; // oyuncu modelinin Animator'ı (kuruluysa) — yürüme/saldırı animasyonu için
        GameObject weaponVisual; // sağ ele takılı gerçek silah modeli (sınıfa göre değişir)
        GameObject armorVisual;  // göğüs kemiğine takılı gerçek zırh modeli (tier'a göre değişir)
        readonly Dictionary<ulong, Animator> anims = new Dictionary<ulong, Animator>(); // tüm varlıkların Animator'ları (mob dahil)

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

        async void Start()
        {
            SetupScene();
            cts = new CancellationTokenSource();
            ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(new Uri(url), cts.Token);
                Debug.Log("[NetClient] bağlandı: " + url + " olarak '" + playerName + "'");
                outbound.Enqueue(Protocol.EncodeJoin(playerName));
                _ = ReceiveLoop();
                _ = SendLoop();
            }
            catch (Exception e)
            {
                Debug.LogError("[NetClient] bağlanılamadı: " + e.Message +
                    "  -> Sunucu açık mı? (server klasöründe: go run ./cmd/gameserver)");
            }
        }

        async Task ReceiveLoop()
        {
            var buf = new byte[64 * 1024];
            while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                using (var mem = new System.IO.MemoryStream())
                {
                    WebSocketReceiveResult res;
                    do
                    {
                        res = await ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token);
                        if (res.MessageType == WebSocketMessageType.Close) return;
                        mem.Write(buf, 0, res.Count);
                    } while (!res.EndOfMessage);
                    inbound.Enqueue(mem.ToArray());
                }
            }
        }

        async Task SendLoop()
        {
            while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
            {
                if (outbound.TryDequeue(out var frame))
                    await ws.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true, cts.Token);
                else
                    await Task.Delay(5, cts.Token);
            }
        }

        void Update()
        {
            // 1) gelen mesajlar
            while (inbound.TryDequeue(out var msg))
            {
                if (Protocol.TryDecodeJoined(msg, out var id))
                {
                    myId = id;
                    Debug.Log("[NetClient] benim varlık id'm = " + id);
                }
                else if (Protocol.TryDecodeSnapshot(msg, out _, out var ents))
                {
                    lastEnts = ents;
                    ApplySnapshot(ents);
                }
                else if (Protocol.TryDecodeReward(msg, out var amt, out var total))
                {
                    myGold = total;
                    if (amt > 0)
                    {
                        lastReward = amt;
                        rewardPopupTimer = 1.5f;
                        Debug.Log($"[NetClient] +{amt} altın! (toplam {myGold})");
                    }
                }
                else if (Protocol.TryDecodeStats(msg, out var lvl, out var xp, out var xpNext, out var maxHp, out var dmg))
                {
                    if (lvl > myLevel) Debug.Log($"[NetClient] SEVİYE ATLADIN -> {lvl}!");
                    myLevel = lvl; myXp = xp; myXpNext = xpNext; myMaxHp = maxHp; myDamage = dmg;
                }
                else if (Protocol.TryDecodeEquipment(msg, out var wdef, out var adef))
                {
                    bool armorChanged = adef != equippedArmor;
                    bool weaponChanged = wdef != equippedWeapon;
                    equippedWeapon = wdef; equippedArmor = adef;
                    if (weaponChanged) UpdateWeaponVisual();
                    if (armorChanged) UpdateArmorVisual();
                }
                else if (Protocol.TryDecodeAttributes(msg, out var adat))
                {
                    attrData = adat; hasAttr = true; // Faz A: karakter sayfası verisi
                }
                else if (Protocol.TryDecodeMasteryDetail(msg, out var mdet))
                {
                    masteryDetail = mdet; hasMasteryDetail = true; // Faz C: uzmanlık paneli
                }
                else if (Protocol.TryDecodeZone(msg, out var zid, out var zname))
                {
                    if (zid != zoneId) { zonePopupTimer = 2.5f; Debug.Log($"[NetClient] Bölge: {zname}"); }
                    zoneId = zid; zoneName = zname;
                    UpdateDungeonEnv(); // zindan görsel ortamı (varsa) yükle/kaldır
                }
                else if (Protocol.TryDecodeGear(msg, out var wd, out var wm, out var ad, out var am, out var ip, out var arange))
                {
                    weaponDur = wd; weaponMaxDur = wm; armorDur = ad; armorMaxDur = am; itemPower = ip;
                    myReach = Mathf.Max(2.7f, arange - 0.3f); // menzilli silahta uzaktan saldır
                }
                else if (Protocol.TryDecodeDeath(msg, out var dangerous))
                {
                    deathMsg = dangerous ? "ÖLDÜN!  Eşyaların düştü — mezarına dön!" : "Öldün.";
                    deathMsgTimer = 4f;
                    corpseTarget = 0; corpseRequested = false;
                    Debug.Log("[NetClient] " + deathMsg);
                }
                else if (Protocol.TryDecodePartyState(msg, out var pm))
                {
                    partyMembers = pm;
                    partyIds = new HashSet<ulong>();
                    foreach (var m in pm) partyIds.Add(m.Id);
                }
                else if (Protocol.TryDecodePartyInvite(msg, out var inviter))
                {
                    pendingInviteFrom = inviter; pendingInviteTimer = 15f;
                    Debug.Log($"[NetClient] {inviter} seni partiye davet etti (Y: kabul)");
                }
                else if (Protocol.TryDecodeGuildInfo(msg, out var hg, out var gn, out var gl, out var bgold, out var gms))
                {
                    hasGuild = hg; guildName = gn; guildIsLeader = gl; guildBankGold = bgold; guildMembers = gms;
                }
                else if (Protocol.TryDecodeGuildBank(msg, out var gbank))
                {
                    guildBank = gbank;
                }
                else if (Protocol.TryDecodeProfMastery(msg, out var pf, out var plv, out var pxp, out var pnx, out var pbn))
                {
                    professions[pf] = new ProfState { Level = plv, Xp = pxp, XpNext = pnx, Bonus = pbn };
                }
                else if (Protocol.TryDecodeNotice(msg, out var notice))
                {
                    noticeMsg = notice; noticeTimer = 3f;
                    Debug.Log("[NetClient] " + notice);
                }
                else if (Protocol.TryDecodeAbilities(msg, out var abs))
                {
                    abilities = abs;
                }
                else if (Protocol.TryDecodeMana(msg, out var mcur, out var mmax))
                {
                    myMana = mcur; myMaxMana = mmax; // Faz D
                }
                else if (Protocol.TryDecodeEnchantInfo(msg, out var einf))
                {
                    enchantInfo = einf; hasEnchant = true; // Faz F
                }
                else if (Protocol.TryDecodeWorldTime(msg, out var wmin))
                {
                    worldMinute = wmin; // Faz J
                }
                else if (Protocol.TryDecodeReputation(msg, out var reps))
                {
                    repList = reps; // Faz K
                }
                else if (Protocol.TryDecodeQuests(msg, out var qs))
                {
                    quests = qs;
                }
                else if (Protocol.TryDecodeChat(msg, out var cscope, out var csender, out var ctext))
                {
                    string tag = cscope == 1 ? "[G]" : "[B]";
                    chatLog.Add(tag + " " + csender + ": " + ctext);
                    if (chatLog.Count > 8) chatLog.RemoveAt(0);
                }
                else if (Protocol.TryDecodeLeaderboard(msg, out var lb))
                {
                    leaderboard = lb;
                }
                else if (Protocol.TryDecodeMastery(msg, out var mc, out var mlvl, out var mxp, out var mxpNext, out var mbonus))
                {
                    if (mc == masteryClass && mlvl > masteryLevel)
                    {
                        masteryPopupLevel = mlvl; masteryPopupTimer = 2.5f;
                        Debug.Log($"[NetClient] {ClassName(mc)} USTALIĞI {mlvl}! (+{mbonus} hasar)");
                    }
                    bool classChanged = mc != masteryClass;
                    masteryClass = mc; masteryLevel = mlvl; masteryXp = mxp; masteryXpNext = mxpNext; masteryBonus = mbonus;
                    if (classChanged) UpdateWeaponVisual(); // silah sınıfı değişti -> elde görünen silahı güncelle
                }
                else if (Protocol.TryDecodeRecipes(msg, out var recs))
                {
                    recipes = recs;
                }
                else if (Protocol.TryDecodeMarket(msg, out var mk))
                {
                    market = mk;
                }
                else if (Protocol.TryDecodeInventory(msg, out var inv))
                {
                    inventory = inv;
                    gatherRequested = false; // toplama tamamlandı -> tekrar topla
                }
                else if (Protocol.TryDecodeLoot(msg, out var ln, out var lr, out var lq))
                {
                    lootName = ln; lootRarity = lr; lootQty = lq; lootPopupTimer = 2f;
                    Debug.Log($"[NetClient] loot: +{lq} {ln}");
                }
            }

            if (rewardPopupTimer > 0f) rewardPopupTimer -= Time.deltaTime;
            if (lootPopupTimer > 0f) lootPopupTimer -= Time.deltaTime;
            if (masteryPopupTimer > 0f) masteryPopupTimer -= Time.deltaTime;
            if (zonePopupTimer > 0f) zonePopupTimer -= Time.deltaTime;
            if (repairMsgTimer > 0f) repairMsgTimer -= Time.deltaTime;
            if (deathMsgTimer > 0f) deathMsgTimer -= Time.deltaTime;
            if (noticeTimer > 0f) noticeTimer -= Time.deltaTime;

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

            if (ws == null || ws.State != WebSocketState.Open) return;
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
                outbound.Enqueue(Protocol.EncodeMove(0, 0)); // yazarken dur
                return;
            }

            if (kb != null && kb.iKey.wasPressedThisFrame) showInventory = !showInventory;
            if (kb != null && kb.cKey.wasPressedThisFrame) showCrafting = !showCrafting;
            if (kb != null && kb.kKey.wasPressedThisFrame) showCharSheet = !showCharSheet; // Faz A: karakter sayfası
            if (kb != null && kb.mKey.wasPressedThisFrame) { showMarket = !showMarket; if (showMarket) outbound.Enqueue(Protocol.EncodeMarketBrowse()); }
            if (kb != null && kb.gKey.wasPressedThisFrame) { showGuild = !showGuild; if (showGuild) outbound.Enqueue(Protocol.EncodeGuildInfoReq()); }
            if (kb != null && kb.jKey.wasPressedThisFrame) showProf = !showProf;
            if (kb != null && kb.tKey.wasPressedThisFrame) { showLeader = !showLeader; if (showLeader) outbound.Enqueue(Protocol.EncodeLeaderReq()); }
            if (kb != null && kb.qKey.wasPressedThisFrame) { showQuests = !showQuests; if (showQuests) outbound.Enqueue(Protocol.EncodeQuestList()); }
            if (kb != null && kb.rKey.wasPressedThisFrame)
            {
                if (zoneId == "meadow") { outbound.Enqueue(Protocol.EncodeRepair()); repairMsg = "Tamir ediliyor..."; }
                else repairMsg = "Tamir sadece güvenli bölgede (Başlangıç Çayırı)";
                repairMsgTimer = 2.5f;
            }
            // Faz 15: parti — P davet, Y kabul, L ayrıl
            if (kb != null && kb.pKey.wasPressedThisFrame) InviteNearest();
            if (kb != null && kb.yKey.wasPressedThisFrame && pendingInviteTimer > 0f)
            {
                outbound.Enqueue(Protocol.EncodePartyAccept());
                pendingInviteTimer = 0f; pendingInviteFrom = "";
                partyMsg = "Partiye katıldın"; partyMsgTimer = 2.5f;
            }
            if (kb != null && kb.lKey.wasPressedThisFrame && partyMembers.Count > 0)
            {
                outbound.Enqueue(Protocol.EncodePartyLeave());
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

            // 5) hedef mob menzildeyse otomatik saldır
            if (attackTarget != 0 && attackCooldown <= 0f && InRangeOfMob(attackTarget))
            {
                outbound.Enqueue(Protocol.EncodeAttack(attackTarget));
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
                        if (!gatherRequested) { outbound.Enqueue(Protocol.EncodeGather(gatherTarget)); gatherRequested = true; }
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
                        if (!corpseRequested) { outbound.Enqueue(Protocol.EncodeLootCorpse(corpseTarget)); corpseRequested = true; }
                    }
                }
            }

            // 6) hareket niyetini yolla (throttle)
            sendTimer += Time.deltaTime;
            if (sendTimer >= 1f / sendRate)
            {
                sendTimer = 0f;
                outbound.Enqueue(Protocol.EncodeMove(dir.x, dir.y));
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
                outbound.Enqueue(Protocol.EncodePartyInvite(best));
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
            if (t.Length > 0) outbound.Enqueue(Protocol.EncodeChat(scope, t));
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
            outbound.Enqueue(Protocol.EncodeAbility((byte)idx, target));
            if (playerAnim != null)
            {
                // yeteneğe özel animasyon: heal=Buff (savaş narası), 1.yetenek=Attack2 (combo), 2.=Attack3 (360 dönüş)
                string trig = ab.Type == 2 ? "Buff" : (idx == 0 ? "Attack2" : "Attack3");
                if (HasParam(playerAnim, trig)) playerAnim.SetTrigger(trig);
                else playerAnim.SetTrigger("Attack"); // eski controller'la geriye uyumlu
            }
            abilityReadyAt[idx] = Time.time + ab.CooldownMs / 1000f; // yerel cooldown (sunucu otoriter)
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
            if (best != 0) { outbound.Enqueue(Protocol.EncodeAttack(best)); if (playerAnim != null) playerAnim.SetTrigger("Attack"); }
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
                if (e.Id == myId) myHp = e.Hp;
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
                        if (delta < 0) { SpawnFloat(e.X, yOff + 1.4f, e.Y, "-" + (-delta), new Color(1f, 0.55f, 0.15f)); hitFlash[e.Id] = Time.time + 0.18f; }
                        else if (e.Id == myId) SpawnFloat(e.X, yOff + 1.4f, e.Y, "+" + delta, new Color(0.4f, 1f, 0.4f));
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
            }

            var remove = new List<ulong>();
            foreach (var kv in cubes) if (!seen.Contains(kv.Key)) remove.Add(kv.Key);
            foreach (var id in remove) { Destroy(cubes[id]); cubes.Remove(id); prevHp.Remove(id); baseCol.Remove(id); hitFlash.Remove(id); targetPos.Remove(id); usePrefab.Remove(id); anims.Remove(id); }
        }

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

        static void SetColor(GameObject go, Color c)
        {
            // FBX modellerde renderer alt objelerdedir -> hepsini boya
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r.material.HasProperty("_BaseColor")) r.material.SetColor("_BaseColor", c);
                else r.material.color = c;
            }
        }

        void OnGUI()
        {
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
                        outbound.Enqueue(Protocol.EncodeEquip(it.DefID));
                    if (GUI.Button(new Rect(px + w - 74, ry, 66, 22), "Sat", sbtn))
                    {
                        long pr; long.TryParse(sellPriceStr, out pr);
                        if (pr > 0) outbound.Enqueue(Protocol.EncodeMarketList(it.DefID, 1, pr));
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
                        outbound.Enqueue(Protocol.EncodeCraft(r.Id));
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
                        outbound.Enqueue(Protocol.EncodeMarketBuy(e.Id));
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
                        outbound.Enqueue(Protocol.EncodeGuildCreate(guildNameInput));
                    if (GUI.Button(new Rect(gx + 342, gy + 33, 80, 22), "Katıl", gbtn))
                        outbound.Enqueue(Protocol.EncodeGuildJoin(guildNameInput));
                }
                else
                {
                    GUI.Label(new Rect(gx + 12, gy + 6, gw - 20, 22),
                        "LONCA: " + guildName + (guildIsLeader ? " (lider)" : "") + "   —   Kasa: " + guildBankGold + " altın", gt);
                    if (GUI.Button(new Rect(gx + gw - 70, gy + 6, 60, 20), "Ayrıl", gbtn))
                        outbound.Enqueue(Protocol.EncodeGuildLeave());
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
                                if (m.Rank == 0 && GUI.Button(new Rect(gx + 215, yy, 62, 20), "Terfi", sbtn)) outbound.Enqueue(Protocol.EncodeGuildPromote(m.Name));
                                if (m.Rank == 1 && GUI.Button(new Rect(gx + 215, yy, 62, 20), "İndir", sbtn)) outbound.Enqueue(Protocol.EncodeGuildDemote(m.Name));
                                if (GUI.Button(new Rect(gx + 281, yy, 50, 20), "Kov", sbtn)) outbound.Enqueue(Protocol.EncodeGuildKick(m.Name));
                            }
                            yy += 22;
                        }
                    }
                    // altın
                    GUI.Label(new Rect(gx + 12, yy, 60, 22), "Altın:", gl2);
                    guildGoldInput = GUI.TextField(new Rect(gx + 64, yy, 80, 22), guildGoldInput, 9);
                    long gamt; long.TryParse(guildGoldInput, out gamt);
                    if (GUI.Button(new Rect(gx + 150, yy, 80, 22), "Yatır", gbtn) && gamt > 0)
                        outbound.Enqueue(Protocol.EncodeGuildDepositGold(gamt));
                    if (GUI.Button(new Rect(gx + 234, yy, 80, 22), "Çek", gbtn) && gamt > 0)
                        outbound.Enqueue(Protocol.EncodeGuildWithdrawGold(gamt));
                    yy += 28;
                    // kasa eşyaları (çek)
                    GUI.Label(new Rect(gx + 12, yy, gw - 20, 18), "Kasa eşyaları (tıkla=çek):", gl2); yy += 20;
                    for (int i = 0; i < guildBank.Count; i++)
                    {
                        var bi = guildBank[i];
                        gbtn.normal.textColor = RarityColor(bi.Rarity); gbtn.hover.textColor = RarityColor(bi.Rarity);
                        if (GUI.Button(new Rect(gx + 16, yy, gw - 32, 22), bi.Name + " x" + bi.Qty))
                            outbound.Enqueue(Protocol.EncodeGuildWithdrawItem(bi.InstID));
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
                            outbound.Enqueue(Protocol.EncodeGuildDepositItem(it.DefID, it.Qty));
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
                    if (q.State == 0) { if (GUI.Button(new Rect(qx + qw - 110, ry, 100, 24), "Kabul Et", qbtn)) outbound.Enqueue(Protocol.EncodeQuestAccept(q.Id)); }
                    else if (q.State == 2) { if (GUI.Button(new Rect(qx + qw - 110, ry, 100, 24), "Ödül Al", qbtn)) outbound.Enqueue(Protocol.EncodeQuestClaim(q.Id)); }
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
                "Sol tık: yürü/saldır/topla | WASD | SPACE | 1/2/3 | I çanta | C üretim | K karakter | M pazar | G lonca | J meslek | Q görev | T sıralama | Enter sohbet | R tamir | P/Y/L parti", help);
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
                    outbound.Enqueue(Protocol.EncodeSpendAttr((byte)i)); // sunucu doğrular, yeni paket döner
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
                    if (!active) outbound.Enqueue(Protocol.EncodeSelectSpec(o.Id)); // sunucu doğrular
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
                    outbound.Enqueue(Protocol.EncodeEnchant(0));
                if (GUI.Button(new Rect(x + 26 + (w - 40) / 2, ey + 50, (w - 40) / 2, 30),
                    "Zırh +" + (enchantInfo.ALvl + 1) + "\n" + enchantInfo.ACost + "g %" + enchantInfo.APct, ebtn))
                    outbound.Enqueue(Protocol.EncodeEnchant(1));
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
            try { cts?.Cancel(); } catch { }
            if (ws != null && ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
                catch { }
            }
            ws?.Dispose();
        }
    }
}
