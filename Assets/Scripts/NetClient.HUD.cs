// NetClient.HUD.cs — Faz 2 (İstemci sağlığı) gün 5 dilimi.
// NetClient.cs'ten çıkarılan TÜM arayüz çizimi: OnGUI ve yardımcı Draw*/format
// metodları. Hâlâ IMGUI — gerçek UI'ye geçiş Faz 5'in işi, bu sadece dosya/
// organizasyon ayrımı (yapı). Davranış birebir korunuyor, hiçbir mantık
// değişmedi. Bkz. Docs/DECISION_LOG.md D-12 (partial class yaklaşımı).
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
    }
}
