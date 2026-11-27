using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using BepInEx.Unity.Mono.Configuration;
using HarmonyLib;
using UnityEngine;

namespace ThiefMod
{
	public class ThiefSave : SkillSaveBase
	{
		protected override string SaveGuid { get { return "JuanBerta_Thief_Save_001"; } }
		protected override string SaveTitle { get { return "Thief"; } }

		public float heat = 0f;        // 0 to 100, falls each in-game day
		public float fenceRep = 0f;    // 0 to 100, grows with every stolen item you sell to a fence
		public string robbed = "";     // people already pickpocketed or shops already hit today: "key|key"

		public bool WasRobbed(string key) { return ("|" + robbed + "|").Contains("|" + key + "|"); }
		public void MarkRobbed(string key) { if (!WasRobbed(key)) robbed = string.IsNullOrEmpty(robbed) ? key : robbed + "|" + key; if (robbed.Length > 4000) robbed = key; }
	}

	/// <summary>A tool that opens locks: how good it is, how fast, how loud, and how often it breaks.</summary>
	internal sealed class Tool
	{
		internal int Id; internal string Name;
		internal ConfigEntry<float> Mult, Break, Seconds, Noise;
	}

	/// <summary>
	/// Thief: new items (lock pick sets, bolt cutters, stealth bag, gloves, soft-soled shoes), lockpicking by percentage
	/// instead of a minigame, heat, fences, pickpocketing and shoplifting.
	/// </summary>
	[BepInPlugin("JuanBerta.Obenseuer.Thief", "Thief", "1.0.0")]
	public class ThiefPlugin : BaseUnityPlugin
	{
		internal const int SetId = 920001, MasterId = 920002, CuttersId = 920003, BagId = 920004, GlovesId = 920005, ShoesId = 920006;
		internal const int CrowbarId = 19690, PickId = 4;

		internal static ThiefSave Save { get { return SkillHost.Save as ThiefSave; } }
		internal static BepInEx.Logging.ManualLogSource Log;

		internal static ConfigEntry<bool> AddItems, Percent, ShowChance, ShowHeat, HeatOn, FencesOn, Pickpocket, Shoplift;
		internal static ConfigEntry<float> MinChance, MaxChance;
		internal static ConfigEntry<float> HeatPerSeverity, HeatDecayPerDay, ShopMarkup, FencePriceCut, GlovesHeatCut, BagHeatCut, ShoesNoiseCut;
		internal static ConfigEntry<float> FenceBonus, FenceRepPerOc;
		internal static ConfigEntry<KeyboardShortcut> PickpocketKey, ShopliftKey;
		internal static ConfigEntry<float> PpBase, PpSleeping, PpGloves, PpShoes, PpMinOc, PpMaxOc, PpHeat, PpDistance;
		internal static ConfigEntry<float> SlBase, SlGloves, SlShoes, SlBag, SlMaxValue, SlHeat;
		internal static ConfigEntry<int> PpSeverity, SlSeverity;
		internal static readonly List<Tool> Tools = new List<Tool>();

		private static ConfigDescription D(string text, int order, AcceptableValueBase range = null) { return CustomSkill.D(text, order, range); }

		private void Awake()
		{
			if (!SkillsPackMod.SkillsPack.Use("Thief", this)) return;   // Skills Pack: per-module on/off
			Log = Logger;
			Bind(Config);
			SkillHost.Init(this, Logger, null, "Thief_Save", go => go.AddComponent<ThiefSave>());
			SkillsPackMod.SafePatch.All(typeof(ThiefHooks), "JuanBerta.Obenseuer.Thief", Logger);
			SkillHost.DayChanged += Heat.OnNewDay;
			Logger.LogInfo("Thief loaded. Items: " + AddItems.Value + ", percentage lockpicking: " + Percent.Value + ".");
		}

		private static Tool Add(ConfigFile cfg, int id, string name, float mult, float brk, float secs, float noise, int order)
		{
			string sec = "Tool - " + name;
			var t = new Tool { Id = id, Name = name };
			t.Mult = cfg.Bind(sec, "SuccessMultiplier", mult, D("Multiplies the game's own chance to open a lock (which already counts your Lockpicking skill, alcohol, mushrooms and how hard the lock is).", 100 - order, new AcceptableValueRange<float>(0.1f, 10f)));
			t.Break = cfg.Bind(sec, "BreakChance", brk, D("Chance that this tool breaks (is used up) on a failed try. The normal lock pick always breaks (1 = always).", 90 - order, new AcceptableValueRange<float>(0f, 1f)));
			t.Seconds = cfg.Bind(sec, "SecondsPerTry", secs, D("How long one try takes, in seconds.", 80 - order, new AcceptableValueRange<float>(0.1f, 10f)));
			t.Noise = cfg.Bind(sec, "Noise", noise, D("Chance per try that the noise carries and counts as you making a noise (people nearby may react) and adds heat. 0 = silent.", 70 - order, new AcceptableValueRange<float>(0f, 1f)));
			Tools.Add(t);
			return t;
		}

		private static void Bind(ConfigFile cfg)
		{
			AddItems = cfg.Bind("General", "AddItems", true, D("Add the Thief items (lock pick sets, bolt cutters, stealth bag, gloves, soft-soled shoes) and their recipes. Turn off to stop them being added; items already in a save stay as unknown items.", 100));
			Percent = cfg.Bind("Lockpicking", "PercentageLockpicking", true, D("Lockpicking is a percentage roll: no minigame. The lock shows your chance, the pick works by itself and the roll decides.", 100));
			ShowChance = cfg.Bind("Lockpicking", "ShowChance", true, D("Show the exact chance on the lock instead of Easy / Difficult.", 90));
			MinChance = cfg.Bind("Lockpicking", "MinChance", 0.02f, D("Lowest chance of a try working.", 80, new AcceptableValueRange<float>(0f, 0.5f)));
			MaxChance = cfg.Bind("Lockpicking", "MaxChance", 0.97f, D("Highest chance of a try working.", 70, new AcceptableValueRange<float>(0.5f, 1f)));
			Add(cfg, PickId, "Lock Pick", 1f, 1f, 1.5f, 0f, 0);
			Add(cfg, SetId, "Lock Pick Set", 1.35f, 0.5f, 1.2f, 0f, 1);
			Add(cfg, MasterId, "Master Lock Pick Set", 1.8f, 0.25f, 0.9f, 0f, 2);
			Add(cfg, CuttersId, "Bolt Cutters", 3f, 0.05f, 0.4f, 0.8f, 3);
			Add(cfg, CrowbarId, "Crowbar", 2.2f, 0.08f, 0.5f, 1f, 4);

			HeatOn = cfg.Bind("Heat", "Enabled", true, D("Crimes build heat. Heat makes shops charge more and fences pay less, and falls by itself each day.", 100));
			ShowHeat = cfg.Bind("Heat", "ShowHeat", true, D("Show your heat in the corner of the screen while it is above zero.", 90));
			HeatPerSeverity = cfg.Bind("Heat", "HeatPerSeverity", 0.1f, D("Heat gained per point of crime severity (theft, burglary, trespassing, vandalism, violence).", 80, new AcceptableValueRange<float>(0f, 2f)));
			HeatDecayPerDay = cfg.Bind("Heat", "DecayPerDay", 12f, D("Heat lost each in-game day.", 70, new AcceptableValueRange<float>(0f, 100f)));
			ShopMarkup = cfg.Bind("Heat", "ShopMarkup", 0.4f, D("Shops charge up to this much more at 100 heat (0.4 = +40%).", 60, new AcceptableValueRange<float>(0f, 2f)));
			FencePriceCut = cfg.Bind("Heat", "FencePriceCut", 0.4f, D("Fences pay up to this much less for stolen goods at 100 heat.", 50, new AcceptableValueRange<float>(0f, 0.9f)));
			GlovesHeatCut = cfg.Bind("Heat", "GlovesHeatCut", 0.3f, D("Carrying Thief's Gloves cuts the heat you gain by this share (no fingerprints).", 40, new AcceptableValueRange<float>(0f, 1f)));
			BagHeatCut = cfg.Bind("Heat", "BagHeatCut", 0.25f, D("Wearing the Stealth Bag cuts the heat you gain from theft by this share.", 30, new AcceptableValueRange<float>(0f, 1f)));
			ShoesNoiseCut = cfg.Bind("Heat", "ShoesNoiseCut", 0.5f, D("Carrying Soft-soled Shoes stops this share of the noises you make.", 20, new AcceptableValueRange<float>(0f, 1f)));

			FencesOn = cfg.Bind("Fences", "Enabled", true, D("Traders who buy stolen goods are fences: sell them stolen items to build a rapport and get better prices.", 100));
			FenceBonus = cfg.Bind("Fences", "BonusAtMaxRapport", 0.5f, D("At full rapport a fence pays this much more for stolen goods (0.5 = +50%).", 90, new AcceptableValueRange<float>(0f, 2f)));
			FenceRepPerOc = cfg.Bind("Fences", "RapportPer100Oc", 6f, D("Rapport gained (0 to 100) per 100 OC of stolen goods sold to a fence.", 80, new AcceptableValueRange<float>(0f, 50f)));

			Pickpocket = cfg.Bind("Pickpocketing", "Enabled", true, D("Press the key while looking at a person to pick their pocket.", 100));
			PickpocketKey = cfg.Bind("Pickpocketing", "Key", new KeyboardShortcut(KeyCode.P), D("Pickpocket key.", 95));
			PpDistance = cfg.Bind("Pickpocketing", "MaxDistance", 1.8f, D("How close you must be, in meters.", 90, new AcceptableValueRange<float>(0.5f, 5f)));
			PpBase = cfg.Bind("Pickpocketing", "BaseChance", 0.3f, D("Chance before skill, sleep, gear and state.", 85, new AcceptableValueRange<float>(0f, 1f)));
			PpSleeping = cfg.Bind("Pickpocketing", "SleepingBonus", 0.4f, D("Added chance when the person is asleep.", 80, new AcceptableValueRange<float>(0f, 1f)));
			PpGloves = cfg.Bind("Pickpocketing", "GlovesBonus", 0.08f, D("Added chance while carrying Thief's Gloves.", 75, new AcceptableValueRange<float>(0f, 1f)));
			PpShoes = cfg.Bind("Pickpocketing", "ShoesBonus", 0.08f, D("Added chance while carrying Soft-soled Shoes.", 70, new AcceptableValueRange<float>(0f, 1f)));
			PpMinOc = cfg.Bind("Pickpocketing", "MinLoot", 4f, D("Smallest haul in OC.", 65, new AcceptableValueRange<float>(1f, 1000f)));
			PpMaxOc = cfg.Bind("Pickpocketing", "MaxLoot", 40f, D("Biggest haul in OC.", 60, new AcceptableValueRange<float>(1f, 5000f)));
			PpHeat = cfg.Bind("Pickpocketing", "HeatOnSuccess", 2f, D("Heat gained for a clean pickpocket.", 55, new AcceptableValueRange<float>(0f, 50f)));
			PpSeverity = cfg.Bind("Pickpocketing", "CaughtSeverity", 60, D("Crime severity when the person catches you (theft, with them as the victim).", 50, new AcceptableValueRange<int>(1, 500)));

			Shoplift = cfg.Bind("Shoplifting", "Enabled", true, D("With a shop open, press the key to try to palm something from the shelf.", 100));
			ShopliftKey = cfg.Bind("Shoplifting", "Key", new KeyboardShortcut(KeyCode.L), D("Shoplift key (works while a trader's window is open).", 95));
			SlBase = cfg.Bind("Shoplifting", "BaseChance", 0.35f, D("Chance before gear and the value of the item.", 90, new AcceptableValueRange<float>(0f, 1f)));
			SlGloves = cfg.Bind("Shoplifting", "GlovesBonus", 0.05f, D("Added chance while carrying Thief's Gloves.", 80, new AcceptableValueRange<float>(0f, 1f)));
			SlShoes = cfg.Bind("Shoplifting", "ShoesBonus", 0.05f, D("Added chance while carrying Soft-soled Shoes.", 70, new AcceptableValueRange<float>(0f, 1f)));
			SlBag = cfg.Bind("Shoplifting", "BagBonus", 0.15f, D("Added chance while wearing the Stealth Bag.", 60, new AcceptableValueRange<float>(0f, 1f)));
			SlMaxValue = cfg.Bind("Shoplifting", "MaxItemValue", 400f, D("Items worth more than this (OC) cannot be palmed. Pricier items are harder: the chance falls with value.", 50, new AcceptableValueRange<float>(10f, 10000f)));
			SlHeat = cfg.Bind("Shoplifting", "HeatOnSuccess", 3f, D("Heat gained for a clean theft.", 40, new AcceptableValueRange<float>(0f, 50f)));
			SlSeverity = cfg.Bind("Shoplifting", "CaughtSeverity", 80, D("Crime severity when the shopkeeper catches you.", 30, new AcceptableValueRange<int>(1, 500)));
		}

		private float nextEnsure;

		private void Update()
		{
			SkillHost.Tick();
			try
			{
				if (Time.unscaledTime >= nextEnsure) { nextEnsure = Time.unscaledTime + 2f; if (AddItems != null && AddItems.Value) ThiefItems.Ensure(); }
				Steal.Tick();
			}
			catch (Exception e) { Logger.LogWarning("Thief: " + e.Message); }
		}

		private void OnGUI() { try { Heat.DrawHud(); Steal.DrawHint(); } catch (Exception) { } }
	}
}


