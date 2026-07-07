// MMO/Animasyon Kur — karakter + animasyonları TEK TIKLA kurar.
// Beklenen dosyalar: Assets/Models/Character/ içinde
//   X Bot.fbx (veya tek karakter fbx'i) + standing idle.fbx + standing walk forward.fbx
//   + standing melee attack horizontal.fbx
// Yaptıkları:
//   1) Karakteri Humanoid yapar (avatar) + Resources/Entities/player.fbx olarak kopyalar
//   2) Klipleri Humanoid + avatar-kopya + loop yapar
//   3) AnimatorController üretir -> Resources/Entities/player_controller (runtime otomatik bağlar)
//   4) URP pembe-materyal önlemi (FBX materyal importu kapatılır)
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class MMOAnimSetup
{
    const string Dir = "Assets/Models/Character/";
    const string IdleName = "standing idle.fbx";
    const string WalkName = "standing walk forward.fbx";
    const string AttackName = "standing melee attack horizontal.fbx";
    // Yetenek animasyonları (Faz-görsel): Ağır Darbe=combo, Girdap=360 dönüş, İyileşme=savaş narası
    const string ComboName = "standing melee combo attack ver. 1.fbx";
    const string SpinName = "standing melee attack 360 high.fbx";
    const string BuffName = "standing taunt battlecry.fbx";
    const string PlayerFbx = "Assets/Resources/Entities/player.fbx";
    const string CtrlPath = "Assets/Resources/Entities/player_controller.controller";

    [MenuItem("MMO/Animasyon Kur (Karakter)")]
    public static void Setup()
    {
        if (!Directory.Exists(Dir)) { Debug.LogError("[MMO] Klasör yok: " + Dir); return; }

        // Karakter fbx'ini bul: klip isimleri dışındaki EN BÜYÜK .fbx (karakter mesh içerir,
        // animasyon kliplerinden büyüktür). ALT KLASÖRLER DAHİL taranır.
        string modelPath = null;
        long best = -1;
        foreach (var f in Directory.GetFiles(Dir, "*.fbx", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(f).ToLowerInvariant();
            if (name == IdleName || name == WalkName || name == AttackName) continue;
            long size = new FileInfo(f).Length;
            if (size > best) { best = size; modelPath = f.Replace('\\', '/'); }
        }
        if (modelPath == null) { Debug.LogError("[MMO] Karakter fbx bulunamadı (klip olmayan bir .fbx bekleniyor): " + Dir); return; }
        Debug.Log("[MMO] Karakter: " + modelPath);

        // 1) Humanoid + senkron import (önceki hata: import bitmeden avatar arıyorduk)
        var avatar = MakeHumanoid(modelPath, null);
        if (avatar == null) { Debug.LogError("[MMO] Avatar oluşmadı: " + modelPath + " — Console'daki üstteki [MMO] loglarını yolla"); return; }

        // Karakteri Resources/Entities/player.fbx olarak kopyala (runtime bunu yükler)
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerFbx) != null) AssetDatabase.DeleteAsset(PlayerFbx);
        if (!AssetDatabase.CopyAsset(modelPath, PlayerFbx)) { Debug.LogError("[MMO] player.fbx kopyalanamadı"); return; }
        AssetDatabase.ImportAsset(PlayerFbx, ImportAssetOptions.ForceSynchronousImport);
        MakeHumanoid(PlayerFbx, null);

        // 2) klipler (alt klasörlerde isimle bul)
        var idle = SetupClip(FindFile(IdleName), avatar, true);
        var walk = SetupClip(FindFile(WalkName), avatar, true);
        var atk = SetupClip(FindFile(AttackName), avatar, false);
        if (idle == null || walk == null || atk == null)
        {
            Debug.LogError("[MMO] Klip eksik (idle=" + (idle != null) + " walk=" + (walk != null) + " attack=" + (atk != null) + ") — dosya adları birebir şöyle olmalı: '" + IdleName + "', '" + WalkName + "', '" + AttackName + "'");
            return;
        }

        // 3) controller
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(CtrlPath) != null) AssetDatabase.DeleteAsset(CtrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        var sm = ctrl.layers[0].stateMachine;
        var sIdle = sm.AddState("Idle"); sIdle.motion = idle;
        var sWalk = sm.AddState("Walk"); sWalk.motion = walk;
        var sAtk = sm.AddState("Attack"); sAtk.motion = atk;
        sm.defaultState = sIdle;
        var t1 = sIdle.AddTransition(sWalk); t1.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed"); t1.hasExitTime = false; t1.duration = 0.15f;
        var t2 = sWalk.AddTransition(sIdle); t2.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed"); t2.hasExitTime = false; t2.duration = 0.15f;
        var ta = sm.AddAnyStateTransition(sAtk); ta.AddCondition(AnimatorConditionMode.If, 0, "Attack"); ta.hasExitTime = false; ta.duration = 0.08f; ta.canTransitionToSelf = false;
        var tb = sAtk.AddTransition(sIdle); tb.hasExitTime = true; tb.exitTime = 0.85f; tb.duration = 0.15f;

        // Yetenek animasyonları (varsa): Attack2=combo, Attack3=360 dönüş, Buff=savaş narası
        AddAbilityState(ctrl, sm, sIdle, avatar, ComboName, "Attack2");
        AddAbilityState(ctrl, sm, sIdle, avatar, SpinName, "Attack3");
        AddAbilityState(ctrl, sm, sIdle, avatar, BuffName, "Buff");

        // (Düşman modeli artık MMOWorldSetup'ta: gerçek canavar paketi Skeleton/Dragon kullanılıyor)
        const string EnemyFbx = "Assets/Resources/Entities/enemy.fbx";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(EnemyFbx) != null) AssetDatabase.DeleteAsset(EnemyFbx);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MMO] Animasyon kurulumu TAMAM ✓ (player + enemy) — Play'e bas.");
    }

    // Bir yetenek klibini controller'a state + AnyState->trigger geçişiyle ekler (klip yoksa atlar).
    static void AddAbilityState(AnimatorController ctrl, UnityEditor.Animations.AnimatorStateMachine sm,
        UnityEditor.Animations.AnimatorState idleState, Avatar avatar, string clipFile, string trigger)
    {
        var clip = SetupClip(FindFile(clipFile), avatar, false);
        if (clip == null) { Debug.LogWarning("[MMO] yetenek klibi yok, atlandı: " + clipFile); return; }
        ctrl.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
        var st = sm.AddState(trigger); st.motion = clip;
        var ti = sm.AddAnyStateTransition(st);
        ti.AddCondition(AnimatorConditionMode.If, 0, trigger);
        ti.hasExitTime = false; ti.duration = 0.08f; ti.canTransitionToSelf = false;
        var to = st.AddTransition(idleState); to.hasExitTime = true; to.exitTime = 0.85f; to.duration = 0.15f;
    }

    // Dir altında (alt klasörler dahil) tam dosya adıyla ara; Unity asset yolu döner.
    static string FindFile(string fileName)
    {
        foreach (var f in Directory.GetFiles(Dir, "*.fbx", SearchOption.AllDirectories))
            if (Path.GetFileName(f).ToLowerInvariant() == fileName) return f.Replace('\\', '/');
        return Dir + fileName; // bulunamazsa eski yol (SetupClip 'klip yok' uyarısı verir)
    }

    static Avatar MakeHumanoid(string path, Avatar source)
    {
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi == null) { Debug.LogWarning("[MMO] importer yok: " + path); return null; }
        mi.animationType = ModelImporterAnimationType.Human;
        if (source != null) { mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther; mi.sourceAvatar = source; }
        else mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        EditorUtility.SetDirty(mi);
        mi.SaveAndReimport();
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); // import bitmeden devam etme
        Avatar found = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (o is Avatar a) { found = a; break; }
        }
        if (found == null)
        {
            // teşhis: alt-asset listesini dök
            var subs = AssetDatabase.LoadAllAssetsAtPath(path);
            Debug.LogWarning("[MMO] " + path + " avatar içermiyor. Alt-asset sayısı=" + subs.Length);
            foreach (var o in subs) Debug.LogWarning("   alt-asset: " + o.GetType().Name + " '" + o.name + "'");
        }
        else if (!found.isValid)
        {
            Debug.LogWarning("[MMO] Avatar bulundu ama GEÇERSİZ (kemik eşleme hatası olabilir): " + path);
        }
        return found;
    }

    static AnimationClip SetupClip(string path, Avatar avatar, bool loop)
    {
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi == null) { Debug.LogWarning("[MMO] klip yok: " + path); return null; }
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        mi.sourceAvatar = avatar;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        var clips = mi.defaultClipAnimations;
        foreach (var c in clips) { c.loopTime = loop; c.lockRootRotation = true; c.lockRootPositionXZ = true; }
        mi.clipAnimations = clips;
        mi.SaveAndReimport();
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
        return null;
    }
}
#endif
