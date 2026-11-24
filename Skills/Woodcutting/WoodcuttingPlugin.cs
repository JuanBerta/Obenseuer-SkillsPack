using System;
using System.IO;
using System.Reflection;
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
        internal static ConfigEntry<float> cfgChopPower, cfgSwing, cfgYield, cfgXpBase, cfgXpPerHit, cfgXpPerWood;
        internal static ConfigEntry<bool> cfgNotify, cfgSpeed, cfgExtraWood, cfgMushrooms;
        internal static ConfigEntry<float> cfgDrunkFail, cfgDrunkFailXp, cfgDrunkOutput;

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
            cfgXpBase = Config.Bind("Skill", "XpPerLevel", 1000f, "XP needed for each level, like the game's own skills (1000 XP per level).");
            cfgXpPerHit = Config.Bind("Skill", "XpPerHit", 10f, "XP gained for each axe hit on a tree or log that still has wood.");
            cfgXpPerWood = Config.Bind("Skill", "XpPerWood", 40f, "Extra XP gained each time a tree or log gives you wood.");
            cfgSpeed = Config.Bind("Faster chopping", "Enabled", true, "Faster chopping with the axe as your level rises.");
            cfgChopPower = Config.Bind("Faster chopping", "ChopPowerPerLevel", 0.15f, "Extra damage per level against trees and logs (0.15 = +15% per level, so 2.5x at level 10): fewer hits per piece of wood.");
            cfgSwing = Config.Bind("Faster chopping", "SwingSpeedPerLevel", 0.04f, "Extra axe swing speed per level (0.04 = +40% at level 10).");
            cfgExtraWood = Config.Bind("More wood", "Enabled", true, "More wood each time a tree or log gives you wood.");
            cfgYield = Config.Bind("More wood", "ExtraWoodPerLevel", 0.2f, "Extra wood per yield per level (0.2 = +2 wood at level 10). The fraction is a chance: 0.5 at level 5 means a 50% chance of +1.");
            cfgMushrooms = Config.Bind("State", "MushroomsBoostSkill", true, "Mushrooms raise your effective Woodcutting level while you are high, like the game's own skills: (level + high / 10) x (1 - SMV progression / 200).");
            cfgDrunkFail = Config.Bind("State", "DrunkMissChance", 0.5f, new ConfigDescription("Chance that an axe hit is a clumsy miss when completely drunk (scaled by alcohol in blood and your Drinking skill). A miss does only 1 damage and gives no bonus wood.", new AcceptableValueRange<float>(0f, 1f)));
            cfgDrunkFailXp = Config.Bind("State", "DrunkMissXpFactor", 3f, new ConfigDescription("A clumsy miss gives this many times the normal XP per hit: you learn from your mistakes.", new AcceptableValueRange<float>(1f, 10f)));
            cfgDrunkOutput = Config.Bind("State", "DrunkOutputLoss", 0.4f, new ConfigDescription("How much weaker your chopping and bonus wood get when completely drunk (0.4 = 40% less).", new AcceptableValueRange<float>(0f, 1f)));
            cfgNotify = Config.Bind("General", "ShowNotifications", true, "Show a message when you gain a level.");
            SkillsPackMod.SafePatch.All(typeof(Patches), "com.juanberta.obenseuer.woodcutting", Logger);
            Logger.LogInfo("Woodcutting 1.1.0 loaded.");
        }

        internal void Warn(string m) { Logger.LogWarning(m); }
        internal void Info(string m) { Logger.LogInfo(m); }

        void OnDestroy() { Save(); }
        void OnApplicationQuit() { Save(); }

        internal static int MaxLevel { get { return Mathf.Clamp(cfgMaxLevel.Value, 1, 50); } }
        internal static float NeededForNext { get { return Mathf.Max(1f, cfgXpBase.Value); } }

        internal static void GainXp(float amount)
        {
            if (Level >= MaxLevel) return;
            bool first = Level == 0 && Xp <= 0f;
            Xp += Mathf.Max(0f, amount);
            if (first && cfgNotify.Value && Notifications.instance != null)
                Notifications.instance.CreateNotification("Woodcutting", "You are learning to chop. Progress is in the Character tab.", false);
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

        // The level used for the effects: the game's mushroom rule (PlayerStats.GetSkillWithEffects).
        internal static float Eff
        {
            get
            {
                float l = Level;
                if (l <= 0f) return 0f;
                try
                {
                    var ps = PlayerStats.instance;
                    if (cfgMushrooms.Value && ps != null && ps.mushrooms != null)
                        l = (l + ps.mushrooms.MushroomsHigh / 10f) * (1f - ps.mushrooms.SMVProgression / 200f);
                }
                catch { }
                return Mathf.Clamp(l, 0f, MaxLevel);
            }
        }

        // 0 = sober, 1 = completely drunk (the Drinking skill makes you hold it better).
        internal static float Drunk
        {
            get
            {
                try
                {
                    var ps = PlayerStats.instance;
                    if (ps == null || ps.alcohol == null) return 0f;
                    float tol = Mathf.Clamp(1f - ps.GetSkill(PlayerStats.PlayerSkillType.Drinking) * 0.08f, 0.2f, 1f);
                    return Mathf.Clamp01(ps.alcohol.AlcoholInBlood / 100f * tol);
                }
                catch { return 0f; }
            }
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
        static bool given, miss;
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
                float drunk = WoodcuttingPlugin.Drunk;
                miss = drunk > 0f && UnityEngine.Random.value < drunk * WoodcuttingPlugin.cfgDrunkFail.Value;
                float weak = 1f - drunk * WoodcuttingPlugin.cfgDrunkOutput.Value;
                if (miss) damage = 1;
                else if (WoodcuttingPlugin.cfgSpeed.Value && WoodcuttingPlugin.Eff > 0f)
                    damage = Mathf.Max(1, WoodcuttingPlugin.RandomRound(damage * (1f + WoodcuttingPlugin.Eff * WoodcuttingPlugin.cfgChopPower.Value * weak)));
                else if (drunk > 0f) damage = Mathf.Max(1, WoodcuttingPlugin.RandomRound(damage * weak));
                WoodcuttingPlugin.GainXp(WoodcuttingPlugin.cfgXpPerHit.Value * (miss ? WoodcuttingPlugin.cfgDrunkFailXp.Value : 1f));
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
                WoodcuttingPlugin.GainXp(WoodcuttingPlugin.cfgXpPerWood.Value);
                if (miss || !WoodcuttingPlugin.cfgExtraWood.Value || WoodcuttingPlugin.Eff <= 0f) return;
                int extra = WoodcuttingPlugin.RandomRound(WoodcuttingPlugin.Eff * WoodcuttingPlugin.cfgYield.Value * (1f - WoodcuttingPlugin.Drunk * WoodcuttingPlugin.cfgDrunkOutput.Value));
                if (extra > 0) amount += extra;
            }
            catch (Exception e) { WoodcuttingPlugin.I.Warn(e.Message); }
        }

        // Woodcutting as one more row in the Character tab, drawn with the game's own skill row.
        static CharacterPanelSkill woodRow;

        static void SetText(object comp, string field, string text)
        {
            var f = AccessTools.Field(comp.GetType(), field); if (f == null) return;
            var tmp = f.GetValue(comp); if (tmp == null) return;
            var p = tmp.GetType().GetProperty("text"); if (p != null) p.SetValue(tmp, text, null);
        }

        [HarmonyPatch(typeof(CharacterPanel), "UpdateSkills")]
        [HarmonyPostfix]
        static void PanelPost(CharacterPanel __instance)
        {
            try
            {
                var t = typeof(CharacterPanel);
                var list = (Transform)AccessTools.Field(t, "skillList").GetValue(__instance);
                var prefab = (Transform)AccessTools.Field(t, "skillPrefab").GetValue(__instance);
                if (woodRow == null || woodRow.transform.parent != list)
                    woodRow = UnityEngine.Object.Instantiate(prefab, list).GetComponent<CharacterPanelSkill>();
                int lv = WoodcuttingPlugin.Level;
                bool started = lv >= 1 || WoodcuttingPlugin.Xp > 0f;
                woodRow.gameObject.SetActive(started);
                if (!started) return;
                bool max = lv >= WoodcuttingPlugin.MaxLevel;
                float frac = max ? 0f : Mathf.Clamp01(WoodcuttingPlugin.Xp / WoodcuttingPlugin.NeededForNext);
                woodRow.UpdateSkill(new PlayerStats.PlayerSkill { Type = PlayerStats.PlayerSkillType.None, Skill = Mathf.Min(lv + frac, (float)PlayerStats.maxLevel) });
                SetText(woodRow, "title", "Woodcutting");
                SetText(woodRow, "level", "LVL " + lv);
                SetText(woodRow, "xp", Mathf.FloorToInt(lv * WoodcuttingPlugin.NeededForNext + (max ? 0f : WoodcuttingPlugin.Xp)) + " XP");
                var sp = AccessTools.Field(t, "skillPanel").GetValue(__instance) as GameObject; if (sp != null) sp.SetActive(true);
                var et = AccessTools.Field(t, "emptyPanelText").GetValue(__instance);
                if (et != null) { var p = et.GetType().GetProperty("text"); if (p != null) p.SetValue(et, "", null); }
            }
            catch (Exception e) { WoodcuttingPlugin.I.Warn("Character tab: " + e.Message); }
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
                float lv = WoodcuttingPlugin.Eff;
                if (lv > 0f) __result += lv * WoodcuttingPlugin.cfgSwing.Value;
            }
            catch { }
        }
    }
}
