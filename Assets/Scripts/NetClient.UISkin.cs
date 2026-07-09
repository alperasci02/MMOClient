// NetClient.UISkin.cs — Faz 5 (Sunum) ilk dilim: IMGUI'nin görsel kimliği + fare-odaklı
// menü erişimi. Tasarım Bible Cilt 15 ("aydınlatılmış el yazması + kilim geometrisi",
// oil-lamp iç mekân): koyu ceviz panel + altın-varak başlık şeridi + parşömen metin +
// lapis ikincil renk. Tüm dokular kodda üretilir (harici asset yok, editörsüz çalışır).
//
// Bu dilim BİLİNÇLİ olarak hâlâ IMGUI'dir (tam uGUI göçü Cilt 15'in geri kalanı, ayrı
// bir iş — editörde prefab/canvas gerektirir, bkz. Docs/DECISION_LOG.md D-18). Amaç:
// ham debug görünümünü ve "tuşla menü aç" mantığını, temalı + tıklanabilir bir arayüzle
// değiştirmek — ekranda tuş-komut yazıları KALMADI, menülere araç çubuğundan girilir.
using System.Collections.Generic;
using UnityEngine;

namespace MMO
{
    public partial class NetClient
    {
        // --- palet (Cilt 15) ---
        static readonly Color colPanel  = new Color(0.11f, 0.09f, 0.07f, 0.96f); // koyu ceviz/mürekkep zemin
        static readonly Color colBorder = new Color(0.66f, 0.50f, 0.22f, 1f);    // altın-varak çerçeve
        static readonly Color colHeader = new Color(0.60f, 0.46f, 0.20f, 1f);    // altın başlık şeridi
        static readonly Color colInk    = new Color(0.12f, 0.09f, 0.05f, 1f);    // koyu mürekkep (altın üstü yazı)
        static readonly Color colParch  = new Color(0.91f, 0.86f, 0.74f, 1f);    // parşömen metin
        static readonly Color colGoldTx = new Color(0.90f, 0.74f, 0.36f, 1f);    // altın değer metni
        static readonly Color colLapis  = new Color(0.56f, 0.73f, 0.93f, 1f);    // lapis ikincil
        static readonly Color colBtn     = new Color(0.19f, 0.15f, 0.11f, 1f);   // buton tabanı
        static readonly Color colBtnHov  = new Color(0.30f, 0.24f, 0.15f, 1f);   // buton hover
        static readonly Color colBar     = new Color(0.06f, 0.05f, 0.04f, 0.97f);// araç çubuğu zemini
        static readonly Color colActive  = new Color(0.62f, 0.47f, 0.20f, 1f);   // aktif/altın buton
        static readonly Color colAbil    = new Color(0.16f, 0.14f, 0.22f, 0.94f);// yetenek yuvası (hazır)
        static readonly Color colAbilCd  = new Color(0.28f, 0.11f, 0.10f, 0.94f);// yetenek yuvası (bekleme)

        // --- üretilen dokular (bir kez, paylaşımlı) ---
        static Texture2D _txPanel, _txBorder, _txHeader, _txBtn, _txBtnHov, _txBar, _txActive, _txAbil, _txAbilCd;

        // --- stiller ---
        GUIStyle uiHeader, uiTitle, uiBody, uiSmall, uiLapis, uiGold, uiClose, uiBtn, uiBtnGold, uiItemBtn,
                 uiField, uiTool, uiToolActive, uiAbilTxt, uiAbilCdTxt, uiInvisible;
        bool _uiReady;

        // fare-üstü-UI tespiti için açık panel/araç-çubuğu dikdörtgenleri (dünya tıklamasını engeller)
        readonly List<Rect> _uiRects = new List<Rect>();

        static Texture2D SolidTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Repeat;
            return t;
        }

        void EnsureUI()
        {
            if (_uiReady) return;
            if (_txPanel == null)
            {
                _txPanel = SolidTex(colPanel); _txBorder = SolidTex(colBorder); _txHeader = SolidTex(colHeader);
                _txBtn = SolidTex(colBtn); _txBtnHov = SolidTex(colBtnHov); _txBar = SolidTex(colBar);
                _txActive = SolidTex(colActive); _txAbil = SolidTex(colAbil); _txAbilCd = SolidTex(colAbilCd);
            }

            GUIStyle Label(int size, Color c, FontStyle fs = FontStyle.Normal, TextAnchor a = TextAnchor.UpperLeft)
            {
                var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = a, richText = false };
                s.normal.textColor = c; return s;
            }
            GUIStyle Button(Texture2D bg, Texture2D hov, Color txt, int size, FontStyle fs = FontStyle.Normal, TextAnchor a = TextAnchor.MiddleCenter)
            {
                var s = new GUIStyle(GUI.skin.button) { fontSize = size, fontStyle = fs, alignment = a };
                s.normal.background = bg; s.hover.background = hov; s.active.background = _txActive;
                s.normal.textColor = txt; s.hover.textColor = txt; s.active.textColor = colInk;
                s.border = new RectOffset(2, 2, 2, 2); s.padding = new RectOffset(6, 6, 3, 3);
                return s;
            }

            uiHeader     = Label(15, colInk, FontStyle.Bold, TextAnchor.MiddleLeft);
            uiTitle      = Label(14, colGoldTx, FontStyle.Bold);
            uiBody       = Label(13, colParch);
            uiSmall      = Label(11, new Color(0.72f, 0.68f, 0.58f));
            uiLapis      = Label(13, colLapis);
            uiGold       = Label(13, colGoldTx, FontStyle.Bold);
            uiClose      = Button(_txHeader, _txBtnHov, colInk, 15, FontStyle.Bold);
            uiBtn        = Button(_txBtn, _txBtnHov, colParch, 12);
            uiBtnGold    = Button(_txActive, _txActive, colInk, 12, FontStyle.Bold);
            uiItemBtn    = Button(_txBtn, _txBtnHov, colParch, 12, FontStyle.Normal, TextAnchor.MiddleLeft);
            uiTool       = Button(_txBtn, _txBtnHov, colParch, 12);
            uiToolActive = Button(_txActive, _txActive, colInk, 12, FontStyle.Bold);
            uiAbilTxt    = Label(12, colParch, FontStyle.Bold, TextAnchor.MiddleCenter);
            uiAbilCdTxt  = Label(12, new Color(1f, 0.6f, 0.55f), FontStyle.Bold, TextAnchor.MiddleCenter);
            uiInvisible  = new GUIStyle();

            uiField = new GUIStyle(GUI.skin.textField) { fontSize = 13 };
            uiField.normal.textColor = colParch; uiField.focused.textColor = colParch;

            _uiReady = true;
        }

        // UIPanel: çerçeveli panel + altın başlık şeridi + başlık + (×) kapat butonu.
        // Dönüş: kapat butonuna basıldıysa true. İçerik r.y+38'den başlamalı.
        bool UIPanel(Rect r, string title)
        {
            EnsureUI();
            GUI.DrawTexture(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), _txBorder);
            GUI.DrawTexture(r, _txPanel);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 30), _txHeader);
            GUI.Label(new Rect(r.x + 12, r.y + 4, r.width - 46, 22), title, uiHeader);
            return GUI.Button(new Rect(r.x + r.width - 28, r.y + 4, 22, 22), "×", uiClose);
        }

        void RegisterUI(Rect r) { _uiRects.Add(r); }

        // PointerOverUI: verilen ekran-konumu (Input System, sol-alt orijin) açık bir UI
        // dikdörtgeninin üstünde mi? Dünya tıklamasını (yürü/saldır) UI üstündeyken engeller.
        bool PointerOverUI(Vector2 mouseScreenPos)
        {
            float gx = mouseScreenPos.x;
            float gy = Screen.height - mouseScreenPos.y; // GUI: sol-üst orijin
            for (int i = 0; i < _uiRects.Count; i++)
                if (_uiRects[i].Contains(new Vector2(gx, gy))) return true;
            return false;
        }

        // Alt araç çubuğu — TÜM menüler buradan fareyle açılır (tuş gerekmez).
        static readonly string[] toolLabels =
            { "Çanta", "Karakter", "Üretim", "Pazar", "Lonca", "Meslek", "Görev", "Sıralama", "Parti", "Sohbet", "Tamir", "Menü" };

        void DrawToolbar()
        {
            EnsureUI();
            int n = toolLabels.Length;
            float bw = Mathf.Min(106f, (Screen.width - 28f) / n);
            float bh = 30f, gap = 3f;
            float total = n * bw + (n - 1) * gap;
            float sx = Screen.width / 2f - total / 2f;
            float sy = Screen.height - bh - 6f;

            var bar = new Rect(sx - 8, sy - 6, total + 16, bh + 12);
            GUI.DrawTexture(bar, _txBar);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width, 2), _txBorder); // üst altın hat
            RegisterUI(bar);

            for (int i = 0; i < n; i++)
            {
                float x = sx + i * (bw + gap);
                bool active = PanelActive(i);
                if (GUI.Button(new Rect(x, sy, bw, bh), toolLabels[i], active ? uiToolActive : uiTool))
                    ToolbarAction(i);
            }
        }

        bool PanelActive(int i)
        {
            switch (i)
            {
                case 0: return showInventory;
                case 1: return showCharSheet;
                case 2: return showCrafting;
                case 3: return showMarket;
                case 4: return showGuild;
                case 5: return showProf;
                case 6: return showQuests;
                case 7: return showLeader;
                case 9: return chatOpen;
                case 11: return paused;
                default: return false;
            }
        }

        void ToolbarAction(int i)
        {
            PlaySfx(uiClickClip);
            switch (i)
            {
                case 0: showInventory = !showInventory; break;
                case 1: showCharSheet = !showCharSheet; break;
                case 2: showCrafting = !showCrafting; break;
                case 3: showMarket = !showMarket; if (showMarket) net.Outbound.Enqueue(Protocol.EncodeMarketBrowse()); break;
                case 4: showGuild = !showGuild; if (showGuild) net.Outbound.Enqueue(Protocol.EncodeGuildInfoReq()); break;
                case 5: showProf = !showProf; break;
                case 6: showQuests = !showQuests; if (showQuests) net.Outbound.Enqueue(Protocol.EncodeQuestList()); break;
                case 7: showLeader = !showLeader; if (showLeader) net.Outbound.Enqueue(Protocol.EncodeLeaderReq()); break;
                case 8: InviteNearest(); break;
                case 9: chatOpen = !chatOpen; if (chatOpen) chatInput = ""; break;
                case 10: DoRepair(); break;
                case 11: paused = !paused; break;
            }
        }

        // DoRepair: eski R-tuşu mantığı — artık "Tamir" araç-çubuğu butonundan çağrılır.
        void DoRepair()
        {
            if (zoneId == "meadow") { net.Outbound.Enqueue(Protocol.EncodeRepair()); repairMsg = "Tamir ediliyor..."; }
            else repairMsg = "Tamir sadece güvenli bölgede (Başlangıç Çayırı)";
            repairMsgTimer = 2.5f;
        }
    }
}
