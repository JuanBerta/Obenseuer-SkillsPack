using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;

namespace LockpickLearningMod
{
	/// <summary>
	/// Lockpick Learning: the game's own Lockpicking skill grows faster from a lockpick that fails, and faster
	/// again when you are drunk or high. The game gives a failed attempt a fifth of what a success gives.
	/// </summary>
	[BepInPlugin("JuanBerta.Obenseuer.LockpickLearning", "Lockpick Learning", "1.0.0")]
	public class LockpickLearningPlugin : BaseUnityPlugin
	{
		internal static ConfigEntry<float> FailureShare, ImpairedBonus;

		private void Awake()
		{
			if (!SkillsPackMod.SkillsPack.Use("LockpickLearning", this)) return;   // Skills Pack: per-skill on/off
			FailureShare = Config.Bind("Learning", "FailureShare", 0.8f,
				new ConfigDescription("How much Lockpicking skill a FAILED attempt gives, as a share of what a success gives. The game uses 0.2. 1 = same as a success.", new AcceptableValueRange<float>(0.2f, 3f)));
			ImpairedBonus = Config.Bind("Learning", "ImpairedBonus", 1f,
				new ConfigDescription("Extra Lockpicking skill from picking while drunk or high (scaled by how impaired you are). 0 = none, 1 = up to double.", new AcceptableValueRange<float>(0f, 4f)));
			var harmony = new Harmony("JuanBerta.Obenseuer.LockpickLearning");
			harmony.Patch(AccessTools.Method(typeof(PlayerStats), "IncreaseSkill"),
				prefix: new HarmonyMethod(typeof(Patches), nameof(Patches.IncreaseSkill)));
			harmony.Patch(AccessTools.Method(typeof(PlayerLockpicking), "BreakLockpick"),
				postfix: new HarmonyMethod(typeof(Patches), nameof(Patches.PickBroke)));
			Logger.LogInfo("Lockpick Learning loaded.");
		}
	}

	internal static class Patches
	{
		private static bool awarding;

		private static float Impaired()
		{
			PlayerStats ps = PlayerStats.instance;
			float impaired = Mathf.Clamp01((ps.alcohol.AlcoholInBlood + ps.mushrooms.MushroomsHigh) / 100f);
			return 1f + LockpickLearningPlugin.ImpairedBonus.Value * impaired;
		}

		// Every miss breaks a pick. The game only pays out when the last pick is gone, so a miss with picks to spare
		// taught nothing. Pay each miss as it happens.
		internal static void PickBroke()
		{
			if (LockpickLearningPlugin.FailureShare == null) return;
			try
			{
				float chance = Traverse.Create(LockpickUI.instance).Field("lockpickChange").GetValue<float>();
				float value = chance > 0f ? 1f / (chance * chance + 35f * chance) : 1f;
				value = Mathf.Clamp(value, 0.01f, 1f);
				awarding = true;
				PlayerStats.instance.IncreaseSkill(PlayerStats.PlayerSkillType.Lockpicking, value * LockpickLearningPlugin.FailureShare.Value * Impaired());
			}
			catch (System.Exception) { }
			finally { awarding = false; }
		}

		internal static void IncreaseSkill(PlayerStats.PlayerSkillType Type, ref float amount)
		{
			if (awarding || Type != PlayerStats.PlayerSkillType.Lockpicking || LockpickLearningPlugin.FailureShare == null) return;
			try
			{
				if (LockpickUI.instance == null || PlayerLockpicking.instance == null) return;
				if (!LockpickUI.instance.WasSuccess()) amount = 0f;   // the misses were already paid one by one
				else amount *= Impaired();
			}
			catch (System.Exception) { }
		}
	}
}
