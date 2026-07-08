// Protocol.cs — sunucunun codec.go'su ile BİREBİR aynı ikili protokol (C# aynası).
// C# x86'da little-endian olduğu için big-endian'a elle çeviriyoruz.
//
// ╔═══════════════════════════════════════════════════════════════════════╗
// ║ PROTOKOL SÖZLEŞMESİ (Docs/AI_RULES.md §3, MMO reposunda) — BU DOSYAYI  ║
// ║ TEK BAŞINA DEĞİŞTİRME. İkizi: server/internal/realtime/codec.go       ║
// ║ (ayrı repo: PROJECT-ANATOLIA/MMO).                                     ║
// ║  • Çerçeve/alan düzeni değişirse İKİ dosya da AYNI oturumda güncellenir║
// ║    (NetClient.Update'teki karşılığıyla / ws.go'daki decode dispatch).  ║
// ║  • SnapRowSize burada = snapRowSize orada (şu an 23). Tek baytlık kayma║
// ║    geçmişte sessiz bozulmaya yol açtı (21→22→23) — bkz. docs/06.       ║
// ║  • Opcode'lar EKLEMELİ: yenisi 0x31'in ötesine gider, numara değişmez/ ║
// ║    yeniden kullanılmaz.                                                ║
// ║  • Değişiklik sonrası kanıt: sunucuda `go run ./cmd/ws-testclient` +   ║
// ║    Unity Play testi. İkisi de geçmeden "bitti" deme.                   ║
// ╚═══════════════════════════════════════════════════════════════════════╝
using System;
using System.Collections.Generic;
using System.Text;

namespace MMO
{
    public static class Protocol
    {
        public const byte OpJoin = 0x01;
        public const byte OpMove = 0x02;
        public const byte OpAttack = 0x03;
        public const byte OpAbility = 0x04;
        public const byte OpQuest = 0x05;
        public const byte OpEquip = 0x06;
        public const byte OpGather = 0x07;
        public const byte OpCraft = 0x08;
        public const byte OpMarketList = 0x09;
        public const byte OpMarketBuy = 0x0A;
        public const byte OpMarketBrowse = 0x0B;
        public const byte OpRepair = 0x0C;
        public const byte OpLootCorpse = 0x0D;
        public const byte OpParty = 0x0E;
        public const byte OpGuild = 0x0F;
        public const byte OpJoined = 0x10;
        public const byte OpSnapshot = 0x11;
        public const byte OpReward = 0x12;
        public const byte OpStats = 0x13;
        public const byte OpInventory = 0x14;
        public const byte OpLoot = 0x15;
        public const byte OpEquipment = 0x16;
        public const byte OpRecipes = 0x17;
        public const byte OpMarket = 0x18;
        public const byte OpMastery = 0x19;
        public const byte OpZone = 0x1A;
        public const byte OpGear = 0x1B;
        public const byte OpDeath = 0x1C;
        public const byte OpPartyState = 0x1D;
        public const byte OpPartyInvite = 0x1E;
        public const byte OpGuildInfo = 0x1F;
        public const byte OpGuildBank = 0x20;
        public const byte OpProfMastery = 0x21;
        public const byte OpNotice = 0x22;
        public const byte OpAbilities = 0x23;
        public const byte OpQuests = 0x24;
        public const byte OpChatSend = 0x25;
        public const byte OpChat = 0x26;
        public const byte OpLeaderReq = 0x27;
        public const byte OpLeaderboard = 0x28;
        public const byte OpAttributes = 0x29; // sunucu->istemci: attribute + puan + derived (Faz A)
        public const byte OpSpendAttr = 0x2A;  // istemci->sunucu: puan harca [u8 which] (Faz A)
        public const byte OpMasteryDetail = 0x2B; // sunucu->istemci: aile + uzmanlık listesi (Faz C)
        public const byte OpSelectSpec = 0x2C;    // istemci->sunucu: uzmanlık seç [str id] (Faz C)
        public const byte OpMana = 0x2D;          // sunucu->istemci: mevcut/max mana (Faz D)
        public const byte OpEnchantInfo = 0x2E;   // sunucu->istemci: enchant seviye+maliyet+şans (Faz F)
        public const byte OpEnchant = 0x2F;       // istemci->sunucu: ekipmanı büyüle [u8 slot] (Faz F)
        public const byte OpWorldTime = 0x30;     // sunucu->istemci: gün-içi dakika (Faz J)
        public const byte OpReputation = 0x31;    // sunucu->istemci: hizip itibarları (Faz K)

        public struct InvItem { public string DefID; public string Name; public byte Rarity; public int Qty; }
        public struct Ingredient { public string Name; public int Qty; }
        public struct RecipeInfo { public string Id; public string Name; public string OutName; public byte OutRarity; public List<Ingredient> Inputs; }
        public struct MarketEntry { public long Id; public string Name; public byte Rarity; public int Qty; public long Price; public string Seller; }
        public struct PartyMember { public ulong Id; public string Name; public int Level; public int MaxHp; }
        public struct GuildMemberC { public string Name; public byte Rank; } // 2=lider 1=officer 0=üye
        public struct LeaderRow { public string Name; public int Level; }
        public struct GuildBankItem { public string InstID; public string Name; public byte Rarity; public int Qty; }
        public struct AbilityC { public string Name; public int CooldownMs; public byte Type; public int ManaCost; }
        public struct QuestC { public string Id; public string Name; public byte State; public int Progress; public int Count; }

        public const byte KindPlayer = 0;
        public const byte KindMob = 1;
        public const byte KindResource = 2;
        public const byte KindPortal = 3;
        public const byte KindCorpse = 4;

        const int SnapRowSize = 23;

        // Mob görsel alt-türleri (snapshot subKind baytı — sunucu codec.go ile eş)
        public const byte SubDefault = 0; // iskelet/haydut
        public const byte SubWolf = 1;
        public const byte SubSlime = 2;
        public const byte SubBat = 3;

        public struct Entity
        {
            public ulong Id;
            public byte Kind;
            public short Hp;
            public short MaxHp;
            public float X;
            public float Y;
            public byte SubKind;
            public byte Status; // Faz E: baskın status effect kodu (0=yok,1=burn,2=poison,3=bleed,4=stun,5=freeze,6=slow)
        }

        public static byte[] EncodeJoin(string name)
        {
            var nb = System.Text.Encoding.UTF8.GetBytes(name ?? "");
            if (nb.Length > 255) Array.Resize(ref nb, 255);
            var b = new byte[2 + nb.Length];
            b[0] = OpJoin;
            b[1] = (byte)nb.Length;
            Array.Copy(nb, 0, b, 2, nb.Length);
            return b;
        }

        public static byte[] EncodeMove(float dx, float dy)
        {
            var b = new byte[9];
            b[0] = OpMove;
            WriteFloatBE(b, 1, dx);
            WriteFloatBE(b, 5, dy);
            return b;
        }

        public static byte[] EncodeAttack(ulong target)
        {
            var b = new byte[9];
            b[0] = OpAttack;
            WriteU64BE(b, 1, target);
            return b;
        }

        public static byte[] EncodeAbility(byte idx, ulong target)
        {
            var b = new byte[10];
            b[0] = OpAbility; b[1] = idx;
            WriteU64BE(b, 2, target);
            return b;
        }

        public static byte[] EncodeQuestList() => new byte[] { OpQuest, 0 };
        public static byte[] EncodeQuestAccept(string id) => questAction(1, id);
        public static byte[] EncodeQuestClaim(string id) => questAction(2, id);
        static byte[] questAction(byte action, string id)
        {
            var d = Encoding.UTF8.GetBytes(id ?? "");
            if (d.Length > 255) Array.Resize(ref d, 255);
            var b = new byte[3 + d.Length];
            b[0] = OpQuest; b[1] = action; b[2] = (byte)d.Length;
            Array.Copy(d, 0, b, 3, d.Length);
            return b;
        }

        // [0x24][u8 n] n*([id][name][u8 state][u16 progress][u16 count])
        public static bool TryDecodeQuests(byte[] b, out List<QuestC> quests)
        {
            quests = null;
            if (b.Length < 2 || b[0] != OpQuests) return false;
            int n = b[1];
            quests = new List<QuestC>(n);
            int off = 2;
            for (int i = 0; i < n; i++)
            {
                if (off >= b.Length) return false;
                int il = b[off++]; if (off + il > b.Length) return false;
                string id = Encoding.UTF8.GetString(b, off, il); off += il;
                if (off >= b.Length) return false;
                int nl = b[off++]; if (off + nl > b.Length) return false;
                string nm = Encoding.UTF8.GetString(b, off, nl); off += nl;
                if (off + 5 > b.Length) return false;
                byte state = b[off++];
                int prog = (b[off] << 8) | b[off + 1]; off += 2;
                int cnt = (b[off] << 8) | b[off + 1]; off += 2;
                quests.Add(new QuestC { Id = id, Name = nm, State = state, Progress = prog, Count = cnt });
            }
            return true;
        }

        // [0x23][u8 n] n*([u8 nameLen][name][u16 cdMs][u8 type])
        public static bool TryDecodeAbilities(byte[] b, out List<AbilityC> abilities)
        {
            abilities = null;
            if (b.Length < 2 || b[0] != OpAbilities) return false;
            int n = b[1];
            abilities = new List<AbilityC>(n);
            int off = 2;
            for (int i = 0; i < n; i++)
            {
                if (off >= b.Length) return false;
                int nl = b[off++]; if (off + nl + 5 > b.Length) return false; // +2 mana
                string nm = Encoding.UTF8.GetString(b, off, nl); off += nl;
                int cd = (b[off] << 8) | b[off + 1]; off += 2;
                byte t = b[off++];
                int mana = (b[off] << 8) | b[off + 1]; off += 2; // Faz D
                abilities.Add(new AbilityC { Name = nm, CooldownMs = cd, Type = t, ManaCost = mana });
            }
            return true;
        }

        // Faz D: mana [0x2D][u16 mevcut][u16 max]
        public static bool TryDecodeMana(byte[] b, out int cur, out int max)
        {
            cur = 0; max = 0;
            if (b.Length < 5 || b[0] != OpMana) return false;
            cur = (b[1] << 8) | b[2];
            max = (b[3] << 8) | b[4];
            return true;
        }

        // Faz F: ekipmanı büyüle (slot: 0=silah 1=zırh)
        public static byte[] EncodeEnchant(byte slot) => new byte[] { OpEnchant, slot };

        // Faz J: dünya saati [0x30][u16 dakika 0..1439]
        public static bool TryDecodeWorldTime(byte[] b, out int minuteOfDay)
        {
            minuteOfDay = 0;
            if (b.Length < 3 || b[0] != OpWorldTime) return false;
            minuteOfDay = (b[1] << 8) | b[2];
            return true;
        }

        // Faz K: itibar listesi [0x31][u8 n] + n×([hizipAdı][u16 puan][seviyeAdı])
        public struct RepRow { public string Faction, Level; public int Points; }
        public static bool TryDecodeReputation(byte[] b, out List<RepRow> reps)
        {
            reps = null;
            if (b.Length < 2 || b[0] != OpReputation) return false;
            int n = b[1], off = 2;
            reps = new List<RepRow>(n);
            for (int i = 0; i < n; i++)
            {
                if (off >= b.Length) return false;
                int fl = b[off++]; string fn = Encoding.UTF8.GetString(b, off, fl); off += fl;
                int pts = (b[off] << 8) | b[off + 1]; off += 2;
                int ll = b[off++]; string lv = Encoding.UTF8.GetString(b, off, ll); off += ll;
                reps.Add(new RepRow { Faction = fn, Points = pts, Level = lv });
            }
            return true;
        }

        // Faz F: enchant bilgisi [0x2E][wLvl][aLvl][u16 wCost][u16 aCost][wPct][aPct]
        public struct EnchantInfo { public int WLvl, ALvl, WCost, ACost, WPct, APct; }
        public static bool TryDecodeEnchantInfo(byte[] b, out EnchantInfo e)
        {
            e = default;
            if (b.Length < 9 || b[0] != OpEnchantInfo) return false;
            e.WLvl = b[1]; e.ALvl = b[2];
            e.WCost = (b[3] << 8) | b[4]; e.ACost = (b[5] << 8) | b[6];
            e.WPct = b[7]; e.APct = b[8];
            return true;
        }

        public static bool TryDecodeJoined(byte[] b, out ulong id)
        {
            id = 0;
            if (b.Length < 9 || b[0] != OpJoined) return false;
            id = ReadU64BE(b, 1);
            return true;
        }

        public static bool TryDecodeReward(byte[] b, out long amount, out long total)
        {
            amount = 0; total = 0;
            if (b.Length < 17 || b[0] != OpReward) return false;
            amount = (long)ReadU64BE(b, 1);
            total = (long)ReadU64BE(b, 9);
            return true;
        }

        public static bool TryDecodeStats(byte[] b, out int level, out long xp, out long xpNext, out int maxHp, out int damage)
        {
            level = 0; xp = 0; xpNext = 0; maxHp = 0; damage = 0;
            if (b.Length < 23 || b[0] != OpStats) return false;
            level = (b[1] << 8) | b[2];
            xp = (long)ReadU64BE(b, 3);
            xpNext = (long)ReadU64BE(b, 11);
            maxHp = (b[19] << 8) | b[20];
            damage = (b[21] << 8) | b[22];
            return true;
        }

        // [0x19][u8 classLen][class][u16 level][i64 xp][i64 xpNext][u16 bonus]
        public static bool TryDecodeMastery(byte[] b, out string weaponClass, out int level, out long xp, out long xpNext, out int bonus)
        {
            weaponClass = ""; level = 0; xp = 0; xpNext = 0; bonus = 0;
            if (b.Length < 2 || b[0] != OpMastery) return false;
            int off = 1;
            int cl = b[off++]; if (off + cl > b.Length) return false;
            weaponClass = Encoding.UTF8.GetString(b, off, cl); off += cl;
            if (off + 2 + 8 + 8 + 2 > b.Length) return false;
            level = (b[off] << 8) | b[off + 1]; off += 2;
            xp = (long)ReadU64BE(b, off); off += 8;
            xpNext = (long)ReadU64BE(b, off); off += 8;
            bonus = (b[off] << 8) | b[off + 1];
            return true;
        }

        // [0x1A][u8 idLen][id][u8 nameLen][name] — oyuncunun mevcut bölgesi (Faz 11)
        public static bool TryDecodeZone(byte[] b, out string zoneId, out string zoneName)
        {
            zoneId = ""; zoneName = "";
            if (b.Length < 2 || b[0] != OpZone) return false;
            int off = 1;
            int il = b[off++]; if (off + il > b.Length) return false;
            zoneId = Encoding.UTF8.GetString(b, off, il); off += il;
            if (off >= b.Length) return false;
            int nl = b[off++]; if (off + nl > b.Length) return false;
            zoneName = Encoding.UTF8.GetString(b, off, nl);
            return true;
        }

        public static byte[] EncodeRepair() => new byte[] { OpRepair };

        public static byte[] EncodeLootCorpse(ulong corpseID)
        {
            var b = new byte[9];
            b[0] = OpLootCorpse;
            WriteU64BE(b, 1, corpseID);
            return b;
        }

        public static byte[] EncodePartyInvite(ulong targetID)
        {
            var b = new byte[10];
            b[0] = OpParty; b[1] = 0; // action 0 = davet
            WriteU64BE(b, 2, targetID);
            return b;
        }
        public static byte[] EncodePartyAccept() => new byte[] { OpParty, 1 };
        public static byte[] EncodePartyLeave() => new byte[] { OpParty, 2 };

        // [0x1D][u8 n] n*(u64 id, u8 nameLen, name, u16 level, u16 maxHP)
        public static bool TryDecodePartyState(byte[] b, out List<PartyMember> members)
        {
            members = null;
            if (b.Length < 2 || b[0] != OpPartyState) return false;
            int n = b[1];
            members = new List<PartyMember>(n);
            int off = 2;
            for (int i = 0; i < n; i++)
            {
                if (off + 8 > b.Length) return false;
                ulong id = ReadU64BE(b, off); off += 8;
                int nl = b[off++]; if (off + nl > b.Length) return false;
                string nm = Encoding.UTF8.GetString(b, off, nl); off += nl;
                if (off + 4 > b.Length) return false;
                int lvl = (b[off] << 8) | b[off + 1]; off += 2;
                int mhp = (b[off] << 8) | b[off + 1]; off += 2;
                members.Add(new PartyMember { Id = id, Name = nm, Level = lvl, MaxHp = mhp });
            }
            return true;
        }

        public static bool TryDecodePartyInvite(byte[] b, out string inviterName)
        {
            inviterName = "";
            if (b.Length < 2 || b[0] != OpPartyInvite) return false;
            int nl = b[1]; if (2 + nl > b.Length) return false;
            inviterName = Encoding.UTF8.GetString(b, 2, nl);
            return true;
        }

        // --- Faz 16: Lonca ---
        static byte[] guildName(byte action, string name)
        {
            var d = Encoding.UTF8.GetBytes(name ?? "");
            if (d.Length > 255) Array.Resize(ref d, 255);
            var b = new byte[3 + d.Length];
            b[0] = OpGuild; b[1] = action; b[2] = (byte)d.Length;
            Array.Copy(d, 0, b, 3, d.Length);
            return b;
        }
        public static byte[] EncodeGuildInfoReq() => new byte[] { OpGuild, 0 };
        public static byte[] EncodeGuildCreate(string name) => guildName(1, name);
        public static byte[] EncodeGuildJoin(string name) => guildName(2, name);
        public static byte[] EncodeGuildLeave() => new byte[] { OpGuild, 3 };
        public static byte[] EncodeGuildDepositGold(long amt) { var b = new byte[10]; b[0] = OpGuild; b[1] = 4; WriteU64BE(b, 2, (ulong)amt); return b; }
        public static byte[] EncodeGuildWithdrawGold(long amt) { var b = new byte[10]; b[0] = OpGuild; b[1] = 5; WriteU64BE(b, 2, (ulong)amt); return b; }
        public static byte[] EncodeGuildDepositItem(string defID, int qty)
        {
            var d = Encoding.UTF8.GetBytes(defID ?? "");
            if (d.Length > 255) Array.Resize(ref d, 255);
            var b = new byte[3 + d.Length + 2];
            b[0] = OpGuild; b[1] = 6; b[2] = (byte)d.Length;
            Array.Copy(d, 0, b, 3, d.Length);
            int off = 3 + d.Length;
            b[off] = (byte)(qty >> 8); b[off + 1] = (byte)qty;
            return b;
        }
        public static byte[] EncodeGuildPromote(string name) => guildName(8, name);
        public static byte[] EncodeGuildDemote(string name) => guildName(9, name);
        public static byte[] EncodeGuildKick(string name) => guildName(10, name);

        // --- Faz 26: chat ---
        public static byte[] EncodeChat(byte scope, string text)
        {
            var d = Encoding.UTF8.GetBytes(text ?? "");
            if (d.Length > 200) Array.Resize(ref d, 200);
            var b = new byte[3 + d.Length];
            b[0] = OpChatSend; b[1] = scope; b[2] = (byte)d.Length;
            Array.Copy(d, 0, b, 3, d.Length);
            return b;
        }
        public static bool TryDecodeChat(byte[] b, out byte scope, out string sender, out string text)
        {
            scope = 0; sender = ""; text = "";
            if (b.Length < 2 || b[0] != OpChat) return false;
            scope = b[1];
            int off = 2;
            int sl = b[off++]; if (off + sl > b.Length) return false;
            sender = Encoding.UTF8.GetString(b, off, sl); off += sl;
            if (off >= b.Length) return false;
            int tl = b[off++]; if (off + tl > b.Length) return false;
            text = Encoding.UTF8.GetString(b, off, tl);
            return true;
        }

        // --- Faz 27: leaderboard ---
        public static byte[] EncodeLeaderReq() => new byte[] { OpLeaderReq };

        // Faz A: attribute puanı harca (which: 0=Str 1=Dex 2=Int 3=Vit 4=Wis 5=Luck)
        public static byte[] EncodeSpendAttr(byte which) => new byte[] { OpSpendAttr, which };

        // Faz C: uzmanlık seç [0x2C][u8 len][specID]
        public static byte[] EncodeSelectSpec(string specID)
        {
            var s = System.Text.Encoding.UTF8.GetBytes(specID ?? "");
            var b = new byte[2 + s.Length];
            b[0] = OpSelectSpec; b[1] = (byte)s.Length;
            System.Array.Copy(s, 0, b, 2, s.Length);
            return b;
        }

        // Faz C: ustalık detayı [0x2B][familyName][activeSpecID][u8 level][u8 unlockLvl][u8 n] + n×(id,name,u8 dmg,desc)
        public struct SpecOption { public string Id, Name, Desc; public int DmgBonus; }
        public struct MasteryDetail
        {
            public string Family, ActiveSpec;
            public int Level, UnlockLevel, GM, Legend; // Faz G: Grandmaster + Legend rank
            public System.Collections.Generic.List<SpecOption> Options;
        }
        public static bool TryDecodeMasteryDetail(byte[] b, out MasteryDetail d)
        {
            d = default;
            if (b.Length < 2 || b[0] != OpMasteryDetail) return false;
            int off = 1;
            string Str() { int n = b[off++]; var s = System.Text.Encoding.UTF8.GetString(b, off, n); off += n; return s; }
            d.Family = Str();
            d.ActiveSpec = Str();
            if (off + 4 > b.Length) return false;
            d.Level = b[off++]; d.UnlockLevel = b[off++];
            d.GM = b[off++]; d.Legend = b[off++]; // Faz G
            int count = b[off++];
            d.Options = new System.Collections.Generic.List<SpecOption>(count);
            for (int i = 0; i < count; i++)
            {
                var o = new SpecOption();
                o.Id = Str(); o.Name = Str();
                o.DmgBonus = b[off++];
                o.Desc = Str();
                d.Options.Add(o);
            }
            return true;
        }

        // Faz A: attribute paketi [0x29] + 6 attr(u16) + puan(u16) + maxMana,def,crit×10,atkSpd×10,cdr×10,loot×10(u16)
        public struct AttrData
        {
            public int Str, Dex, Int, Vit, Wis, Luck, Points;
            public int MaxMana, Defense;
            public float CritChance, AttackSpeed, CooldownRed, LootFind;
        }
        public static bool TryDecodeAttributes(byte[] b, out AttrData a)
        {
            a = default;
            if (b.Length < 27 || b[0] != OpAttributes) return false;
            int off = 1;
            int U16() { int v = (b[off] << 8) | b[off + 1]; off += 2; return v; }
            a.Str = U16(); a.Dex = U16(); a.Int = U16(); a.Vit = U16(); a.Wis = U16(); a.Luck = U16();
            a.Points = U16();
            a.MaxMana = U16(); a.Defense = U16();
            a.CritChance = U16() / 10f; a.AttackSpeed = U16() / 10f; a.CooldownRed = U16() / 10f; a.LootFind = U16() / 10f;
            return true;
        }
        public static bool TryDecodeLeaderboard(byte[] b, out List<LeaderRow> rows)
        {
            rows = null;
            if (b.Length < 2 || b[0] != OpLeaderboard) return false;
            int n = b[1];
            rows = new List<LeaderRow>(n);
            int off = 2;
            for (int i = 0; i < n; i++)
            {
                if (off >= b.Length) return false;
                int nl = b[off++]; if (off + nl + 2 > b.Length) return false;
                string nm = Encoding.UTF8.GetString(b, off, nl); off += nl;
                int lvl = (b[off] << 8) | b[off + 1]; off += 2;
                rows.Add(new LeaderRow { Name = nm, Level = lvl });
            }
            return true;
        }

        public static byte[] EncodeGuildWithdrawItem(string instID)
        {
            var d = Encoding.UTF8.GetBytes(instID ?? "");
            if (d.Length > 255) Array.Resize(ref d, 255);
            var b = new byte[3 + d.Length];
            b[0] = OpGuild; b[1] = 7; b[2] = (byte)d.Length;
            Array.Copy(d, 0, b, 3, d.Length);
            return b;
        }

        public static bool TryDecodeGuildInfo(byte[] b, out bool hasGuild, out string name, out bool isLeader, out long bankGold, out List<GuildMemberC> members)
        {
            hasGuild = false; name = ""; isLeader = false; bankGold = 0; members = new List<GuildMemberC>();
            if (b.Length < 2 || b[0] != OpGuildInfo) return false;
            if (b[1] == 0) return true; // loncasız
            hasGuild = true;
            int off = 2;
            int nl = b[off++]; if (off + nl > b.Length) return false;
            name = Encoding.UTF8.GetString(b, off, nl); off += nl;
            if (off + 1 + 8 + 1 > b.Length) return false;
            isLeader = b[off++] != 0;
            bankGold = (long)ReadU64BE(b, off); off += 8;
            int nm = b[off++];
            for (int i = 0; i < nm; i++)
            {
                if (off >= b.Length) return false;
                int ml = b[off++]; if (off + ml + 1 > b.Length) return false;
                string mn = Encoding.UTF8.GetString(b, off, ml); off += ml;
                byte rank = b[off++];
                members.Add(new GuildMemberC { Name = mn, Rank = rank });
            }
            return true;
        }

        // [0x21][u8 profLen][profession][u16 level][i64 xp][i64 xpNext][u16 bonus]
        public static bool TryDecodeNotice(byte[] b, out string text)
        {
            text = "";
            if (b.Length < 2 || b[0] != OpNotice) return false;
            int n = b[1]; if (2 + n > b.Length) return false;
            text = Encoding.UTF8.GetString(b, 2, n);
            return true;
        }

        public static bool TryDecodeProfMastery(byte[] b, out string prof, out int level, out long xp, out long xpNext, out int bonus)
        {
            prof = ""; level = 0; xp = 0; xpNext = 0; bonus = 0;
            if (b.Length < 2 || b[0] != OpProfMastery) return false;
            int off = 1;
            int pl = b[off++]; if (off + pl > b.Length) return false;
            prof = Encoding.UTF8.GetString(b, off, pl); off += pl;
            if (off + 2 + 8 + 8 + 2 > b.Length) return false;
            level = (b[off] << 8) | b[off + 1]; off += 2;
            xp = (long)ReadU64BE(b, off); off += 8;
            xpNext = (long)ReadU64BE(b, off); off += 8;
            bonus = (b[off] << 8) | b[off + 1];
            return true;
        }

        public static bool TryDecodeGuildBank(byte[] b, out List<GuildBankItem> items)
        {
            items = null;
            if (b.Length < 3 || b[0] != OpGuildBank) return false;
            int count = (b[1] << 8) | b[2];
            items = new List<GuildBankItem>(count);
            int off = 3;
            for (int i = 0; i < count; i++)
            {
                if (off >= b.Length) return false;
                int il = b[off++]; if (off + il > b.Length) return false;
                string inst = Encoding.UTF8.GetString(b, off, il); off += il;
                if (off >= b.Length) return false;
                int nl = b[off++]; if (off + nl > b.Length) return false;
                string nm = Encoding.UTF8.GetString(b, off, nl); off += nl;
                if (off + 3 > b.Length) return false;
                byte rar = b[off++];
                int qty = (b[off] << 8) | b[off + 1]; off += 2;
                items.Add(new GuildBankItem { InstID = inst, Name = nm, Rarity = rar, Qty = qty });
            }
            return true;
        }

        // [0x1C][u8 dangerous] — oyuncu öldü (Faz 14)
        public static bool TryDecodeDeath(byte[] b, out bool dangerous)
        {
            dangerous = false;
            if (b.Length < 2 || b[0] != OpDeath) return false;
            dangerous = b[1] != 0;
            return true;
        }

        // [0x1B][i16 wDur][i16 wMax][i16 aDur][i16 aMax][u16 itemPower] — durabilite + eşya gücü (Faz 13)
        public static bool TryDecodeGear(byte[] b, out int wDur, out int wMax, out int aDur, out int aMax, out int itemPower, out int atkRange)
        {
            wDur = 0; wMax = 0; aDur = 0; aMax = 0; itemPower = 0; atkRange = 3;
            if (b.Length < 13 || b[0] != OpGear) return false;
            wDur = (short)((b[1] << 8) | b[2]);
            wMax = (short)((b[3] << 8) | b[4]);
            aDur = (short)((b[5] << 8) | b[6]);
            aMax = (short)((b[7] << 8) | b[8]);
            itemPower = (b[9] << 8) | b[10];
            atkRange = (b[11] << 8) | b[12];
            return true;
        }

        public static byte[] EncodeMarketBrowse() => new byte[] { OpMarketBrowse };

        public static byte[] EncodeMarketBuy(long listingID)
        {
            var b = new byte[9];
            b[0] = OpMarketBuy;
            WriteU64BE(b, 1, (ulong)listingID);
            return b;
        }

        public static byte[] EncodeMarketList(string defID, int qty, long price)
        {
            var d = Encoding.UTF8.GetBytes(defID ?? "");
            if (d.Length > 255) Array.Resize(ref d, 255);
            var b = new byte[2 + d.Length + 10];
            b[0] = OpMarketList;
            b[1] = (byte)d.Length;
            Array.Copy(d, 0, b, 2, d.Length);
            int off = 2 + d.Length;
            b[off] = (byte)(qty >> 8); b[off + 1] = (byte)qty;
            WriteU64BE(b, off + 2, (ulong)price);
            return b;
        }

        public static bool TryDecodeMarket(byte[] b, out List<MarketEntry> entries)
        {
            entries = null;
            if (b.Length < 3 || b[0] != OpMarket) return false;
            int count = (b[1] << 8) | b[2];
            entries = new List<MarketEntry>(count);
            int off = 3;
            for (int i = 0; i < count; i++)
            {
                if (off + 8 > b.Length) return false;
                long id = (long)ReadU64BE(b, off); off += 8;
                int nmLen = b[off++]; if (off + nmLen > b.Length) return false;
                string name = Encoding.UTF8.GetString(b, off, nmLen); off += nmLen;
                if (off + 1 + 2 + 8 > b.Length) return false;
                byte rar = b[off++];
                int qty = (b[off] << 8) | b[off + 1]; off += 2;
                long price = (long)ReadU64BE(b, off); off += 8;
                int slLen = b[off++]; if (off + slLen > b.Length) return false;
                string seller = Encoding.UTF8.GetString(b, off, slLen); off += slLen;
                entries.Add(new MarketEntry { Id = id, Name = name, Rarity = rar, Qty = qty, Price = price, Seller = seller });
            }
            return true;
        }

        public static byte[] EncodeCraft(string recipeID)
        {
            var d = Encoding.UTF8.GetBytes(recipeID ?? "");
            if (d.Length > 255) Array.Resize(ref d, 255);
            var b = new byte[2 + d.Length];
            b[0] = OpCraft;
            b[1] = (byte)d.Length;
            Array.Copy(d, 0, b, 2, d.Length);
            return b;
        }

        public static bool TryDecodeRecipes(byte[] b, out List<RecipeInfo> recipes)
        {
            recipes = null;
            if (b.Length < 3 || b[0] != OpRecipes) return false;
            int count = (b[1] << 8) | b[2];
            recipes = new List<RecipeInfo>(count);
            int off = 3;
            for (int i = 0; i < count; i++)
            {
                if (off >= b.Length) return false;
                int idLen = b[off++]; if (off + idLen > b.Length) return false;
                string id = Encoding.UTF8.GetString(b, off, idLen); off += idLen;
                int nmLen = b[off++]; if (off + nmLen > b.Length) return false;
                string name = Encoding.UTF8.GetString(b, off, nmLen); off += nmLen;
                int onLen = b[off++]; if (off + onLen > b.Length) return false;
                string outName = Encoding.UTF8.GetString(b, off, onLen); off += onLen;
                if (off + 2 > b.Length) return false;
                byte rar = b[off++];
                int nin = b[off++];
                var inputs = new List<Ingredient>(nin);
                for (int j = 0; j < nin; j++)
                {
                    if (off >= b.Length) return false;
                    int inLen = b[off++]; if (off + inLen > b.Length) return false;
                    string inName = Encoding.UTF8.GetString(b, off, inLen); off += inLen;
                    if (off + 2 > b.Length) return false;
                    int q = (b[off] << 8) | b[off + 1]; off += 2;
                    inputs.Add(new Ingredient { Name = inName, Qty = q });
                }
                recipes.Add(new RecipeInfo { Id = id, Name = name, OutName = outName, OutRarity = rar, Inputs = inputs });
            }
            return true;
        }

        public static byte[] EncodeGather(ulong nodeID)
        {
            var b = new byte[9];
            b[0] = OpGather;
            WriteU64BE(b, 1, nodeID);
            return b;
        }

        public static byte[] EncodeEquip(string defID)
        {
            var d = Encoding.UTF8.GetBytes(defID ?? "");
            if (d.Length > 255) Array.Resize(ref d, 255);
            var b = new byte[2 + d.Length];
            b[0] = OpEquip;
            b[1] = (byte)d.Length;
            Array.Copy(d, 0, b, 2, d.Length);
            return b;
        }

        public static bool TryDecodeEquipment(byte[] b, out string weaponDef, out string armorDef)
        {
            weaponDef = ""; armorDef = "";
            if (b.Length < 2 || b[0] != OpEquipment) return false;
            int off = 1;
            int wl = b[off++]; if (off + wl > b.Length) return false;
            weaponDef = Encoding.UTF8.GetString(b, off, wl); off += wl;
            if (off >= b.Length) return false;
            int al = b[off++]; if (off + al > b.Length) return false;
            armorDef = Encoding.UTF8.GetString(b, off, al);
            return true;
        }

        public static bool TryDecodeInventory(byte[] b, out List<InvItem> items)
        {
            items = null;
            if (b.Length < 3 || b[0] != OpInventory) return false;
            int count = (b[1] << 8) | b[2];
            items = new List<InvItem>(count);
            int off = 3;
            for (int i = 0; i < count; i++)
            {
                if (off >= b.Length) return false;
                int idLen = b[off++]; if (off + idLen > b.Length) return false;
                string defID = Encoding.UTF8.GetString(b, off, idLen); off += idLen;
                if (off >= b.Length) return false;
                int nmLen = b[off++]; if (off + nmLen > b.Length) return false;
                string name = Encoding.UTF8.GetString(b, off, nmLen); off += nmLen;
                if (off + 3 > b.Length) return false;
                byte rar = b[off++];
                int qty = (b[off] << 8) | b[off + 1]; off += 2;
                items.Add(new InvItem { DefID = defID, Name = name, Rarity = rar, Qty = qty });
            }
            return true;
        }

        public static bool TryDecodeLoot(byte[] b, out string name, out byte rarity, out int qty)
        {
            name = null; rarity = 0; qty = 0;
            if (b.Length < 2 || b[0] != OpLoot) return false;
            int nmLen = b[1];
            if (2 + nmLen + 3 > b.Length) return false;
            name = Encoding.UTF8.GetString(b, 2, nmLen);
            int off = 2 + nmLen;
            rarity = b[off++];
            qty = (b[off] << 8) | b[off + 1];
            return true;
        }

        public static bool TryDecodeSnapshot(byte[] b, out uint tick, out List<Entity> ents)
        {
            tick = 0; ents = null;
            if (b.Length < 7 || b[0] != OpSnapshot) return false;
            tick = ReadU32BE(b, 1);
            int count = (b[5] << 8) | b[6];
            ents = new List<Entity>(count);
            int off = 7;
            for (int i = 0; i < count; i++)
            {
                if (off + SnapRowSize > b.Length) return false;
                ents.Add(new Entity
                {
                    Id = ReadU64BE(b, off),
                    Kind = b[off + 8],
                    Hp = (short)((b[off + 9] << 8) | b[off + 10]),
                    MaxHp = (short)((b[off + 11] << 8) | b[off + 12]),
                    X = ReadFloatBE(b, off + 13),
                    Y = ReadFloatBE(b, off + 17),
                    SubKind = b[off + 21],
                    Status = b[off + 22],
                });
                off += SnapRowSize;
            }
            return true;
        }

        // ---- big-endian yardımcıları ----
        static void WriteFloatBE(byte[] b, int off, float v)
        {
            var bytes = BitConverter.GetBytes(v);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            Array.Copy(bytes, 0, b, off, 4);
        }
        static void WriteU64BE(byte[] b, int off, ulong v)
        {
            for (int i = 0; i < 8; i++) b[off + i] = (byte)(v >> (8 * (7 - i)));
        }
        static float ReadFloatBE(byte[] b, int off)
        {
            var tmp = new byte[4];
            Array.Copy(b, off, tmp, 0, 4);
            if (BitConverter.IsLittleEndian) Array.Reverse(tmp);
            return BitConverter.ToSingle(tmp, 0);
        }
        static uint ReadU32BE(byte[] b, int off) =>
            (uint)((b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3]);
        static ulong ReadU64BE(byte[] b, int off)
        {
            ulong v = 0;
            for (int i = 0; i < 8; i++) v = (v << 8) | b[off + i];
            return v;
        }
    }
}
