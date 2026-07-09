// NetClient.Settings.cs — Faz 5 (Sunum): oyun-içi Ayarlar ekranı + kalıcılık.
// Duraklat menüsündeki "Ayarlar" butonundan açılır. Kamera sarsıntı gücü ve ana ses
// oyun içinden ayarlanır; PlayerPrefs ile kalıcı (sunucu adresi gibi). Ham asset yok.
// Not: gerçek "UI ölçeği" IMGUI'de tıklama koordinatlarıyla çakıştığı için şimdilik
// dışarıda — uGUI göçünde (A-07) native olarak gelir.
using UnityEngine;

namespace MMO
{
    public partial class NetClient
    {
        bool showSettings = false;
        bool showCredits = false;
        bool _nameFocused = false; // isim ekranında bir kez odak (her kare zorlama = sunucu alanı düzenlenemez hatası)
        float masterVolume = 0.8f; // AudioListener.volume'a uygulanır (0 = sessiz)

        const string PrefShake = "mmo_shake";
        const string PrefVolume = "mmo_volume";

        void LoadSettings()
        {
            shakeStrength = Mathf.Clamp(PlayerPrefs.GetFloat(PrefShake, 1f), 0f, 1.5f); // shakeStrength: NetClient.Juice.cs
            masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefVolume, 0.8f));
            AudioListener.volume = masterVolume;
        }

        void SaveSettings()
        {
            PlayerPrefs.SetFloat(PrefShake, shakeStrength);
            PlayerPrefs.SetFloat(PrefVolume, masterVolume);
            PlayerPrefs.Save();
        }

        void DrawSettings()
        {
            float w = 400, h = 250, x = Screen.width / 2f - w / 2f, y = Screen.height / 2f - h / 2f;
            var r = new Rect(x, y, w, h); RegisterUI(r);
            if (UIPanel(r, "Ayarlar")) { showSettings = false; SaveSettings(); }

            float cy = y + 48;

            // Kamera sarsıntısı (0 = kapalı)
            GUI.Label(new Rect(x + 22, cy, w - 44, 22),
                "Kamera sarsıntısı:  " + Mathf.RoundToInt(shakeStrength / 1.5f * 100f) + "%", uiBody);
            float ns = GUI.HorizontalSlider(new Rect(x + 22, cy + 26, w - 44, 18), shakeStrength, 0f, 1.5f);
            if (!Mathf.Approximately(ns, shakeStrength)) shakeStrength = ns;
            cy += 58;

            // Ana ses
            GUI.Label(new Rect(x + 22, cy, w - 44, 22),
                "Ses:  " + Mathf.RoundToInt(masterVolume * 100f) + "%", uiBody);
            float nv = GUI.HorizontalSlider(new Rect(x + 22, cy + 26, w - 44, 18), masterVolume, 0f, 1f);
            if (!Mathf.Approximately(nv, masterVolume)) { masterVolume = nv; AudioListener.volume = masterVolume; }

            // butonlar
            float bw = (w - 56) / 2f;
            if (GUI.Button(new Rect(x + 22, y + h - 44, bw, 30), "Varsayılana Dön", uiBtn))
            {
                shakeStrength = 1f; masterVolume = 0.8f; AudioListener.volume = masterVolume;
            }
            if (GUI.Button(new Rect(x + 34 + bw, y + h - 44, bw, 30), "Kapat", uiBtnGold))
            {
                PlaySfx(uiClickClip); showSettings = false; SaveSettings();
            }
        }

        // Faz 5: krediler (ana menüden). Ses paketi CC0 atıfları + oyun kimliği.
        void DrawCredits()
        {
            float w = 440, h = 250, x = Screen.width / 2f - w / 2f, y = Screen.height / 2f - h / 2f;
            var r = new Rect(x, y, w, h); RegisterUI(r);
            if (UIPanel(r, "Krediler")) showCredits = false;

            GUI.Label(new Rect(x + 20, y + 44, w - 40, 22), "ANATOLIA — Katmanlı Dünya", uiGold);
            GUI.Label(new Rect(x + 20, y + 70, w - 40, 40),
                "Solo + LAN co-op Aksiyon RPG.\nTasarım incili: Docs/DESIGN_BIBLE.", uiBody);
            GUI.Label(new Rect(x + 20, y + 120, w - 40, 22), "— Ses —", uiTitle);
            GUI.Label(new Rect(x + 20, y + 146, w - 40, 44),
                "Kenney.nl (CC0): RPG Audio · Impact Sounds ·\nMusic Jingles · UI Audio.  Teşekkürler!", uiSmall);

            if (GUI.Button(new Rect(x + w / 2f - 60, y + h - 44, 120, 30), "Kapat", uiBtnGold))
            {
                PlaySfx(uiClickClip); showCredits = false;
            }
        }
    }
}
