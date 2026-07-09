// NetClient.HUD.cs — Faz 5 (Sunum) ilk dilim: temalı, fare-odaklı arayüz.
// Cilt 15 paleti (bkz. NetClient.UISkin.cs). Bu dosya HUD okumalarını (can/mana/altın/
// bölge/popup) ve panelleri çizer. Paneller artık UIPanel() ile çerçeveli/altın-başlıklı;
// her panelde (×) kapat butonu var; TÜM menüler alt araç çubuğundan açılır (DrawToolbar).
// Ekranda tuş-komut yazısı YOK (eski "I çanta / C üretim ..." yardım satırı kaldırıldı);
// parti davet/kabul/ayrıl da artık butonlarla. Mantık/paket çağrıları birebir korundu.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MMO
{
    public partial class NetClient
    {
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

        void OnGUI()
        {
            _uiRects.Clear(); // her karede açık-UI dikdörtgenleri yeniden toplanır (dünya-tıklama koruması)
            EnsureUI();

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
                DrawFloaters(wcam); // Faz 5 his: pop-ölçekli, konturlu, kayan hasar sayıları
            }

            DrawScreenFlash(); // Faz 5 his: seviye/ölüm/diriliş/bölge flaşları (dünya üstü, panel altı)

            // --- sol-üst durum bloğu: hafif parşömen zeminli okunaklı künye ---
            var statBg = new Rect(10, 8, 360, 150);
            var pv0 = GUI.color; GUI.color = new Color(colPanel.r, colPanel.g, colPanel.b, 0.55f);
            GUI.DrawTexture(statBg, _txPanel); GUI.color = pv0;

            var gold = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            gold.normal.textColor = colGoldTx;
            GUI.Label(new Rect(18, 12, 500, 40), playerName + "  —  Altın: " + myGold, gold);

            // Faz 11: mevcut bölge (üst orta)
            var zn = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            zn.normal.textColor = colLapis;
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
            lvl.normal.textColor = colLapis;
            GUI.Label(new Rect(18, 42, 500, 30), "Seviye " + myLevel + "   XP " + myXp + " / " + myXpNext, lvl);

            var hp = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            hp.normal.textColor = myHp > lowHp ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.3f, 0.3f);
            GUI.Label(new Rect(18, 68, 170, 30), "Can: " + myHp + " / " + myMaxHp, hp);

            // Faz D: mana (mavi)
            var mana = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            mana.normal.textColor = new Color(0.45f, 0.65f, 1f);
            GUI.Label(new Rect(18, 94, 170, 28), "Mana: " + myMana + " / " + myMaxMana, mana);

            var dmg = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            dmg.normal.textColor = new Color(1f, 0.7f, 0.4f);
            GUI.Label(new Rect(198, 68, 300, 30), "Hasar: " + myDamage, dmg);

            // Faz 10: aktif silah sınıfı ustalığı
            var mst = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
            mst.normal.textColor = new Color(0.85f, 0.7f, 1f);
            string mstTxt = ClassName(masteryClass) + " Ustalığı  Sv " + masteryLevel + "  (+" + masteryBonus + " hasar)";
            if (masteryXpNext > 0) mstTxt += "   " + masteryXp + " / " + masteryXpNext;
            else mstTxt += "   (MAX)";
            GUI.Label(new Rect(18, 120, 600, 28), mstTxt, mst);

            if (masteryPopupTimer > 0f)
            {
                var mp = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                mp.normal.textColor = new Color(0.8f, 0.6f, 1f);
                GUI.Label(new Rect(0, 174, Screen.width, 34), ClassName(masteryClass) + " Ustalığı " + masteryPopupLevel + "!", mp);
            }

            // Faz 13: eşya gücü + durabilite (kırıksa kırmızı) — "R: tamir" ipucu kaldırıldı (araç çubuğu: Tamir)
            var gear = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            bool wBroken = weaponMaxDur > 0 && weaponDur <= 0;
            bool aBroken = armorMaxDur > 0 && armorDur <= 0;
            gear.normal.textColor = (wBroken || aBroken) ? new Color(1f, 0.35f, 0.35f) : new Color(0.75f, 0.85f, 0.95f);
            string wTxt = weaponMaxDur > 0 ? ("  Silah " + weaponDur + "/" + weaponMaxDur + (wBroken ? " KIRIK!" : "")) : "";
            string aTxt = armorMaxDur > 0 ? ("  Zırh " + armorDur + "/" + armorMaxDur + (aBroken ? " KIRIK!" : "")) : "";
            GUI.Label(new Rect(18, 146, 700, 26), "Eşya Gücü " + itemPower + wTxt + aTxt, gear);

            if (repairMsgTimer > 0f)
            {
                var rs = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                rs.normal.textColor = new Color(1f, 0.9f, 0.4f);
                GUI.Label(new Rect(0, 206, Screen.width, 28), repairMsg, rs);
            }

            if (rewardPopupTimer > 0f)
            {
                var pop = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
                pop.normal.textColor = new Color(0.3f, 1f, 0.3f);
                GUI.Label(new Rect(380, 64, 300, 34), "+" + lastReward + " altın!", pop);
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

            // ---------------- PANELLER (temalı, kapat butonlu) ----------------

            if (showInventory)
            {
                float w = 340;
                int rows = Mathf.Max(inventory.Count, 1);
                float h = 74 + rows * 26;
                float px = Screen.width - w - 20, py = 20;
                var r = new Rect(px, py, w, h); RegisterUI(r);
                if (UIPanel(r, "Çanta (" + inventory.Count + ")")) showInventory = false;

                GUI.Label(new Rect(px + 14, py + 40, 150, 20), "Satış fiyatı (adet):", uiSmall);
                sellPriceStr = GUI.TextField(new Rect(px + 150, py + 39, 70, 22), sellPriceStr, 9, uiField);

                if (inventory.Count == 0)
                    GUI.Label(new Rect(px + 14, py + 70, w - 24, 20), "(boş)", uiSmall);

                for (int i = 0; i < inventory.Count; i++)
                {
                    var it = inventory[i];
                    bool equipped = (it.DefID == equippedWeapon || it.DefID == equippedArmor);
                    uiItemBtn.normal.textColor = RarityColor(it.Rarity);
                    uiItemBtn.hover.textColor = RarityColor(it.Rarity);
                    float ry = py + 68 + i * 26;
                    string label = (equipped ? "★ " : "") + it.Name + "   x" + it.Qty;
                    if (GUI.Button(new Rect(px + 10, ry, w - 92, 24), label, uiItemBtn))
                        net.Outbound.Enqueue(Protocol.EncodeEquip(it.DefID));
                    if (GUI.Button(new Rect(px + w - 78, ry, 68, 24), "Sat", uiBtn))
                    {
                        long pr; long.TryParse(sellPriceStr, out pr);
                        if (pr > 0) net.Outbound.Enqueue(Protocol.EncodeMarketList(it.DefID, 1, pr));
                    }
                }
            }

            if (showCrafting)
            {
                float cw = 380;
                int cn = Mathf.Max(recipes.Count, 1);
                float ch = 44 + cn * 26;
                float cx = 15, cy = 210;
                var r = new Rect(cx, cy, cw, ch); RegisterUI(r);
                if (UIPanel(r, "Üretim")) showCrafting = false;

                for (int i = 0; i < recipes.Count; i++)
                {
                    var rc = recipes[i];
                    string inp = "";
                    for (int j = 0; j < rc.Inputs.Count; j++) { if (j > 0) inp += ", "; inp += rc.Inputs[j].Name + " x" + rc.Inputs[j].Qty; }
                    uiItemBtn.normal.textColor = RarityColor(rc.OutRarity);
                    uiItemBtn.hover.textColor = RarityColor(rc.OutRarity);
                    if (GUI.Button(new Rect(cx + 10, cy + 38 + i * 26, cw - 20, 24), rc.OutName + "  ←  " + inp, uiItemBtn))
                        net.Outbound.Enqueue(Protocol.EncodeCraft(rc.Id));
                }
            }

            if (showMarket)
            {
                float mw = 470;
                int mn = Mathf.Max(market.Count, 1);
                float mh = 44 + mn * 24;
                float mx = Screen.width / 2f - mw / 2f, my = 60;
                var r = new Rect(mx, my, mw, mh); RegisterUI(r);
                if (UIPanel(r, "Pazar")) showMarket = false;

                if (market.Count == 0)
                    GUI.Label(new Rect(mx + 14, my + 40, mw - 24, 20), "(ilan yok — bir şey sat!)", uiSmall);

                for (int i = 0; i < market.Count; i++)
                {
                    var e = market[i];
                    uiItemBtn.normal.textColor = RarityColor(e.Rarity);
                    uiItemBtn.hover.textColor = RarityColor(e.Rarity);
                    string label = e.Name + " x" + e.Qty + "   —   " + e.Price + " altın   (" + e.Seller + ")";
                    if (GUI.Button(new Rect(mx + 10, my + 38 + i * 24, mw - 20, 22), label, uiItemBtn))
                        net.Outbound.Enqueue(Protocol.EncodeMarketBuy(e.Id));
                }
            }

            // Faz 16: lonca paneli
            if (showGuild)
            {
                float gw = 440, gx = Screen.width / 2f - gw / 2f, gy = 50;
                float gh = hasGuild ? (100 + guildMembers.Count * (guildIsLeader ? 24 : 18) + guildBank.Count * 24 + Mathf.Min(inventory.Count, 8) * 24 + 60) : 120;
                var r = new Rect(gx, gy, gw, gh); RegisterUI(r);
                if (UIPanel(r, hasGuild ? ("Lonca — " + guildName + (guildIsLeader ? " (lider)" : "")) : "Lonca")) showGuild = false;

                if (!hasGuild)
                {
                    GUI.Label(new Rect(gx + 12, gy + 42, gw - 20, 22), "Bir loncan yok — kur ya da katıl:", uiBody);
                    GUI.Label(new Rect(gx + 12, gy + 70, 70, 22), "İsim:", uiBody);
                    guildNameInput = GUI.TextField(new Rect(gx + 70, gy + 69, 180, 22), guildNameInput, 24, uiField);
                    if (GUI.Button(new Rect(gx + 258, gy + 69, 80, 22), "Kur (100)", uiBtnGold))
                        net.Outbound.Enqueue(Protocol.EncodeGuildCreate(guildNameInput));
                    if (GUI.Button(new Rect(gx + 342, gy + 69, 80, 22), "Katıl", uiBtn))
                        net.Outbound.Enqueue(Protocol.EncodeGuildJoin(guildNameInput));
                }
                else
                {
                    GUI.Label(new Rect(gx + 12, gy + 38, gw - 20, 20), "Kasa: " + guildBankGold + " altın", uiGold);
                    if (GUI.Button(new Rect(gx + gw - 78, gy + 38, 66, 20), "Ayrıl", uiBtn))
                        net.Outbound.Enqueue(Protocol.EncodeGuildLeave());
                    float yy = gy + 62;
                    if (!guildIsLeader)
                    {
                        GUI.Label(new Rect(gx + 12, yy, gw - 20, 18), "Üyeler: " + MembersStr(), uiBody); yy += 20;
                    }
                    else
                    {
                        for (int i = 0; i < guildMembers.Count; i++)
                        {
                            var m = guildMembers[i];
                            string tag = m.Rank == 2 ? " ★" : (m.Rank == 1 ? " ◆" : "");
                            GUI.Label(new Rect(gx + 12, yy, 200, 20), m.Name + tag, uiBody);
                            if (m.Name != playerName && m.Rank != 2)
                            {
                                if (m.Rank == 0 && GUI.Button(new Rect(gx + 215, yy, 62, 20), "Terfi", uiBtn)) net.Outbound.Enqueue(Protocol.EncodeGuildPromote(m.Name));
                                if (m.Rank == 1 && GUI.Button(new Rect(gx + 215, yy, 62, 20), "İndir", uiBtn)) net.Outbound.Enqueue(Protocol.EncodeGuildDemote(m.Name));
                                if (GUI.Button(new Rect(gx + 281, yy, 50, 20), "Kov", uiBtn)) net.Outbound.Enqueue(Protocol.EncodeGuildKick(m.Name));
                            }
                            yy += 22;
                        }
                    }
                    GUI.Label(new Rect(gx + 12, yy, 60, 22), "Altın:", uiBody);
                    guildGoldInput = GUI.TextField(new Rect(gx + 64, yy, 80, 22), guildGoldInput, 9, uiField);
                    long gamt; long.TryParse(guildGoldInput, out gamt);
                    if (GUI.Button(new Rect(gx + 150, yy, 80, 22), "Yatır", uiBtn) && gamt > 0)
                        net.Outbound.Enqueue(Protocol.EncodeGuildDepositGold(gamt));
                    if (GUI.Button(new Rect(gx + 234, yy, 80, 22), "Çek", uiBtn) && gamt > 0)
                        net.Outbound.Enqueue(Protocol.EncodeGuildWithdrawGold(gamt));
                    yy += 28;
                    GUI.Label(new Rect(gx + 12, yy, gw - 20, 18), "Kasa eşyaları (tıkla = çek):", uiSmall); yy += 20;
                    for (int i = 0; i < guildBank.Count; i++)
                    {
                        var bi = guildBank[i];
                        uiItemBtn.normal.textColor = RarityColor(bi.Rarity); uiItemBtn.hover.textColor = RarityColor(bi.Rarity);
                        if (GUI.Button(new Rect(gx + 16, yy, gw - 32, 22), bi.Name + " x" + bi.Qty, uiItemBtn))
                            net.Outbound.Enqueue(Protocol.EncodeGuildWithdrawItem(bi.InstID));
                        yy += 24;
                    }
                    yy += 4;
                    GUI.Label(new Rect(gx + 12, yy, gw - 20, 18), "Çantandan yatır (tıkla):", uiSmall); yy += 20;
                    for (int i = 0; i < inventory.Count && i < 8; i++)
                    {
                        var it = inventory[i];
                        uiItemBtn.normal.textColor = colParch; uiItemBtn.hover.textColor = colParch;
                        if (GUI.Button(new Rect(gx + 16, yy, gw - 32, 22), it.Name + " x" + it.Qty, uiItemBtn))
                            net.Outbound.Enqueue(Protocol.EncodeGuildDepositItem(it.DefID, it.Qty));
                        yy += 24;
                    }
                }
            }

            // Faz 17: meslek (Destiny Board) paneli
            if (showProf)
            {
                string[] order = { "mining", "woodcutting", "herbalism", "fishing", "blacksmith", "refinery", "leatherworker", "alchemist" };
                float jw = 380, jx = 20, jy = 210, jh = 44 + order.Length * 22;
                var r = new Rect(jx, jy, jw, jh); RegisterUI(r);
                if (UIPanel(r, "Meslekler")) showProf = false;

                for (int i = 0; i < order.Length; i++)
                {
                    string key = order[i];
                    ProfState st; professions.TryGetValue(key, out st);
                    bool craft = i >= 4;
                    var jl = st.Level > 0 ? uiBody : uiSmall;
                    string eff = craft ? ("+%" + st.Bonus + " iade") : ("+" + st.Bonus + " verim");
                    string xp = st.XpNext > 0 ? (st.Xp + "/" + st.XpNext) : "MAX";
                    GUI.Label(new Rect(jx + 14, jy + 38 + i * 22, jw - 24, 20),
                        ProfName(key) + "  Sv " + st.Level + "   " + xp + "   " + eff, jl);
                }
            }

            // Faz 24: görev paneli
            if (showQuests)
            {
                float qw = 460, qx = Screen.width / 2f - qw / 2f, qy = 70;
                float qh = 44 + quests.Count * 30;
                var r = new Rect(qx, qy, qw, qh); RegisterUI(r);
                if (UIPanel(r, "Görevler")) showQuests = false;

                for (int i = 0; i < quests.Count; i++)
                {
                    var q = quests[i];
                    float ry = qy + 40 + i * 30;
                    string suffix = q.State == 1 ? ("  " + q.Progress + "/" + q.Count) : "";
                    var ql = q.State == 3 ? uiLapis : (q.State == 2 ? uiGold : uiBody);
                    GUI.Label(new Rect(qx + 14, ry, qw - 130, 28), q.Name + suffix, ql);
                    if (q.State == 0) { if (GUI.Button(new Rect(qx + qw - 112, ry, 100, 24), "Kabul Et", uiBtnGold)) net.Outbound.Enqueue(Protocol.EncodeQuestAccept(q.Id)); }
                    else if (q.State == 2) { if (GUI.Button(new Rect(qx + qw - 112, ry, 100, 24), "Ödül Al", uiBtnGold)) net.Outbound.Enqueue(Protocol.EncodeQuestClaim(q.Id)); }
                    else if (q.State == 3) { GUI.Label(new Rect(qx + qw - 112, ry, 100, 24), "✓ Tamam", uiLapis); }
                }
            }

            // Faz 15: parti paneli (sağ-orta) — "L: ayrıl" yerine buton
            if (partyMembers.Count > 0)
            {
                var hpById = new Dictionary<ulong, int>();
                if (lastEnts != null) foreach (var e in lastEnts) if (e.Kind == Protocol.KindPlayer) hpById[e.Id] = e.Hp;
                float pw = 236, px = Screen.width - pw - 20, py = 250;
                float ph = 40 + partyMembers.Count * 22;
                var r = new Rect(px, py, pw, ph); RegisterUI(r);
                if (UIPanel(r, "Parti (" + partyMembers.Count + "/3)")) { } // başlık kapatması yok; içerik altında
                if (GUI.Button(new Rect(px + pw - 74, py + 5, 44, 20), "Ayrıl", uiBtn))
                {
                    net.Outbound.Enqueue(Protocol.EncodePartyLeave());
                    partyMsg = "Partiden ayrıldın"; partyMsgTimer = 2.5f;
                }
                for (int i = 0; i < partyMembers.Count; i++)
                {
                    var m = partyMembers[i];
                    string hpTxt = hpById.TryGetValue(m.Id, out var h) ? (h + "/" + m.MaxHp) : "—";
                    string self = m.Id == myId ? " (sen)" : "";
                    GUI.Label(new Rect(px + 14, py + 38 + i * 22, pw - 24, 20), m.Name + self + "  Sv" + m.Level + "  " + hpTxt, uiBody);
                }
            }
            // parti daveti — "Y: kabul et" yerine Kabul/Reddet butonları
            if (pendingInviteTimer > 0f)
            {
                float iw = 360, ix = Screen.width / 2f - iw / 2f, iy = Screen.height - 190;
                var r = new Rect(ix, iy, iw, 74); RegisterUI(r);
                if (UIPanel(r, "Parti Daveti")) pendingInviteTimer = 0f;
                GUI.Label(new Rect(ix + 14, iy + 36, iw - 28, 20), pendingInviteFrom + " seni partiye çağırıyor.", uiBody);
                if (GUI.Button(new Rect(ix + 14, iy + 46, 150, 22), "Kabul Et", uiBtnGold))
                {
                    net.Outbound.Enqueue(Protocol.EncodePartyAccept());
                    pendingInviteTimer = 0f; pendingInviteFrom = "";
                    partyMsg = "Partiye katıldın"; partyMsgTimer = 2.5f;
                }
                if (GUI.Button(new Rect(ix + iw - 164, iy + 46, 150, 22), "Reddet", uiBtn))
                {
                    pendingInviteTimer = 0f; pendingInviteFrom = "";
                }
            }
            if (partyMsgTimer > 0f)
            {
                var pm2 = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
                pm2.normal.textColor = new Color(0.6f, 0.85f, 1f);
                GUI.Label(new Rect(0, Screen.height - 210, Screen.width, 24), partyMsg, pm2);
            }

            // Faz 21: yetenek barı (alt-orta) — tıklanabilir; 1/2/3 tuşları da çalışır
            if (abilities.Count > 0)
            {
                float bw = 150, gap = 8, total = abilities.Count * (bw + gap), bx = Screen.width / 2f - total / 2f, by = Screen.height - 120;
                for (int i = 0; i < abilities.Count; i++)
                {
                    float x = bx + i * (bw + gap);
                    bool ready = Time.time >= abilityReadyAt[i];
                    var rr = new Rect(x, by, bw, 44); RegisterUI(rr);
                    GUI.DrawTexture(new Rect(rr.x - 1, rr.y - 1, rr.width + 2, rr.height + 2), _txBorder);
                    GUI.DrawTexture(rr, ready ? _txAbil : _txAbilCd);
                    string cd = ready ? "" : "  (" + Mathf.CeilToInt(abilityReadyAt[i] - Time.time) + "s)";
                    GUI.Label(rr, "[" + (i + 1) + "] " + abilities[i].Name + cd, ready ? uiAbilTxt : uiAbilCdTxt);
                    if (GUI.Button(rr, GUIContent.none, uiInvisible)) UseAbility(i);
                }
            }

            // Faz 26: chat log (sol-alt) + giriş kutusu + Gönder butonu ("Enter=gönder" ipucu kaldırıldı)
            for (int i = 0; i < chatLog.Count; i++)
                GUI.Label(new Rect(16, Screen.height - 168 - (chatLog.Count - i) * 16, 620, 16), chatLog[i], uiBody);
            if (chatOpen)
            {
                GUI.SetNextControlName("chatField");
                chatInput = GUI.TextField(new Rect(16, Screen.height - 160, 460, 24), chatInput, 200, uiField);
                RegisterUI(new Rect(16, Screen.height - 160, 620, 24));
                GUI.FocusControl("chatField");
                if (GUI.Button(new Rect(482, Screen.height - 160, 80, 24), "Gönder", uiBtnGold)) { SendChat(); chatOpen = false; }
                GUI.Label(new Rect(570, Screen.height - 158, 220, 20), "/g = global", uiSmall);
            }

            // Faz 27: leaderboard paneli
            if (showLeader)
            {
                float lw = 300, lx = Screen.width / 2f - lw / 2f, ly = 80;
                float lh = 44 + leaderboard.Count * 22;
                var r = new Rect(lx, ly, lw, lh); RegisterUI(r);
                if (UIPanel(r, "Sıralama — En Yüksek Seviye")) showLeader = false;
                for (int i = 0; i < leaderboard.Count; i++)
                    GUI.Label(new Rect(lx + 16, ly + 38 + i * 22, lw - 24, 20),
                        (i + 1) + ". " + leaderboard[i].Name + "   Sv " + leaderboard[i].Level, uiBody);
            }

            if (showCharSheet) DrawCharSheet();

            // Alt araç çubuğu — tüm menüler buradan (tuş gerekmez). En sonda çizilir ki üstte kalsın.
            DrawToolbar();

            if (paused) DrawPauseMenu();
        }

        // Faz 1 (Foundation): ilk-oynanış isim ekranı — bağlantı Inspector'dan değil buradan başlar.
        void DrawNameScreen()
        {
            float w = 480, h = 300;
            float x = Screen.width / 2f - w / 2f, y = Screen.height / 2f - h / 2f;
            var r = new Rect(x, y, w, h);
            if (UIPanel(r, "PROJECT ANATOLIA")) { }

            GUI.Label(new Rect(x + 24, y + 52, 90, 28), "İsim:", uiBody);
            GUI.SetNextControlName("nameField");
            nameInput = GUI.TextField(new Rect(x + 110, y + 50, w - 150, 30), nameInput, 24, uiField);
            GUI.FocusControl("nameField");

            GUI.Label(new Rect(x + 24, y + 86, w - 48, 20), "Aynı isimle girersen karakterin/altının kalıcı kalır.", uiSmall);

            // Faz 2 gün 6: LAN sunucu adresi — Host: localhost, Join: arkadaşının LAN IP'si.
            GUI.Label(new Rect(x + 24, y + 118, 90, 28), "Sunucu:", uiBody);
            GUI.SetNextControlName("serverField");
            serverAddressInput = GUI.TextField(new Rect(x + 110, y + 116, w - 150, 30), serverAddressInput, 64, uiField);

            GUI.Label(new Rect(x + 24, y + 152, w - 48, 40),
                "Kendi bilgisayarında (Host): ws://localhost:8080/ws\nArkadaşına bağlanıyorsan (Join): ws://<onun-IP'si>:8080/ws",
                uiSmall);

            bool enterPressed = Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame;
            if (GUI.Button(new Rect(x + w / 2f - 80, y + h - 52, 160, 38), "Oyna", uiBtnGold) || enterPressed)
            {
                PlaySfx(uiClickClip);
                StartConnecting();
            }
        }

        // Faz 1 (Foundation): "Oyna"dan sonra, WS bağlantısı kurulana kadar (retry dahil) gösterilir.
        void DrawConnectingScreen()
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = colParch;
            GUI.Label(new Rect(0, Screen.height / 2f - 20, Screen.width, 40),
                string.IsNullOrEmpty(net.Status) ? "Bağlanılıyor..." : net.Status, st);
        }

        // Faz 1 (Foundation): ESC ile aç/kapa (ya da araç çubuğundan "Menü") — devam et / çıkış.
        void DrawPauseMenu()
        {
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            float w = 280, h = 150;
            float x = Screen.width / 2f - w / 2f, y = Screen.height / 2f - h / 2f;
            var r = new Rect(x, y, w, h); RegisterUI(r);
            if (UIPanel(r, "Duraklatıldı")) paused = false;

            if (GUI.Button(new Rect(x + 30, y + 46, w - 60, 34), "Devam Et", uiBtnGold))
            {
                PlaySfx(uiClickClip);
                paused = false;
            }
            if (GUI.Button(new Rect(x + 30, y + 90, w - 60, 34), "Çıkış", uiBtn))
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
            var r = new Rect(x, y, w, h); RegisterUI(r);
            if (UIPanel(r, "Karakter  (Sv " + myLevel + ")")) showCharSheet = false;

            var pts = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            pts.normal.textColor = attrData.Points > 0 ? new Color(0.4f, 1f, 0.5f) : new Color(0.6f, 0.6f, 0.6f);

            if (!hasAttr) { GUI.Label(new Rect(x + 16, y + 44, w - 30, 24), "yükleniyor...", uiBody); return; }

            GUI.Label(new Rect(x + 16, y + 40, w - 30, 22), "Dağıtılabilir puan: " + attrData.Points, pts);

            int[] vals = { attrData.Str, attrData.Dex, attrData.Int, attrData.Vit, attrData.Wis, attrData.Luck };
            for (int i = 0; i < 6; i++)
            {
                float ry = y + 70 + i * 34;
                GUI.Label(new Rect(x + 16, ry, 210, 26), attrNames[i] + ":  " + vals[i], uiBody);
                GUI.enabled = attrData.Points > 0;
                if (GUI.Button(new Rect(x + w - 52, ry - 2, 34, 28), "+", uiBtnGold))
                    net.Outbound.Enqueue(Protocol.EncodeSpendAttr((byte)i)); // sunucu doğrular, yeni paket döner
                GUI.enabled = true;
            }

            float sy = y + 288;
            GUI.Label(new Rect(x + 16, sy, w - 30, 22), "— Türetilmiş İstatistikler —", uiTitle);
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
                GUI.Label(new Rect(x + 16, sy + 26 + i * 20, w - 30, 20), derived[i], uiLapis);

            // Faz C: silah ustalığı uzmanlık bölümü
            float my = sy + 26 + derived.Length * 20 + 10;
            GUI.Label(new Rect(x + 16, my, w - 30, 22), "— Silah Ustalığı —", uiTitle);
            if (!hasMasteryDetail) { GUI.Label(new Rect(x + 16, my + 26, w - 30, 20), "silah kuşan (ustalık yok)", uiLapis); return; }
            string gmTxt = masteryDetail.GM > 0 ? "  ★ GM " + RomanGM(masteryDetail.GM) : "";
            GUI.Label(new Rect(x + 16, my + 26, w - 30, 20),
                masteryDetail.Family + "  (Sv " + masteryDetail.Level + ")" + gmTxt, uiBody);
            if (masteryDetail.Legend > 0)
            {
                var lg = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
                lg.normal.textColor = new Color(1f, 0.82f, 0.2f);
                GUI.Label(new Rect(x + 200, my + 26, w - 210, 20), "Legend " + masteryDetail.Legend, lg);
            }
            if (masteryDetail.Level < masteryDetail.UnlockLevel)
            {
                GUI.Label(new Rect(x + 16, my + 48, w - 30, 20),
                    "Uzmanlık Sv " + masteryDetail.UnlockLevel + "'de açılır", uiLapis);
                return;
            }
            var specBtn = new GUIStyle(uiItemBtn) { wordWrap = true };
            specBtn.normal.textColor = colParch; specBtn.hover.textColor = colParch; // nadir-renk mirasını temizle
            for (int i = 0; i < masteryDetail.Options.Count; i++)
            {
                var o = masteryDetail.Options[i];
                bool active = o.Id == masteryDetail.ActiveSpec;
                float by = my + 50 + i * 34;
                if (GUI.Button(new Rect(x + 16, by, w - 32, 30),
                    (active ? "✔ " : "") + o.Name + "  (+" + o.DmgBonus + " hasar)", active ? uiBtnGold : specBtn))
                {
                    if (!active) net.Outbound.Enqueue(Protocol.EncodeSelectSpec(o.Id)); // sunucu doğrular
                }
            }

            // Faz F: Enchant (büyüleme) bölümü
            float ey = my + 50 + masteryDetail.Options.Count * 34 + 12;
            GUI.Label(new Rect(x + 16, ey, w - 30, 22), "— Büyüleme (güvenli bölge) —", uiTitle);
            if (hasEnchant)
            {
                GUI.Label(new Rect(x + 16, ey + 26, w - 30, 20), "Silah +" + enchantInfo.WLvl + "   Zırh +" + enchantInfo.ALvl, uiBody);
                if (GUI.Button(new Rect(x + 16, ey + 50, (w - 40) / 2, 30),
                    "Silah +" + (enchantInfo.WLvl + 1) + "\n" + enchantInfo.WCost + "g %" + enchantInfo.WPct, uiBtn))
                    net.Outbound.Enqueue(Protocol.EncodeEnchant(0));
                if (GUI.Button(new Rect(x + 26 + (w - 40) / 2, ey + 50, (w - 40) / 2, 30),
                    "Zırh +" + (enchantInfo.ALvl + 1) + "\n" + enchantInfo.ACost + "g %" + enchantInfo.APct, uiBtn))
                    net.Outbound.Enqueue(Protocol.EncodeEnchant(1));
            }

            // Faz K: hizip itibarları
            float ry2 = ey + 88;
            GUI.Label(new Rect(x + 16, ry2, w - 30, 22), "— İtibar —", uiTitle);
            for (int i = 0; i < repList.Count && i < 4; i++)
            {
                var rp = repList[i];
                GUI.Label(new Rect(x + 16, ry2 + 24 + i * 20, w - 30, 20),
                    rp.Faction + ":  " + rp.Level + "  (" + rp.Points + ")", uiLapis);
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
    }
}
