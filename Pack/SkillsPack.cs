using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

[assembly: AssemblyTitle("Obenseuer-SkillsPack")]
[assembly: AssemblyDescription("Skills Pack for Obenseuer: Scavenger, Haggling, Bribery+, Stealth, Scam Artist and Lockpick Learning, Woodcutting, XP Tiers and Sauna Assist in one mod.")]
[assembly: AssemblyCompany("JuanBerta")]
[assembly: AssemblyProduct("Obenseuer Skills Pack")]
[assembly: AssemblyVersion("1.4.1.0")]
[assembly: AssemblyFileVersion("1.4.1.0")]

namespace SkillsPackMod
{
	/// <summary>
	/// Skills Pack: the five skill mods live in this one DLL as five BepInEx plugins with their original IDs, so
	/// saves, skill levels and each skill's settings file carry over unchanged. This class adds an on/off switch per
	/// skill (BepInEx/config/JuanBerta.Obenseuer.SkillsPack.cfg) and warns when an old separate DLL is still installed.
	/// </summary>
	internal static class SkillsPack
	{
		public const string Version = "1.4.1";

		private static ConfigFile cfg;
		private static ManualLogSource log;
		private static readonly Dictionary<string, ConfigEntry<bool>> switches = new Dictionary<string, ConfigEntry<bool>>();

		private static readonly string[][] Skills =
		{
			new[] { "Scavenger", "Scavenging: search trash to level up and find more, and better, loot." },
			new[] { "Haggling", "Haggling: better prices with traders as you level up." },
			new[] { "BriberyPlus", "Bribery+: more ways and better odds to bribe your way out of trouble." },
			new[] { "Stealth", "Stealth: sneak, steal and stay unseen more easily as you level up." },
			new[] { "ScamArtist", "Scam Artist: the Fraud skill and scam invoices that play out over days." },
			new[] { "LockpickLearning", "Lockpick Learning: the Lockpicking skill grows faster from failed attempts and when drunk or high." },
			new[] { "Woodcutting", "Woodcutting: a skill that makes your axe chop faster and your trees give more wood." },
			new[] { "XPTiers", "XP Tiers: recipes give more skill XP the rarer their result is, and mining, jobs, sauna, drinking and building give much more XP." },
			new[] { "SaunaAssist", "Sauna Assist: auto-l\u00f6yly, an overheat guard, a sauna HUD and a Sauna skill farm mode." },
		};

		private static void Init()
		{
			if (cfg != null) return;
			log = BepInEx.Logging.Logger.CreateLogSource("Skills Pack");
			cfg = new ConfigFile(Path.Combine(Paths.ConfigPath, "JuanBerta.Obenseuer.SkillsPack.cfg"), true);
			foreach (string[] s in Skills)
				switches[s[0]] = cfg.Bind("Skills", s[0], true, s[1] + " Off: this skill isn't loaded (needs a game restart). Its progress stays in your saves.");
			log.LogInfo("Obenseuer Skills Pack " + Version + " loaded.");
			WarnAboutOldDlls();
		}

		/// <summary>Called first thing in each skill plugin's Awake. False = the skill is switched off: the
		/// plugin stops there and disables itself.</summary>
		internal static bool Use(string skill, MonoBehaviour plugin)
		{
			try
			{
				Init();
				ConfigEntry<bool> on;
				if (!switches.TryGetValue(skill, out on) || on.Value) return true;
				log.LogInfo(skill + " is switched off in JuanBerta.Obenseuer.SkillsPack.cfg.");
				if (plugin != null) plugin.enabled = false;
				return false;
			}
			catch (Exception e)
			{
				Debug.LogWarning("[Skills Pack] " + e.Message);
				return true;
			}
		}

		// The separate mods share the plugin IDs: BepInEx would load only one copy of each, so tell the player
		// to remove the old DLLs.
		private static void WarnAboutOldDlls()
		{
			try
			{
				foreach (string f in Directory.GetFiles(Paths.PluginPath, "Woodcutting.dll", SearchOption.AllDirectories))
					log.LogWarning("The separate Woodcutting mod is still installed (" + f + "). Skills Pack already includes it: delete that file, or only one copy will load.");
				string self = Path.GetFullPath(typeof(SkillsPack).Assembly.Location);
				foreach (string[] s in Skills)
					foreach (string f in Directory.GetFiles(Paths.PluginPath, "Obenseuer-" + s[0] + ".dll", SearchOption.AllDirectories))
						if (!string.Equals(Path.GetFullPath(f), self, StringComparison.OrdinalIgnoreCase))
							log.LogWarning("The separate " + s[0] + " mod is still installed (" + f + "). Skills Pack already includes it: delete that file, or only one copy will load.");
			}
			catch (Exception) { }
		}
	}
}
