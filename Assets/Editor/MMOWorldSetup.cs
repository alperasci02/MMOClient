// MMO/DÜNYAYI KUR — yüklenen TÜM asset paketlerini oyuna bağlar (tek tık):
//   • Düşman: Skeleton -> enemy.prefab, Dragon -> boss.prefab (gömülü animasyonlarından
//     Idle/Walk controller üretilir; Speed parametresi runtime'da sürülüyor)
//   • Doğa: nature_meadow / nature_forest.prefab (deterministik ağaç/çalı/kaya halkası)
//   • Pazar: market_meadow.prefab (kemer + tezgah/fıçı/sandık/meşale meydanı)
//   • Zindan: dungeon_env_0/1/2 (oda + varyanta özel dekor: sütun/halı-sandık/kemik-şamdan)
//   • Portal -> Arch, Mezar -> Chest_Gold, Kaynak düğümü -> Rock
// Tüm parçalar ölçülür ve hedef boyuta OTOMATİK ölçeklenir. Eski enemy.fbx kaldırılır.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class MMOWorldSetup
{
    const string M = "Assets/Models/";
    const string Out = "Assets/Resources/Entities/";

    [MenuItem("MMO/DÜNYAYI KUR (tüm assetler)")]
    public static void SetupAll()
    {
        SetupMonster(M + "Monsters", "Skeleton", "enemy", 2.0f, "Scythe");
        SetupMonster(M + "Monsters", "Dragon", "boss", 4.5f);
        // Görsel çeşitlilik: kurt (Animals), balçık + yarasa (Monsters) — sunucu subKind ile eşler
        SetupMonster(M + "Animals", "Wolf", "mob_wolf", 1.6f);
        SetupMonster(M + "Monsters", "Slime", "mob_slime", 1.4f);
        SetupMonster(M + "Monsters", "Bat", "mob_bat", 1.2f);
        SetupSimpleProp(M + "Town", "Arch", "portal", 3.5f);
        SetupSimpleProp(M + "Town", "Chest_Gold", "corpse", 1.1f);
        SetupSimpleProp(M + "Nature", "Rock_1", "node", 1.6f); // genel yedek
        // Kaynak türüne özel görseller (sunucu subKind: 0=ore 1=wood 2=herb 3=fish 4=mithril)
        SetupSimpleProp(M + "Nature", "Rock_1", "node_ore", 1.6f);
        SetupSimpleProp(M + "Nature", "BirchTree_1", "node_wood", 2.8f);
        SetupSimpleProp(M + "Nature", "Plant_Flowers", "node_herb", 1.1f);
        SetupSimpleProp(M + "Fish", "Sunfish", "node_fish", 0.9f);
        SetupSimpleProp(M + "Items", "Crystal1", "node_mithril", 1.5f);
        SetupNature();
        SetupCoast();
        SetupMarket();
        SetupDungeons();
        SetupWeapons();
        SetupArmor();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MMO] DÜNYA KURULUMU TAMAM ✓ — Play'e bas!");
    }

    // ---------- Düşman/boss: model + gömülü kliplerden controller ----------
    static void SetupMonster(string dir, string fbxName, string outName, float height, string handWeapon = null)
    {
        var path = FindFbx(dir, fbxName);
        if (path == null) { Debug.LogWarning("[MMO] bulunamadı: " + fbxName); return; }

        // klipleri looplat (attack hariç) + mümkünse Humanoid yap (el kemiğine silah takabilmek için).
        // Eşleşme tutmazsa (avatar geçersiz) Generic'e geri dönülür -> mevcut animasyonlar bozulmaz.
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        bool triedHumanoid = false;
        if (mi != null)
        {
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            // Yalnız silah takılacaksa (handWeapon != null) Humanoid dene — el kemiği gerekir.
            // Wolf/Slime/Bat gibi hayvan riglerinde boşuna deneyip uyarı basmayalım.
            if (handWeapon != null)
            {
                var origType = mi.animationType;
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.SaveAndReimport();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                Avatar testAvatar = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Avatar a) { testAvatar = a; break; }
                if (testAvatar != null && testAvatar.isValid) { triedHumanoid = true; }
                else
                {
                    Debug.LogWarning("[MMO] " + fbxName + " humanoid eşleşmedi -> Generic'e dönüldü (silah takılamaz ama animasyon bozulmaz)");
                    mi.animationType = origType;
                }
            }
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                var n = c.name.ToLowerInvariant();
                c.loopTime = !(n.Contains("attack") || n.Contains("bite") || n.Contains("death") || n.Contains("hit"));
            }
            if (clips.Length > 0) mi.clipAnimations = clips;
            mi.SaveAndReimport();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (src == null) { Debug.LogWarning("[MMO] model yüklenemedi: " + path); return; }

        // gömülü kliplerden Idle/Walk (+Attack) controller
        AnimationClip idle = null, walk = null, attack = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (!(o is AnimationClip c) || c.name.StartsWith("__preview")) continue;
            var n = c.name.ToLowerInvariant();
            if (idle == null && n.Contains("idle")) idle = c;
            else if (walk == null && (n.Contains("walk") || n.Contains("run") || n.Contains("fly") || n.Contains("move"))) walk = c;
            else if (attack == null && (n.Contains("attack") || n.Contains("bite") || n.Contains("slash"))) attack = c;
        }

        RuntimeAnimatorController rac = null;
        if (idle != null)
        {
            string cp = Out + outName + "_controller.controller";
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(cp) != null) AssetDatabase.DeleteAsset(cp);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(cp);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            var sm = ctrl.layers[0].stateMachine;
            var sI = sm.AddState("Idle"); sI.motion = idle; sm.defaultState = sI;
            var sW = sm.AddState("Walk"); sW.motion = walk != null ? walk : idle;
            var t1 = sI.AddTransition(sW); t1.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed"); t1.hasExitTime = false; t1.duration = 0.15f;
            var t2 = sW.AddTransition(sI); t2.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed"); t2.hasExitTime = false; t2.duration = 0.15f;
            if (attack != null)
            {
                var sA = sm.AddState("Attack"); sA.motion = attack;
                var ta = sm.AddAnyStateTransition(sA); ta.AddCondition(AnimatorConditionMode.If, 0, "Attack"); ta.hasExitTime = false; ta.duration = 0.08f; ta.canTransitionToSelf = false;
                var tb = sA.AddTransition(sI); tb.hasExitTime = true; tb.exitTime = 0.85f; tb.duration = 0.15f;
            }
            rac = ctrl;
        }
        else Debug.LogWarning("[MMO] " + fbxName + " gömülü idle klibi yok — statik kalacak");

        // prefab: root altında ölçekli model + controller
        var root = new GameObject(outName);
        try
        {
            var inst = (GameObject)Object.Instantiate(src, Vector3.zero, src.transform.rotation, root.transform);
            ScaleToHeight(inst, height);
            var an = inst.GetComponentInChildren<Animator>();
            if (an == null) an = inst.AddComponent<Animator>();
            if (rac != null) an.runtimeAnimatorController = rac;
            if (handWeapon != null && triedHumanoid && an.avatar != null && an.avatar.isValid && an.avatar.isHuman)
                AttachToHand(an, M + "Weapons", handWeapon, 0.9f);
            SavePrefab(root, outName);
        }
        finally { Object.DestroyImmediate(root); }

        // aynı isimli eski fbx kopyası varsa kaldır (Resources.Load çakışmasın)
        string oldFbx = Out + outName + ".fbx";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(oldFbx) != null) AssetDatabase.DeleteAsset(oldFbx);
        Debug.Log("[MMO] üretildi: " + outName + ".prefab (" + fbxName + (rac != null ? ", animasyonlu" : ", statik") + ")");
    }

    // ---------- Basit prop prefab'ı (portal/mezar/kaynak) ----------
    static void SetupSimpleProp(string dir, string fbxName, string outName, float height)
    {
        var path = FindFbx(dir, fbxName);
        if (path == null) { Debug.LogWarning("[MMO] bulunamadı: " + fbxName); return; }
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (src == null) return;
        var root = new GameObject(outName);
        try
        {
            var inst = (GameObject)Object.Instantiate(src, Vector3.zero, src.transform.rotation, root.transform);
            ScaleToHeight(inst, height);
            SavePrefab(root, outName);
        }
        finally { Object.DestroyImmediate(root); }
        string oldFbx = Out + outName + ".fbx";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(oldFbx) != null) AssetDatabase.DeleteAsset(oldFbx);
        Debug.Log("[MMO] üretildi: " + outName + ".prefab (" + fbxName + ")");
    }

    // ---------- Doğa halkaları (+bölgeye özel ekstralar) ----------
    static void SetupNature()
    {
        // Çayır: ağaç halkası + otlayan hayvanlar (Animals paketi)
        BuildScatter("nature_meadow", 12345, new[] { "BirchTree_", "MapleTree_" }, new[] { "Bush", "Flower_" }, 16, 26, 19f, 44f, 5.5f, 1.2f, root =>
        {
            PlaceAnimal(root, "Deer", new Vector3(13f, 0, 10f), 140f, 1.7f);
            PlaceAnimal(root, "Stag", new Vector3(15f, 0, 13f), 200f, 2.0f);
            PlaceAnimal(root, "Fox", new Vector3(-13f, 0, -10f), 60f, 0.9f);
            PlaceAnimal(root, "Alpaca", new Vector3(-14f, 0, 11f), 320f, 1.6f);
            // Çiftlik köşesi (batı): çitle çevrili ahır hayvanları
            for (int i = 0; i < 4; i++)
            {
                Place(root, "Town", "Fence_Straight_Modular", new Vector3(-22f + i * 2.6f, 0, 5f), 0f, 1.1f);
                Place(root, "Town", "Fence_Straight_Modular", new Vector3(-22f + i * 2.6f, 0, 14f), 0f, 1.1f);
            }
            Place(root, "Town", "Fence_Straight_Modular", new Vector3(-23.3f, 0, 7.3f), 90f, 1.1f);
            Place(root, "Town", "Fence_Straight_Modular", new Vector3(-23.3f, 0, 11.7f), 90f, 1.1f);
            PlaceAnimal(root, "Cow", new Vector3(-20f, 0, 8f), 40f, 1.7f);
            PlaceAnimal(root, "Bull", new Vector3(-17f, 0, 11f), 210f, 1.8f);
            PlaceAnimal(root, "Horse", new Vector3(-14f, 0, 8f), 130f, 2.1f);
            PlaceAnimal(root, "Donkey", new Vector3(-19f, 0, 12.5f), 300f, 1.6f);
        });
        // Orman: kurumuş ağaçlar + balık düğümünün (-3,-12) yanına iskele + tekne (Fish paketi)
        BuildScatter("nature_forest", 54321, new[] { "DeadTree_", "BirchTree_" }, new[] { "Bush_Small", "Rock_" }, 34, 20, 17f, 46f, 6.5f, 1.4f, root =>
        {
            Place(root, "Fish", "Dock_Long", new Vector3(-3f, 0, -14.5f), 0f, 1.3f);
            Place(root, "Fish", "Boat", new Vector3(-7f, 0, -16f), 25f, 1.6f);
            // Terk edilmiş kalıntılar: yıkık sütunlar + kafatası + dikenli tuzak (tehlike hissi)
            Place(root, "Town", "Column", new Vector3(8f, 0, 9f), 15f, 3.0f);
            Place(root, "Town", "Column2", new Vector3(10.5f, 0, 7f), 340f, 2.2f);
            Place(root, "Town", "Skull", new Vector3(9f, 0, 5.5f), 70f, 0.5f);
            Place(root, "Town", "Trap_spikes", new Vector3(-8f, 0, 8f), 0f, 0.5f);
            Place(root, "DungeonDeco", "Bones", new Vector3(-10f, 0, 6f), 200f, 0.6f);
        });
    }

    // ---------- Sahil Köyü (coast bölgesi): iskele + tekneler + balık pazarı + NPC'ler ----------
    static void SetupCoast()
    {
        // Seyrek akçaağaç + bol kaya/çalı halkası; köy içeriği extra callback'te
        BuildScatter("nature_coast", 77777, new[] { "MapleTree_" }, new[] { "Rock_", "Bush" }, 7, 24, 21f, 45f, 5.0f, 1.3f, root =>
        {
            // Kuzey = deniz tarafı: uzun iskele + yan iskeleler + tekneler + olta
            Place(root, "Fish", "Dock_Long", new Vector3(0f, 0, 15f), 0f, 1.4f);
            Place(root, "Fish", "Dock_Wide", new Vector3(4f, 0, 13f), 0f, 1.2f);
            Place(root, "Fish", "Dock_Stairs", new Vector3(-2.5f, 0, 12.5f), 180f, 1.0f);
            Place(root, "Fish", "Boat", new Vector3(7f, 0, 17f), 205f, 1.7f);
            Place(root, "Fish", "Boat", new Vector3(-5.5f, 0, 16.5f), 155f, 1.7f);
            Place(root, "Fish", "FishingRod_Lvl1", new Vector3(1.3f, 0, 14.6f), 20f, 1.2f);
            Place(root, "Town", "Torch", new Vector3(-1.5f, 0, 11.5f), 0f, 1.7f);
            Place(root, "Town", "Torch", new Vector3(1.5f, 0, 11.5f), 0f, 1.7f);
            // Balık pazarı köşesi (güneybatı): tezgahın ÜSTÜNDE sergilenen balıklar
            Place(root, "Town", "Table_Small", new Vector3(-8f, 0, -6f), 0f, 0.9f);
            Place(root, "Fish", "BlueTang", new Vector3(-8.2f, 0.92f, -6f), 40f, 0.35f);
            Place(root, "Fish", "YellowTang", new Vector3(-7.7f, 0.92f, -5.8f), 300f, 0.35f);
            Place(root, "Town", "Barrel", new Vector3(-9.6f, 0, -5f), 0f, 1.1f);
            Place(root, "Town", "Crate", new Vector3(-6.4f, 0, -6.9f), 25f, 0.9f);
            // Köy sakinleri (Heroes): balıkçı/keşiş/kaçakçı
            PlaceNPC(root, "Ranger", new Vector3(-7f, 0, -4.4f), 150f, 1.9f);
            PlaceNPC(root, "Monk", new Vector3(2.5f, 0, -8f), 340f, 1.9f);
            PlaceNPC(root, "Rogue", new Vector3(9f, 0, -3f), 250f, 1.9f);
            // Kamp ateşi
            Place(root, "Town", "Woodfire", new Vector3(-3f, 0, -7.5f), 0f, 0.8f);
        });
    }

    static void BuildScatter(string outName, int seed, string[] bigPrefixes, string[] smallPrefixes, int bigCount, int smallCount, float rMin, float rMax, float bigH, float smallH, System.Action<GameObject> extra = null)
    {
        var bigs = CollectByPrefix(M + "Nature", bigPrefixes);
        var smalls = CollectByPrefix(M + "Nature", smallPrefixes);
        if (bigs.Count == 0) { Debug.LogWarning("[MMO] doğa parçaları yok (" + outName + ")"); return; }
        var rnd = new System.Random(seed);
        var root = new GameObject(outName);
        try
        {
            // Çim halısı: MERKEZ DAHİL tüm alanı kaplar (halka değil dolu disk) — oyuncunun
            // doğduğu yer çıplak kalmasın diye (önceki sorun: sadece 19+ yarıçapta halka vardı).
            ScatterGrassDisk(root, seed + 999, 220, rMax, 0.5f);
            for (int i = 0; i < bigCount; i++) ScatterOne(root, bigs, rnd, rMin, rMax, bigH);
            for (int i = 0; i < smallCount && smalls.Count > 0; i++) ScatterOne(root, smalls, rnd, rMin * 0.8f, rMax, smallH);
            extra?.Invoke(root); // bölgeye özel ekstralar (hayvan/iskele vb.)
            SavePrefab(root, outName);
        }
        finally { Object.DestroyImmediate(root); }
        Debug.Log("[MMO] üretildi: " + outName + ".prefab (" + bigCount + " ağaç + " + smallCount + " süs)");
    }

    // Çim modellerini (Grass_Small/Large/Large_Extruded) MERKEZDEN başlayarak tüm diske
    // uniform dağıtır (sqrt-örnekleme ile merkeze yığılma önlenir). Ağır olmasın diye küçük ölçek.
    static void ScatterGrassDisk(GameObject root, int seed, int count, float rMax, float height)
    {
        var grass = CollectByPrefix(M + "Nature", new[] { "Grass_" });
        if (grass.Count == 0) { Debug.LogWarning("[MMO] çim parçası yok (Grass_*)"); return; }
        var rnd = new System.Random(seed);
        for (int i = 0; i < count; i++)
        {
            var src = grass[rnd.Next(grass.Count)];
            float ang = (float)(rnd.NextDouble() * Mathf.PI * 2.0);
            float rad = Mathf.Sqrt((float)rnd.NextDouble()) * rMax; // uniform disk (merkez dahil)
            var pos = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
            var inst = (GameObject)Object.Instantiate(src, pos, Quaternion.Euler(0, (float)(rnd.NextDouble() * 360.0), 0) * src.transform.rotation, root.transform);
            ScaleToHeight(inst, height * (0.7f + (float)rnd.NextDouble() * 0.6f));
            StripColliders(inst);
        }
    }

    static void ScatterOne(GameObject root, List<GameObject> pool, System.Random rnd, float rMin, float rMax, float height)
    {
        var src = pool[rnd.Next(pool.Count)];
        float ang = (float)(rnd.NextDouble() * Mathf.PI * 2.0);
        float rad = rMin + (float)rnd.NextDouble() * (rMax - rMin);
        var pos = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
        var inst = (GameObject)Object.Instantiate(src, pos, Quaternion.Euler(0, (float)(rnd.NextDouble() * 360.0), 0) * src.transform.rotation, root.transform);
        ScaleToHeight(inst, height * (0.8f + (float)rnd.NextDouble() * 0.5f));
        StripColliders(inst);
    }

    // ---------- Pazar meydanı (spawn'ın kuzeyinde küçük yay) ----------
    static void SetupMarket()
    {
        var root = new GameObject("market_meadow");
        try
        {
            Place(root, "Town", "Arch", new Vector3(0, 0, 6.5f), 180f, 4.0f);
            Place(root, "Town", "Table_Big", new Vector3(-3.5f, 0, 5.5f), 20f, 1.2f);
            Place(root, "Town", "Table_Small", new Vector3(3.5f, 0, 5.5f), -15f, 1.0f);
            Place(root, "Town", "Barrel", new Vector3(-5.2f, 0, 4.2f), 0f, 1.1f);
            Place(root, "Town", "Barrel2", new Vector3(-4.6f, 0, 3.4f), 40f, 1.0f);
            Place(root, "Town", "Crate", new Vector3(5.0f, 0, 4.0f), 15f, 1.0f);
            Place(root, "Town", "Chest", new Vector3(4.4f, 0, 3.2f), -30f, 0.9f);
            Place(root, "Town", "Torch", new Vector3(-2.2f, 0, 6.8f), 0f, 2.2f);
            Place(root, "Town", "Torch", new Vector3(2.2f, 0, 6.8f), 0f, 2.2f);
            Place(root, "Town", "Banner", new Vector3(-6.0f, 0, 6.0f), 90f, 2.6f);
            Place(root, "Town", "Banner", new Vector3(6.0f, 0, 6.0f), -90f, 2.6f);
            Place(root, "Town", "Statue_Horse", new Vector3(0, 0, 9.5f), 180f, 3.2f);
            Place(root, "Town", "Woodfire", new Vector3(0, 0, 3.2f), 0f, 0.8f);
            // NPC satıcılar (Heroes paketi) — tezgahların arkasında dururlar
            PlaceNPC(root, "Warrior", new Vector3(-3.5f, 0, 6.6f), 170f, 1.9f);
            PlaceNPC(root, "Wizard", new Vector3(3.5f, 0, 6.6f), 190f, 1.9f);
            PlaceNPC(root, "Cleric", new Vector3(-6.0f, 0, 4.8f), 120f, 1.9f);
            // Silah tezgahı: kaide üstünde sergilenen altın kılıç + kalkan (Weapons paketi vitrini)
            Place(root, "Town", "Pedestal2", new Vector3(6.2f, 0, 4.5f), 0f, 1.0f);
            Place(root, "Weapons", "Sword_Golden", new Vector3(6.2f, 1.0f, 4.5f), 30f, 1.0f);
            Place(root, "Town", "Pedestal", new Vector3(7.8f, 0, 3.2f), 0f, 1.0f);
            Place(root, "Weapons", "Shield_Celtic_Golden", new Vector3(7.8f, 1.0f, 3.2f), 200f, 0.9f);
            Place(root, "Town", "Bag_Coins", new Vector3(5.2f, 0, 3.4f), 45f, 0.6f);
            // Pazar köpeği :)
            PlaceAnimal(root, "ShibaInu", new Vector3(1.5f, 0, 2.2f), 250f, 0.7f);
            SavePrefab(root, "market_meadow");
        }
        finally { Object.DestroyImmediate(root); }
        Debug.Log("[MMO] üretildi: market_meadow.prefab (başlangıç pazarı)");
    }

    // ---------- Dekorlu 3 zindan varyantı ----------
    static void SetupDungeons()
    {
        string[] rooms = { "room-large", "room-wide", "room-large-variation" };
        for (int v = 0; v < 3; v++)
        {
            var root = new GameObject("dungeon_env_" + v);
            try
            {
                var roomPath = FindFbx(M + "FBX format", rooms[v]);
                if (roomPath != null)
                {
                    var room = AssetDatabase.LoadAssetAtPath<GameObject>(roomPath);
                    var inst = (GameObject)Object.Instantiate(room, Vector3.zero, room.transform.rotation, root.transform);
                    var b = Measure(inst);
                    if (b.size.x > 0.01f)
                    {
                        float s = 30f / Mathf.Min(b.size.x, b.size.z);
                        inst.transform.localScale = Vector3.one * s;
                        b = Measure(inst);
                        inst.transform.position = new Vector3(-b.center.x, 0.02f - b.min.y, -b.center.z);
                    }
                    StripColliders(inst);
                }
                // Giriş kapısı (kuzey) + iki yan koridor — tek odadan gerçek zindan hissine geçiş.
                Place(root, "FBX format", "gate", new Vector3(0, 0, 15f), 0, 5.0f);
                Place(root, "FBX format", "corridor", new Vector3(0, 0, 20f), 0, 5.0f);
                Place(root, "FBX format", "corridor", new Vector3(0, 0, 25f), 0, 5.0f);
                Place(root, "FBX format", "corridor-corner", new Vector3(-15f, 0, 0), 90f, 5.0f);
                Place(root, "FBX format", "corridor-corner", new Vector3(15f, 0, 0), -90f, 5.0f);
                // varyanta özel dekor
                if (v == 0)
                {
                    Place(root, "DungeonDeco", "Column", new Vector3(-6, 0, -3), 0, 3.2f);
                    Place(root, "DungeonDeco", "Column", new Vector3(6, 0, -3), 0, 3.2f);
                    Place(root, "DungeonDeco", "Candelabrum_tall", new Vector3(-4, 0, 6), 0, 2.0f);
                    Place(root, "DungeonDeco", "Candelabrum_tall", new Vector3(4, 0, 6), 0, 2.0f);
                    Place(root, "Town", "Coin_Pile", new Vector3(1.5f, 0, -7.5f), 0, 0.45f);
                    Place(root, "Town", "Vase", new Vector3(-6.5f, 0, 6.5f), 20f, 0.9f);
                }
                else if (v == 1)
                {
                    Place(root, "DungeonDeco", "Carpet", new Vector3(0, 0, -2), 0, 0.15f, 7f);
                    Place(root, "DungeonDeco", "Chest_gold", new Vector3(-6, 0, -7), 30, 1.0f);
                    Place(root, "DungeonDeco", "Book_Open", new Vector3(5, 0, -6), 0, 0.5f);
                    Place(root, "DungeonDeco", "Candelabrum", new Vector3(6, 0, 5), 0, 1.6f);
                    Place(root, "Items", "Gold_Ingots", new Vector3(-4.5f, 0, -7.5f), 15f, 0.4f);
                    Place(root, "Town", "Bag_Coins", new Vector3(-7f, 0, -5.5f), 70f, 0.55f);
                }
                else
                {
                    Place(root, "DungeonDeco", "Bones", new Vector3(-5, 0, -5), 20, 0.6f);
                    Place(root, "DungeonDeco", "Bones2", new Vector3(4, 0, -7), -35, 0.6f);
                    Place(root, "DungeonDeco", "Barrel", new Vector3(6, 0, 4), 0, 1.1f);
                    Place(root, "DungeonDeco", "Candle", new Vector3(-6, 0, 5), 0, 0.6f);
                    Place(root, "Items", "Crystal1", new Vector3(-2.5f, 0, -8f), 0f, 1.3f);
                    Place(root, "Town", "Skull", new Vector3(3f, 0, -5.5f), 110f, 0.45f);
                    Place(root, "Town", "Spikes", new Vector3(7f, 0, -2f), 0f, 0.6f);
                }
                SavePrefab(root, "dungeon_env_" + v);
            }
            finally { Object.DestroyImmediate(root); }
        }
        Debug.Log("[MMO] üretildi: dungeon_env_0/1/2 (dekorlu varyantlar)");
    }

    // Bir silahı Animator'ın sağ el kemiğine sabit takar (statik mob'lar için — oyuncunun
    // silahı runtime'da NetClient.UpdateWeaponVisual ile değişir).
    static void AttachToHand(Animator an, string dir, string fbxName, float length)
    {
        var hand = an.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null) return;
        var path = FindFbx(dir, fbxName);
        if (path == null) return;
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (src == null) return;
        var w = (GameObject)Object.Instantiate(src, hand, false);
        w.transform.localPosition = Vector3.zero;
        w.transform.localRotation = src.transform.rotation;
        var b = Measure(w);
        float dim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (dim > 0.001f) w.transform.localScale = Vector3.one * (length / dim); // yalnız ölçek, konum sabit (elde)
    }

    // ---------- Elde tutulan silah görselleri ----------
    // İki katman: (1) sınıf yedeği weapon_blade/bow/staff, (2) tier'a özel weapon_<defID>
    // (sword_iron/steel/mithril, bow_wood, staff_oak). Runtime önce defID'yi dener, yoksa sınıfa düşer.
    static void SetupWeapons()
    {
        // sınıf yedekleri
        SetupHandWeapon(M + "Weapons", "Sword", "weapon_blade", 1.05f);
        SetupHandWeapon(M + "Weapons", "Bow_Wooden", "weapon_bow", 0.95f);
        SetupHandWeapon(M + "Heroes", "Wizard_Staff", "weapon_staff", 1.3f);
        // tier'a özel (kuşanılan gerçek silah defID'si) — görsel ilerleme hissi
        SetupHandWeapon(M + "Weapons", "Sword", "weapon_sword_iron", 1.05f);       // T2
        SetupHandWeapon(M + "Weapons", "Sword_2", "weapon_sword_steel", 1.15f);    // T3
        SetupHandWeapon(M + "Weapons", "Sword_Golden", "weapon_sword_mithril", 1.25f); // T4 (altın=epik)
        SetupHandWeapon(M + "Weapons", "Bow_Wooden", "weapon_bow_wood", 0.95f);
        SetupHandWeapon(M + "Heroes", "Wizard_Staff", "weapon_staff_oak", 1.3f);
    }

    static void SetupHandWeapon(string dir, string fbxName, string outName, float length)
    {
        var path = FindFbx(dir, fbxName);
        if (path == null) { Debug.LogWarning("[MMO] silah yok: " + fbxName); return; }
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (src == null) return;
        var root = new GameObject(outName);
        try
        {
            var inst = (GameObject)Object.Instantiate(src, Vector3.zero, src.transform.rotation, root.transform);
            ScaleToHeight(inst, length);
            StripColliders(inst);
            SavePrefab(root, outName);
        }
        finally { Object.DestroyImmediate(root); }
        Debug.Log("[MMO] üretildi: " + outName + ".prefab (" + fbxName + ")");
    }

    // ---------- Zırh görselleri (tier'a göre: armor_leather/plate/mithril = sunucu defID'si) ----------
    static void SetupArmor()
    {
        SetupHandWeapon(M + "Items", "Armor_Leather", "gear_armor_leather", 1.05f); // T1
        SetupHandWeapon(M + "Items", "Armor_Metal", "gear_armor_plate", 1.15f);     // T3
        SetupHandWeapon(M + "Items", "Armor_Golden", "gear_armor_mithril", 1.15f);  // T4 (epic)
    }

    // ---------- yardımcılar ----------
    static GameObject Place(GameObject root, string pack, string fbxName, Vector3 pos, float rotY, float height, float widthOverride = 0f)
    {
        var path = FindFbx(M + pack, fbxName);
        if (path == null) { Debug.LogWarning("[MMO] parça yok: " + pack + "/" + fbxName); return null; }
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (src == null) return null;
        var inst = (GameObject)Object.Instantiate(src, Vector3.zero, Quaternion.Euler(0, rotY, 0) * src.transform.rotation, root.transform);
        if (widthOverride > 0f) ScaleToWidth(inst, widthOverride); else ScaleToHeight(inst, height);
        var b = Measure(inst);
        inst.transform.position = new Vector3(pos.x, pos.y + 0.02f - b.min.y, pos.z);
        StripColliders(inst);
        return inst;
    }

    // Faz J (Part 9 "animals never stand still"): hayvana gezinme davranışı tak.
    static void PlaceAnimal(GameObject root, string fbxName, Vector3 pos, float rotY, float height)
    {
        var go = Place(root, "Animals", fbxName, pos, rotY, height);
        if (go != null) { var w = go.AddComponent<MMO.AmbientWander>(); w.radius = 4f; w.speed = 0.8f; }
    }

    // Faz J: NPC'ye küçük yarıçaplı gezinme (köylüler yerinde saymasın).
    static void PlaceNPC(GameObject root, string fbxName, Vector3 pos, float rotY, float height)
    {
        var go = Place(root, "Heroes", fbxName, pos, rotY, height);
        if (go != null) { var w = go.AddComponent<MMO.AmbientWander>(); w.radius = 2f; w.speed = 0.5f; }
    }

    static string FindFbx(string dir, string nameNoExt)
    {
        if (!Directory.Exists(dir)) return null;
        string want = nameNoExt.ToLowerInvariant();
        foreach (var f in Directory.GetFiles(dir, "*.fbx", SearchOption.AllDirectories))
            if (Path.GetFileNameWithoutExtension(f).ToLowerInvariant() == want) return f.Replace('\\', '/');
        foreach (var f in Directory.GetFiles(dir, "*.fbx", SearchOption.AllDirectories)) // gevşek eşleşme
            if (Path.GetFileNameWithoutExtension(f).ToLowerInvariant().Contains(want)) return f.Replace('\\', '/');
        return null;
    }

    static List<GameObject> CollectByPrefix(string dir, string[] prefixes)
    {
        var list = new List<GameObject>();
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.GetFiles(dir, "*.fbx", SearchOption.AllDirectories))
        {
            var n = Path.GetFileNameWithoutExtension(f);
            foreach (var p in prefixes)
                if (n.StartsWith(p, System.StringComparison.OrdinalIgnoreCase))
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(f.Replace('\\', '/'));
                    if (go != null) list.Add(go);
                    break;
                }
        }
        return list;
    }

    static Bounds Measure(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.001f);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static void ScaleToHeight(GameObject go, float h)
    {
        var b = Measure(go);
        if (b.size.y < 0.001f) return;
        go.transform.localScale = go.transform.localScale * (h / b.size.y);
        b = Measure(go);
        go.transform.position = new Vector3(go.transform.position.x, go.transform.position.y - b.min.y, go.transform.position.z);
    }

    static void ScaleToWidth(GameObject go, float w)
    {
        var b = Measure(go);
        float cur = Mathf.Max(b.size.x, b.size.z);
        if (cur < 0.001f) return;
        go.transform.localScale = go.transform.localScale * (w / cur);
    }

    static void StripColliders(GameObject go)
    {
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c); // dekor tıklamayı engellemesin
    }

    static void SavePrefab(GameObject root, string name)
    {
        FixURP(root); // Standard shader -> URP Lit (pembe/renksiz sorunu çözümü; doku korunur)
        string p = Out + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(p) != null) AssetDatabase.DeleteAsset(p);
        PrefabUtility.SaveAsPrefabAsset(root, p);
    }

    static void FixURP(GameObject root)
    {
        var urp = Shader.Find("Universal Render Pipeline/Lit");
        if (urp == null) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || m.shader == null || m.shader.name.Contains("Universal")) continue;
                string dir = Out + "WorldMats/";
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string mpath = dir + m.name + "_URP.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(mpath);
                if (existing != null) { mats[i] = existing; continue; }
                var nm = new Material(urp) { name = m.name + "_URP" };
                if (m.HasProperty("_MainTex") && m.mainTexture != null) nm.SetTexture("_BaseMap", m.mainTexture);
                if (m.HasProperty("_Color")) nm.SetColor("_BaseColor", m.color);
                AssetDatabase.CreateAsset(nm, mpath);
                mats[i] = nm;
            }
            r.sharedMaterials = mats;
        }
    }
}
#endif
