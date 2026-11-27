using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.Mono;
using BepInEx.Unity.Mono.Configuration;
using HarmonyLib;
using SkillsPackMod;
using UnityEngine;

namespace SaunaAssistMod
{
	/// <summary>
	/// Sauna Assist: throws water on the stones for you (auto-löyly), stops before you overheat (overheat guard),
	/// shows the sauna's numbers on screen (HUD) and has a farm mode that keeps the löyly high for Sauna skill XP
	/// for as long as it is safe. It works on the sauna you are standing in, and never on a sauna contest.
	/// </summary>
	[BepInPlugin("JuanBerta.Obenseuer.SaunaAssist", "Sauna Assist", "1.0.0")]
	public class SaunaAssistPlugin : BaseUnityPlugin
	{
		internal static ConfigEntry<bool> AutoLoyly, FarmMode, ShowHud, StoveOffAtLimit;
		internal static ConfigEntry<float> HudLeft, HudTop, StayMinutes, SmartMax;
		internal static ConfigEntry<bool> Smart;
		internal static ConfigEntry<float> NormalLoyly, FarmLoyly, GuardPercent;
		internal static ConfigEntry<KeyboardShortcut> AutoKey, FarmKey;

		private readonly List<Sauna> saunas = new List<Sauna>();
		private float nextScan, nextTick, flashUntil;
		private Sauna current;
		private string note = "";
		private GUIStyle box, label;

		private void Awake()
		{
			if (!SkillsPack.Use("SaunaAssist", this)) return;   // Skills Pack: per-skill on/off
			AutoLoyly = Config.Bind("Auto-loyly", "Enabled", true, "Throw water on the stones for you while you are in a hot sauna, whenever the loyly is below the level you set.");
			NormalLoyly = Config.Bind("Auto-loyly", "Target", 1.5f, new ConfigDescription("Keep the loyly at about this level. Every effect (less drunk, less addiction, hygiene, Sauna XP) grows with loyly, and so does overheating.", new AcceptableValueRange<float>(0.1f, 10f)));
			Smart = Config.Bind("Auto-loyly", "Smart", true, "Work the loyly out for you: the highest level that still lets you stay StayMinutes before your overheat reaches the guard level. It follows your Sauna skill, the stove's temperature and the overheat you already have. Farm mode and Target are used instead when this is off (farm mode always uses its own target).");
			StayMinutes = Config.Bind("Auto-loyly", "StayMinutes", 30f, new ConfigDescription("Smart mode: how many game minutes you plan to stay in the sauna. A shorter stay allows a higher loyly (more effect per minute).", new AcceptableValueRange<float>(5f, 240f)));
			SmartMax = Config.Bind("Auto-loyly", "SmartMax", 6f, new ConfigDescription("Smart mode never aims above this loyly. Loyly also makes you thirstier (about 3 thirst per minute for each point).", new AcceptableValueRange<float>(0.5f, 15f)));
			SafePatch.All(typeof(SaunaHooks), "JuanBerta.Obenseuer.SaunaAssist", Logger);
			FarmMode = Config.Bind("Skill farming", "Enabled", false, "Farm mode: aim for a much higher loyly, so Sauna skill grows as fast as is safe. The overheat guard still applies.");
			FarmLoyly = Config.Bind("Skill farming", "Target", 4f, new ConfigDescription("The loyly level farm mode aims for.", new AcceptableValueRange<float>(0.5f, 15f)));
			GuardPercent = Config.Bind("Overheat guard", "StopAtPercent", 0.85f, new ConfigDescription("Stop throwing water and warn you once your overheat reaches this share of the level where the heat starts to hurt (that level rises with your Sauna skill). 1 = right at the damage level.", new AcceptableValueRange<float>(0.3f, 1f)));
			StoveOffAtLimit = Config.Bind("Overheat guard", "TurnStoveOffAtLimit", false, "Also switch the stove off when you reach the guard level. The sauna cools down on its own, and you turn the stove back on yourself.");
			ShowHud = Config.Bind("HUD", "Show", true, "Show the sauna's temperature, loyly and your overheat on screen while you are in a sauna.");
			HudLeft = Config.Bind("HUD", "Left", 1f, new ConfigDescription("Where the sauna box sits, as a percent of the screen width from the left edge.", new AcceptableValueRange<float>(0f, 85f)));
			HudTop = Config.Bind("HUD", "Top", 30f, new ConfigDescription("Where the sauna box sits, as a percent of the screen height from the top. The game's notifications use the top-left corner, so the default is lower down.", new AcceptableValueRange<float>(0f, 85f)));
			AutoKey = Config.Bind("Keys", "ToggleAuto", new KeyboardShortcut(KeyCode.Insert), "Turns auto-loyly on or off. The game and the other mods use most letter and function keys, so these defaults are two that stay free.");
			FarmKey = Config.Bind("Keys", "ToggleFarm", new KeyboardShortcut(KeyCode.End), "Turns farm mode on or off.");
			Logger.LogInfo("Sauna Assist loaded.");
		}

		private void Update()
		{
			if (AutoLoyly == null) return;
			try
			{
				if (KeyUtil.IsDown(AutoKey.Value)) { AutoLoyly.Value = !AutoLoyly.Value; Flash("Auto-löyly " + (AutoLoyly.Value ? "on" : "off")); }
				if (KeyUtil.IsDown(FarmKey.Value)) { FarmMode.Value = !FarmMode.Value; Flash("Farm mode " + (FarmMode.Value ? "on" : "off")); }
				if (Time.unscaledTime < nextTick) return;
				nextTick = Time.unscaledTime + 0.25f;
				Tick();
			}
			catch (Exception) { }
		}

		private void Flash(string text) { note = text; flashUntil = Time.unscaledTime + 2.5f; }

		private void Scan()
		{
			saunas.Clear();
			var contest = new HashSet<Sauna>();
			foreach (var p in FindObjectsOfType<SaunaContestParticipant>()) if (p != null && p.contestSauna != null) contest.Add(p.contestSauna);
			foreach (var s in FindObjectsOfType<Sauna>()) if (s != null && !contest.Contains(s)) saunas.Add(s);
		}

		private static bool Inside(Sauna s) { return Traverse.Create(s).Field("Player").GetValue<PlayerStats>() != null; }

		// Game minutes spent in the sauna you are in (counted by a hook on the sauna's own clock).
		internal static Sauna Active;
		internal static float StayedMinutes;

		/// <summary>
		/// The loyly to keep. Farm mode uses its fixed target. Otherwise, with Smart on, it is worked out from the game's own
		/// overheat formula so that your overheat reaches the guard level just as your planned stay ends:
		///   overheat per minute = Overheatrate * (temperature / OverheatThresholdTemp + loyly * k) - cooldown,
		///   k = Lerp(1, 0.33, (skill - 1) * 0.1)  =>  loyly = ((budget / minutes + cooldown) / Overheatrate - temperature / OverheatThresholdTemp) / k
		/// </summary>
		private static float AimLoyly(PlayerStats ps, Sauna s)
		{
			if (FarmMode.Value) return FarmLoyly.Value;
			if (!Smart.Value || ps == null || s == null) return NormalLoyly.Value;
			try
			{
				int skill = ps.GetSkill(PlayerStats.PlayerSkillType.Sauna);
				float k = Mathf.Lerp(1f, 0.33f, (skill - 1) * 0.1f);
				float budget = Mathf.Max(0f, Limit(ps) * GuardPercent.Value - ps.sauna.Overheat);
				float minutes = Mathf.Max(StayMinutes.Value - StayedMinutes, 5f);
				float heat = Mathf.Max(s.temperature, 60f) / s.OverheatThresholdTemp;
				float aim = ((budget / minutes + ps.sauna.OverheatCooldown) / s.Overheatrate - heat) / k;
				return Mathf.Clamp(aim, 0.3f, SmartMax.Value);
			}
			catch (Exception) { return NormalLoyly.Value; }
		}

		private static float Limit(PlayerStats ps)
		{
			int skill = ps.GetSkill(PlayerStats.PlayerSkillType.Sauna);
			return ps.sauna.OverheatThreshold + ps.sauna.OverheatThesholdPerSkillLevel * (skill - 1);
		}

		private void Tick()
		{
			PlayerStats ps = PlayerStats.instance;
			if (ps == null || !ps.Active) { current = null; Active = null; return; }
			if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 3f; Scan(); }
			if (current != null && (!current || !Inside(current))) current = null;
			if (current == null) foreach (var s in saunas) if (s && Inside(s)) { current = s; break; }
			if (current == null) { Active = null; return; }
			if (!ReferenceEquals(Active, current)) { Active = current; StayedMinutes = 0f; }
			if (!current.On || current.passive) return;
			float limit = Limit(ps), ratio = limit > 0f ? ps.sauna.Overheat / limit : 0f;
			bool guarded = ratio >= GuardPercent.Value;
			if (guarded && StoveOffAtLimit.Value) current.TurnOff();
			if (!AutoLoyly.Value || guarded) return;
			float minTemp = Traverse.Create(current).Field("minimumWorkingTemperature").GetValue<float>();
			float target = AimLoyly(ps, current);
			if (current.temperature > minTemp + 1f && current.löyly < target) current.ThrowWater();
		}

		private void OnGUI()
		{
			if (AutoLoyly == null || !ShowHud.Value) return;
			try
			{
				PlayerStats ps = PlayerStats.instance;
				bool flash = Time.unscaledTime < flashUntil;
				if (!flash && (current == null || ps == null || !current)) return;
				if (box == null)
				{
					box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, padding = new RectOffset(10, 10, 8, 8) };
					label = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
				}
				string t = "";
				if (current != null && current && ps != null)
				{
					float limit = Limit(ps), ratio = limit > 0f ? ps.sauna.Overheat / limit : 0f;
					bool warn = ratio >= GuardPercent.Value;
					string col = ratio >= 1f ? "#ff4040" : warn ? "#ffb030" : "#90e090";
					t += "<b>Sauna</b>  lv " + ps.GetSkill(PlayerStats.PlayerSkillType.Sauna) + "\n";
					t += "Temperature  " + current.temperature.ToString("0") + " °C\n";
					t += "Löyly  " + current.löyly.ToString("0.0") + "  (aim " + (current != null && ps != null ? AimLoyly(ps, current) : NormalLoyly.Value).ToString("0.0") + ")\n";
					t += "Overheat  <color=" + col + ">" + Mathf.RoundToInt(ratio * 100f) + "%</color> of the limit\n";
					t += "Auto-löyly " + (AutoLoyly.Value ? "on" : "off") + (FarmMode.Value ? ", farming" : "");
					if (warn) t += "\n<color=" + col + "><b>Too hot: cool off now</b></color>";
				}
				if (flash) t += (t.Length > 0 ? "\n" : "") + note;
				if (t.Length == 0) return;
				int lines = t.Split('\n').Length;
				float hx = Screen.width * HudLeft.Value / 100f, hy = Screen.height * HudTop.Value / 100f;
				GUI.Box(new Rect(hx, hy, 250, 20 + 21 * lines), "", box);
				GUI.Label(new Rect(hx + 10, hy + 6, 240, 40 + 21 * lines), t, label);
			}
			catch (Exception) { }
		}
	}

	// Counts the game minutes you spend in the sauna, on the sauna's own clock, so Smart mode knows how much of your planned stay is left.
	internal static class SaunaHooks
	{
		[HarmonyPatch(typeof(Sauna), "DeltaSeconds")]
		[HarmonyPostfix]
		 private static void Delta_Postfix(Sauna __instance, int sec)
		{
			if (ReferenceEquals(__instance, SaunaAssistPlugin.Active)) SaunaAssistPlugin.StayedMinutes += sec / 60f;
		}
	}
}
