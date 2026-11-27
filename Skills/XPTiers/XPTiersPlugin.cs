using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;

namespace XPTiersMod
{
	/// <summary>
	/// XP Tiers: skill gain follows how valuable the thing you make is, and the activities that barely taught anything
	/// (mining, jobs, sauna, drinking, building) pay out properly.
	///
	/// Every skill goes through PlayerStats.IncreaseSkill, which cannot tell where the XP came from. So each source
	/// (a recipe being crafted, a crop being planted, a mining hit...) sets a multiplier while it runs and the
	/// IncreaseSkill prefix applies it. Recipes get a tier from the base value of their main output, compared with the
	/// other recipes of the same skill: cheap recipes stay at vanilla XP, rare ones give several times more.
	/// </summary>
	[BepInPlugin("JuanBerta.Obenseuer.XPTiers", "XP Tiers", "1.0.0")]
	public class XPTiersPlugin : BaseUnityPlugin
	{
		internal static ConfigEntry<float> Global, Tier1, Tier2, Tier3, Tier4, Mining, Jobs, Sauna, Drinking, Building;
		internal static ConfigEntry<bool> ApplyToRecipes, OutcomeXP, IgnoreDrunk;
		internal static ConfigEntry<float> SuccessXP, FailedXP;
		internal static ConfigEntry<string> Overrides;
		internal static ConfigEntry<string> Cutoffs;

		private void Awake()
		{
			if (!SkillsPackMod.SkillsPack.Use("XPTiers", this)) return;   // Skills Pack: per-skill on/off

			Global = Config.Bind("General", "GlobalMultiplier", 1f, new ConfigDescription("Multiplies every skill gain (after the rest). 1 = no change.", new AcceptableValueRange<float>(0f, 20f)));
			ApplyToRecipes = Config.Bind("Recipes", "TierRecipes", true, "Crafting, cooking, brewing, planting and every other recipe give XP by tier. Off = vanilla XP for recipes.");
			Tier1 = Config.Bind("Recipes", "Tier1Multiplier", 1f, new ConfigDescription("XP multiplier for the cheapest recipes of a skill (tier 1). 1 = vanilla.", new AcceptableValueRange<float>(0f, 20f)));
			Tier2 = Config.Bind("Recipes", "Tier2Multiplier", 1.5f, new ConfigDescription("Tier 2.", new AcceptableValueRange<float>(0f, 20f)));
			Tier3 = Config.Bind("Recipes", "Tier3Multiplier", 2.5f, new ConfigDescription("Tier 3.", new AcceptableValueRange<float>(0f, 20f)));
			Tier4 = Config.Bind("Recipes", "Tier4Multiplier", 4f, new ConfigDescription("Tier 4: the most valuable recipes of a skill.", new AcceptableValueRange<float>(0f, 20f)));
			Cutoffs = Config.Bind("Recipes", "TierCutoffs", "0.4,0.7,0.9", "Where tiers 2, 3 and 4 start, as a share of the recipes of that skill ordered by the value of their main output. 0.4,0.7,0.9 = the cheapest 40% are tier 1, the next 30% tier 2, the next 20% tier 3, the top 10% tier 4.");
			Overrides = Config.Bind("Recipes", "Overrides", "", "Fix a recipe's tier by hand: Recipe name=tier, separated by ; (for example: Gin=4;Boiled egg=1). Names as shown in the game.");
			OutcomeXP = Config.Bind("Recipes", "OutcomeXP", true, "Crafting XP depends on how the craft went: a success pays SuccessMultiplier, a failed craft pays FailedMultiplier. In the vanilla game the XP of a craft grows with its fail chance, so a drunk or under-skilled crafter who fails a lot earns more than a sober one who succeeds. Off = vanilla.");
			IgnoreDrunk = Config.Bind("Recipes", "IgnoreDrunkness", true, "Crafting XP is worked out from the fail chance you would have sober, so being drunk no longer pays more XP.");
			SuccessXP = Config.Bind("Recipes", "SuccessMultiplier", 1.5f, new ConfigDescription("XP multiplier for a craft that succeeds (on top of the tier). 1 = vanilla.", new AcceptableValueRange<float>(0f, 20f)));
			FailedXP = Config.Bind("Recipes", "FailedMultiplier", 0.4f, new ConfigDescription("XP multiplier for a craft that fails (on top of the tier). 1 = vanilla.", new AcceptableValueRange<float>(0f, 20f)));
			Mining = Config.Bind("Activities", "Mining", 8f, new ConfigDescription("Multiplier for skill from mining and chopping (the game gives 0.005 per hit).", new AcceptableValueRange<float>(0f, 50f)));
			Jobs = Config.Bind("Activities", "Jobs", 3f, new ConfigDescription("Multiplier for skill from timed jobs (waiting work). Jobs that need a higher skill give more on top of this.", new AcceptableValueRange<float>(0f, 50f)));
			Sauna = Config.Bind("Activities", "Sauna", 6f, new ConfigDescription("Multiplier for Sauna skill while you are in a hot sauna.", new AcceptableValueRange<float>(0f, 50f)));
			Drinking = Config.Bind("Activities", "Drinking", 2f, new ConfigDescription("Multiplier for Drinking skill from drinks with alcohol.", new AcceptableValueRange<float>(0f, 50f)));
			Building = Config.Bind("Activities", "Building", 2f, new ConfigDescription("Multiplier for skill from finishing a furniture blueprint.", new AcceptableValueRange<float>(0f, 50f)));

			var h = new Harmony("JuanBerta.Obenseuer.XPTiers");
			Patch(h, typeof(PlayerStats), "IncreaseSkill", nameof(Patches.IncreaseSkill), null);
			Patch(h, typeof(CraftingBase), "CreateItems", nameof(Patches.CraftStart), nameof(Patches.Reset));
			try
			{
				var gcr = AccessTools.Method(typeof(Recipe), "GetCraftingResult");
				var ss = AccessTools.Method(typeof(PlayerStats), "SkillSuccess");
				if (gcr != null && ss != null)
				{
					h.Patch(gcr, prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.ResultStart)), postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.ResultDone)), finalizer: new HarmonyMethod(typeof(Patches), nameof(Patches.ResultEnd)));
					h.Patch(ss, prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.SkillRollStart)), postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.SkillRollDone)));
				}
				else Logger.LogWarning("XP Tiers: Recipe.GetCraftingResult or PlayerStats.SkillSuccess not found, crafting XP keeps the vanilla success/fail split.");
			}
			catch (Exception e) { Logger.LogWarning("XP Tiers: could not patch the craft outcome: " + e.Message); }
			Patch(h, typeof(Growing), "StartGrow", nameof(Patches.GrowStart), nameof(Patches.Reset), new[] { typeof(Recipe), typeof(int), typeof(GrowingSpot), typeof(ItemStack[]) });
			Patch(h, typeof(WorkableResourceSource), "Damage", nameof(Patches.MiningStart), nameof(Patches.Reset));
			Patch(h, typeof(InteractableWaitWork), "WaitDone", nameof(Patches.JobStart), nameof(Patches.Reset));
			Patch(h, typeof(PlayerStatsSauna), "ApplySauna", nameof(Patches.SaunaStart), nameof(Patches.Reset));
			Patch(h, typeof(PlayerStatsAlcohol), "UpdateValues", nameof(Patches.DrinkStart), nameof(Patches.Reset));
			Patch(h, typeof(ShopBaseUI), "OnCraftingDone", nameof(Patches.BuildStart), nameof(Patches.Reset));
			Logger.LogInfo("XP Tiers loaded.");
		}

		private void Patch(Harmony h, Type type, string method, string prefix, string finalizer, Type[] args = null)
		{
			try
			{
				var m = AccessTools.Method(type, method, args);
				if (m == null) { Logger.LogWarning("XP Tiers: " + type.Name + "." + method + " not found, that source keeps vanilla XP."); return; }
				h.Patch(m,
					prefix: new HarmonyMethod(typeof(Patches), prefix),
					finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Patches), finalizer));
			}
			catch (Exception e) { Logger.LogWarning("XP Tiers: could not patch " + type.Name + "." + method + ": " + e.Message); }
		}
	}

	internal static class Patches
	{
		// The multiplier of whatever is awarding XP right now (1 outside the patched sources).
		private static float ctx = 1f;

		// ---- craft outcome ----------------------------------------------------------------------------------
		// The game pays crafting XP three times in proportion to the fail chance: in the roll (full XP for a success, half
		// for a failure) and once more when the items are made. The roll is taken over here, so the outcome is known.
		private static bool inCreate, inResult, awarding, rolled, lastSuccess;
		private static float soberChance = -1f, lastRatio = 1f;
		private static PlayerStats.PlayerSkillType rollSkill;
		private static float rollChance, rollGain;
		private static int rollTime;

		internal static Exception Reset(Exception __exception) { ctx = 1f; inCreate = false; rolled = false; return __exception; }

		private static bool Outcome() { return XPTiersPlugin.OutcomeXP != null && XPTiersPlugin.OutcomeXP.Value; }

		internal static void ResultStart(Recipe __instance, float equipmentQuality, CraftingBase.CraftingInputData inputdata)
		{
			inResult = true; rolled = false; soberChance = -1f;
			try
			{
				if (!Outcome() || !XPTiersPlugin.IgnoreDrunk.Value) return;
				// the fail chance without the alcohol in the blood
				var alc = PlayerStats.instance.alcohol; float keep = alc.AlcoholInBlood;
				float factor = inputdata != null && inputdata.groupCraftingValues != null ? inputdata.groupCraftingValues.failFactor : 1f;
				try { alc.AlcoholInBlood = 0f; soberChance = Recipe.CurrentFailChance(__instance.FailChance * factor, __instance.RelevantSkill, equipmentQuality, __instance.SkillFactor, __instance.MinimumSkill); }
				finally { alc.AlcoholInBlood = keep; }
			}
			catch (Exception) { soberChance = -1f; }
		}

		internal static void ResultDone(bool __result) { lastSuccess = __result; }
		internal static Exception ResultEnd(Exception __exception) { inResult = false; return __exception; }

		// Take the XP out of the roll itself, so it can be paid below with the outcome known.
		internal static void SkillRollStart(PlayerStats.PlayerSkillType skillType, float failChance, ref bool dontIncreaseSkill, float skillGainFactor, int crafttime)
		{
			rolled = false;
			if (!inResult || !Outcome() || dontIncreaseSkill) return;
			rolled = true; dontIncreaseSkill = true;
			rollSkill = skillType; rollChance = failChance; rollGain = skillGainFactor; rollTime = crafttime;
		}

		internal static void SkillRollDone(bool __result)
		{
			if (!rolled) return;
			rolled = false;
			try
			{
				float fc = (XPTiersPlugin.IgnoreDrunk.Value && soberChance >= 0f) ? soberChance : rollChance;
				float value = rollTime > -1 ? rollTime / 1800f : 1f;
				value = Mathf.Clamp(value, 0.05f, 5f);
				float amount = value * rollGain * (__result ? 0.1f : 0.05f) * (fc == 0f ? 0.01f : fc);
				amount *= __result ? XPTiersPlugin.SuccessXP.Value : XPTiersPlugin.FailedXP.Value;
				lastRatio = (rollChance > 0.01f) ? fc / rollChance : 1f;
				awarding = true;
				try { PlayerStats.instance.IncreaseSkill(rollSkill, amount); }
				finally { awarding = false; }
			}
			catch (Exception) { }
		}

		internal static void IncreaseSkill(PlayerStats.PlayerSkillType Type, ref float amount)
		{
			if (Type == PlayerStats.PlayerSkillType.None || amount <= 0f || XPTiersPlugin.Global == null) return;
			float m = ctx * XPTiersPlugin.Global.Value;
			// the XP added when the items are made: scale it by the outcome too
			if (inCreate && !inResult && !awarding && Outcome()) m *= (lastSuccess ? XPTiersPlugin.SuccessXP.Value : XPTiersPlugin.FailedXP.Value) * lastRatio;
			amount *= m;
		}

		internal static void CraftStart(Recipe recipe) { ctx = RecipeMultiplier(recipe); inCreate = true; lastRatio = 1f; }
		internal static void GrowStart(Recipe selectedRecipe) { ctx = RecipeMultiplier(selectedRecipe); }
		internal static void MiningStart() { ctx = XPTiersPlugin.Mining.Value; }
		internal static void SaunaStart() { ctx = XPTiersPlugin.Sauna.Value; }
		internal static void DrinkStart() { ctx = XPTiersPlugin.Drinking.Value; }
		internal static void BuildStart() { ctx = XPTiersPlugin.Building.Value; }

		internal static void JobStart(InteractableWaitWork __instance)
		{
			// Jobs that need more skill are worth more.
			ctx = XPTiersPlugin.Jobs.Value * (1f + 0.25f * Mathf.Max(0, __instance.RequiredSkill));
		}

		// ---- recipe tiers ---------------------------------------------------------------------------------------

		private static Dictionary<PlayerStats.PlayerSkillType, float[]> sorted;   // per skill: ascending output values
		private static float[] all;
		private static Dictionary<string, int> overrides;
		private static string overridesText;
		private static readonly Dictionary<Recipe, float> cache = new Dictionary<Recipe, float>();
		private static int builtFor = -1;

		private static float RecipeMultiplier(Recipe r)
		{
			try
			{
				if (r == null || !XPTiersPlugin.ApplyToRecipes.Value) return 1f;
				Build();
				float cached;
				if (cache.TryGetValue(r, out cached)) return cached;
				float m = Multiplier(TierOf(r));
				cache[r] = m;
				return m;
			}
			catch (Exception) { return 1f; }
		}

		private static float Multiplier(int tier)
		{
			switch (tier)
			{
				case 2: return XPTiersPlugin.Tier2.Value;
				case 3: return XPTiersPlugin.Tier3.Value;
				case 4: return XPTiersPlugin.Tier4.Value;
				default: return XPTiersPlugin.Tier1.Value;
			}
		}

		private static float MainValue(Recipe r)
		{
			try
			{
				ItemReference main = r.GetMainOutput();
				if (main != null && main.Item != null && main.Item.Value > 0) return main.Item.Value;
				float best = 0f;
				if (r.output != null)
					foreach (ItemReference k in r.output.Keys)
						if (k != null && k.Item != null && k.Item.Value > best) best = k.Item.Value;
				return best;
			}
			catch (Exception) { return 0f; }
		}

		private static void Build()
		{
			int count = RecipeDatabase.Recipes != null ? RecipeDatabase.Recipes.Count : 0;
			string text = XPTiersPlugin.Overrides.Value + "|" + XPTiersPlugin.Cutoffs.Value;
			if (count == builtFor && text == overridesText && sorted != null) return;
			builtFor = count; overridesText = text; cache.Clear();
			sorted = new Dictionary<PlayerStats.PlayerSkillType, float[]>();
			var groups = new Dictionary<PlayerStats.PlayerSkillType, List<float>>();
			var everything = new List<float>();
			if (RecipeDatabase.Recipes != null)
				foreach (Recipe r in RecipeDatabase.Recipes)
				{
					if (r == null || r.RelevantSkill == PlayerStats.PlayerSkillType.None) continue;
					float v = MainValue(r);
					if (v <= 0f) continue;
					List<float> l;
					if (!groups.TryGetValue(r.RelevantSkill, out l)) groups[r.RelevantSkill] = l = new List<float>();
					l.Add(v); everything.Add(v);
				}
			foreach (var kv in groups) { kv.Value.Sort(); sorted[kv.Key] = kv.Value.ToArray(); }
			everything.Sort(); all = everything.ToArray();

			overrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (string part in XPTiersPlugin.Overrides.Value.Split(';'))
			{
				int eq = part.LastIndexOf('=');
				int t;
				if (eq > 0 && int.TryParse(part.Substring(eq + 1).Trim(), out t)) overrides[part.Substring(0, eq).Trim()] = Mathf.Clamp(t, 1, 4);
			}
		}

		private static float[] CutoffShares()
		{
			float[] d = { 0.4f, 0.7f, 0.9f };
			string[] p = XPTiersPlugin.Cutoffs.Value.Split(',');
			if (p.Length != 3) return d;
			var r = new float[3];
			for (int i = 0; i < 3; i++)
				if (!float.TryParse(p[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r[i])) return d;
			return r;
		}

		private static int TierOf(Recipe r)
		{
			int forced;
			if (!string.IsNullOrEmpty(r.Name) && overrides.TryGetValue(r.Name, out forced)) return forced;
			float v = MainValue(r);
			if (v <= 0f) return 1;
			float[] pool;
			if (!sorted.TryGetValue(r.RelevantSkill, out pool) || pool.Length < 8) pool = all;   // tiny skills: compare with everything
			if (pool == null || pool.Length == 0) return 1;
			// share of recipes strictly cheaper than this one
			int below = 0;
			while (below < pool.Length && pool[below] < v) below++;
			float share = (float)below / pool.Length;
			float[] c = CutoffShares();
			if (share >= c[2]) return 4;
			if (share >= c[1]) return 3;
			if (share >= c[0]) return 2;
			return 1;
		}
	}
}
