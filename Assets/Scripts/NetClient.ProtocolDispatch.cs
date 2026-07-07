// NetClient.ProtocolDispatch.cs — Faz 2 (İstemci sağlığı) gün 2 dilimi.
// NetClient.cs'in Update()'inden çıkarılan gelen-mesaj çözme/dağıtma bloğu.
// Bilinçli olarak `partial class NetClient` — tam bir olay-veriyolu (event-bus)
// mimarisine çevirmek yerine (ki bu 30+ Protocol.TryDecodeXxx dalını, çoğu
// ses/görsel/başka-alt-sistem yan etkisi olan, derleyicisiz elle taşımayı
// gerektirirdi — regresyon riski yüksek), tek tip kalan aynı alanlara sahip bir
// partial class ile fiziksel dosya ayrımı yapıldı. Davranış birebir korunuyor;
// hiçbir mantık değişmedi, sadece taşındı. (Bkz. Docs/DECISION_LOG.md ilgili not.)
using System.Collections.Generic;
using UnityEngine;

namespace MMO
{
    public partial class NetClient
    {
        void ProcessInboundMessages()
        {
            while (net.Inbound.TryDequeue(out var msg))
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
                        PlaySfx(lootClip, 0.6f);
                        Debug.Log($"[NetClient] +{amt} altın! (toplam {myGold})");
                    }
                }
                else if (Protocol.TryDecodeStats(msg, out var lvl, out var xp, out var xpNext, out var maxHp, out var dmg))
                {
                    if (lvl > myLevel) { Debug.Log($"[NetClient] SEVİYE ATLADIN -> {lvl}!"); PlaySfx(levelUpClip); }
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
                    PlaySfx(deathClip);
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
                    PlaySfx(lootClip, 0.6f);
                    Debug.Log($"[NetClient] loot: +{lq} {ln}");
                }
            }
        }
    }
}
