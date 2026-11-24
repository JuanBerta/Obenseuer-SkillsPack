using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using WieldableItem;

namespace WoodcuttingMod
{
    [BepInPlugin("com.juanberta.obenseuer.woodcutting", "Woodcutting", "1.1.0")]
    public class WoodcuttingPlugin : BaseUnityPlugin
    {
        internal static WoodcuttingPlugin I;
        internal static ConfigEntry<int> cfgMaxLevel;
        internal static ConfigEntry<float> cfgChopPower, cfgSwing, cfgYield, cfgXpBase, cfgXpPerHit;
        internal static ConfigEntry<bool> cfgNotify, cfgSpeed, cfgExtraWood;
        internal static ConfigEntry<KeyCode> cfgKey;

        // The skill belongs to the character (save slot): it lives in Saves/<character>/woodcutting.json
        static int lvl;
        static float xpv;
        static string slotKey;
        static float nextCheck;
        static int unsaved;
        internal static int Level { get { EnsureSlot(); return lvl; } private set { lvl = value; } }
        internal static float Xp { get { return xpv; } private set { xpv = value; } }

        void Awake()
        {
            if (!SkillsPackMod.SkillsPack.Use("Woodcutting", this)) return;   // Skills Pack: per-skill on/off
            I = this;
            cfgMaxLevel = Config.Bind("Skill", "MaxLevel", 10, new ConfigDescription("Highest Woodcutting level.", new AcceptableValueRange<int>(1, 50)));
            cfgXpBase = Config.Bind("Skill", "XpPerLevelStep", 15f, "Chops needed to go from level N to N+1 = this x (N+1). 15 means 15 chops for level 1, 30 more for level 2, and so on.");
            cfgXpPerHit = Config.Bind("Skill", "XpPerChop", 1f, "Experience gained for each axe hit on a tree or log that still has wood.");
            cfgSpeed = Config.Bind("Faster chopping", "Enabled", true, "Faster chopping with the axe as your level rises.");
            cfgChopPower = Config.Bind("Faster chopping", "ChopPowerPerLevel", 0.15f, "Extra damage per level against trees and logs (0.15 = +15% per level, so 2.5x at level 10): fewer hits per piece of wood.");
            cfgSwing = Config.Bind("Faster chopping", "SwingSpeedPerLevel", 0.04f, "Extra axe swing speed per level (0.04 = +40% at level 10).");
            cfgExtraWood = Config.Bind("More wood", "Enabled", true, "More wood each time a tree or log gives you wood.");
            cfgYield = Config.Bind("More wood", "ExtraWoodPerLevel", 0.2f, "Extra wood per yield per level (0.2 = +2 wood at level 10). The fraction is a chance: 0.5 at level 5 means a 50% chance of +1.");
            cfgNotify = Config.Bind("General", "ShowNotifications", true, "Show a message when you gain a level.");
            cfgKey = Config.Bind("General", "ShowSkillKey", KeyCode.Home, "Press to see your Woodcutting level and progress.");
            SkillsPackMod.SafePatch.All(typeof(Patches), "com.juanberta.obenseuer.woodcutting", Logger);
            Logger.LogInfo("Woodcutting 1.1.0 loaded.");
        }

        internal void Warn(string m) { Logger.LogWarning(m); }
        internal void Info(string m) { Logger.LogInfo(m); }

        void Update()
        {
            if (cfgKey == null) return;
            try { if (Input.GetKeyDown(cfgKey.Value)) ShowStatus(); } catch { }
        }

        void OnDestroy() { Save(); }
        void OnApplicationQuit() { Save(); }

        internal static int MaxLevel { get { return Mathf.Clamp(cfgMaxLevel.Value, 1, 50); } }
        internal static float NeededForNext { get { return Mathf.Max(1f, cfgXpBase.Value * (Level + 1)); } }

        internal static void ShowStatus()
        {
            if (Notifications.instance == null) return;
            string txt = Level >= MaxLevel ? "Level " + Level + " (max)" : "Level " + Level + " · " + Mathf.FloorToInt(Xp) + "/" + Mathf.CeilToInt(NeededForNext) + " chops to next";
            Notifications.instance.CreateNotification("Woodcutting", txt, true);
        }

        internal static void GainXp()
        {
            if (Level >= MaxLevel) return;
            Xp += Mathf.Max(0f, cfgXpPerHit.Value);
            bool up = false;
            while (Level < MaxLevel && Xp >= NeededForNext)
            {
                Xp -= NeededForNext;
                Level++;
                up = true;
            }
            if (Level >= MaxLevel) Xp = 0f;
            if (up)
            {
                Save();
                if (cfgNotify.Value && Notifications.instance != null)
                    Notifications.instance.CreateNotification("Woodcutting level " + Level + "!", Level >= MaxLevel ? "You are a master woodcutter." : "Chopping got easier.", false);
            }
            else if (++unsaved >= 20) Save();
        }

        // Rounds randomly so small bonuses still work: 1.4 -> 1 (60%) or 2 (40%).
        internal static int RandomRound(float v)
        {
            int f = Mathf.FloorToInt(v);
            return f + (UnityEngine.Random.value < v - f ? 1 : 0);
        }

        // ---------------- per character storage ----------------
        static string CurrentKey()
        {
            try
            {
                var id = PlayerIdentity.identity;
                if (id == null) return null;
                return SaveController.SanitizeCharacterName(id.firstName, id.lastName);
            }
            catch { return null; }
        }

        static string SlotDir(string key) { return Path.Combine(Application.persistentDataPath, "Saves", key); }

        // Notices when another character is loaded (checked once a second) and swaps the skill.
        static void EnsureSlot()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;
            string k = CurrentKey();
            if (k == slotKey) return;
            if (slotKey != null) Save();
            slotKey = k; lvl = 0; xpv = 0f; unsaved = 0;
            if (k != null) Load(k);
            if (I != null) I.Info("Woodcutting skill for " + (k ?? "no character") + ": level " + lvl);
        }

        static void Load(string key)
        {
            try
            {
                string f = Path.Combine(SlotDir(key), "woodcutting.json");
                if (!File.Exists(f)) return;
                var j = JObject.Parse(File.ReadAllText(f));
                lvl = Mathf.Clamp((int)j["level"], 0, MaxLevel);
                xpv = Mathf.Max(0f, (float)j["xp"]);
            }
            catch (Exception e) { if (I != null) I.Warn("Could not read the saved skill: " + e.Message); }
        }

        internal static void Save()
        {
            try
            {
                unsaved = 0;
                if (slotKey == null) return;
                string dir = SlotDir(slotKey);
                // A brand new character has no folder until the game saves for the first time; try again later.
                if (!Directory.Exists(dir)) { unsaved = 15; return; }
                File.WriteAllText(Path.Combine(dir, "woodcutting.json"), new JObject { ["level"] = lvl, ["xp"] = xpv }.ToString());
            }
            catch (Exception e) { if (I != null) I.Warn("Could not save the skill: " + e.Message); }
        }
    }

    static class Patches
    {
        static readonly AccessTools.FieldRef<WorkableResourceSource, int> Stock = AccessTools.FieldRefAccess<WorkableResourceSource, int>("currentStock");
        static WorkableResourceSource cur;
        static int stockBefore;
        static bool given;
        static int logged;

        // Axe hit on a tree or log that still has wood.
        [HarmonyPatch(typeof(WorkableResourceSource), nameof(WorkableResourceSource.Damage))]
        [HarmonyPrefix]
        static void DamagePre(WorkableResourceSource __instance, ref int damage, DamageReceiver.DamageType type)
        {
            cur = null;
            try
            {
                if (type != DamageReceiver.DamageType.Chop) return;
                int stock = Stock(__instance);
                if (stock < 1 || !__instance.CheckDamageType(type)) return;
                if (logged++ < 5) WoodcuttingPlugin.I.Info("Axe hit on " + __instance.name + " (wood left " + stock + ", level " + WoodcuttingPlugin.Level + ")");
                cur = __instance; stockBefore = stock; given = false;
                if (WoodcuttingPlugin.cfgSpeed.Value && WoodcuttingPlugin.Level > 0)
                    damage = Mathf.Max(1, WoodcuttingPlugin.RandomRound(damage * (1f + WoodcuttingPlugin.Level * WoodcuttingPlugin.cfgChopPower.Value)));
                WoodcuttingPlugin.GainXp();
            }
            catch (Exception e) { WoodcuttingPlugin.I.Warn(e.Message); cur = null; }
        }

        [HarmonyPatch(typeof(WorkableResourceSource), nameof(WorkableResourceSource.Damage))]
        [HarmonyFinalizer]
        static void DamageEnd() { cur = null; }

        // The wood handed over when a tree or log yields. Bonus drops come after it and are left alone.
        [HarmonyPatch(typeof(ItemOperations), nameof(ItemOperations.AddItemsAndDropRemaining))]
        [HarmonyPrefix]
        static void AddItemsPre(ref int amount)
        {
            try
            {
                if (cur == null || given || Stock(cur) >= stockBefore) return;
                given = true;
                if (!WoodcuttingPlugin.cfgExtraWood.Value || WoodcuttingPlugin.Level < 1) return;
                int extra = WoodcuttingPlugin.RandomRound(WoodcuttingPlugin.Level * WoodcuttingPlugin.cfgYield.Value);
                if (extra > 0) amount += extra;
            }
            catch (Exception e) { WoodcuttingPlugin.I.Warn(e.Message); }
        }

        // Faster axe swings.
        static readonly AccessTools.FieldRef<MeleeWeapon, DamageReceiver.DamageType> WeaponType = AccessTools.FieldRefAccess<MeleeWeapon, DamageReceiver.DamageType>("damageType");

        [HarmonyPatch(typeof(MeleeWeapon), "get_Speed")]
        [HarmonyPostfix]
        static void SpeedPost(MeleeWeapon __instance, ref float __result)
        {
            try
            {
                if (!WoodcuttingPlugin.cfgSpeed.Value) return;
                if (WeaponType(__instance) != DamageReceiver.DamageType.Chop) return;
                int lv = WoodcuttingPlugin.Level;
                if (lv > 0) __result += lv * WoodcuttingPlugin.cfgSwing.Value;
            }
            catch { }
        }
    }
}
