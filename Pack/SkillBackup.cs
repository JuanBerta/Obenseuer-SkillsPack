using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkillsPackMod
{
	/// <summary>
	/// A second copy of every custom skill's XP, kept in memory and in a small file in BepInEx/config. The game's own save
	/// stays the main storage. This copy is only used when a load brings back no data or older data for the character
	/// (for example a build of the game that does not hand the data back), so progress can only go up, never silently to 0.
	/// </summary>
	internal static class SkillBackup
	{
		private static readonly Dictionary<string, float> data = new Dictionary<string, float>();
		private static bool loaded, hooked, dirty;
		private static float lastFlush;
		private static BepInEx.Logging.ManualLogSource log;

		private static string FilePath { get { return Path.Combine(BepInEx.Paths.ConfigPath, "JuanBerta.Obenseuer.SkillsPack.progress.txt"); } }

		private static string Key(string character, string skill)
		{
			return (character ?? "").Replace('\t', ' ').Replace('\n', ' ') + "\t" + (skill ?? "");
		}

		internal static void Log(string text)
		{
			try
			{
				if (log == null) log = BepInEx.Logging.Logger.CreateLogSource("SkillsPack");
				log.LogInfo(text);
			}
			catch (Exception) { }
		}

		private static void Load()
		{
			if (loaded) return;
			loaded = true;
			try
			{
				if (File.Exists(FilePath))
					foreach (string line in File.ReadAllLines(FilePath))
					{
						string[] p = line.Split('\t');
						float v;
						if (p.Length == 3 && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out v))
							data[Key(p[0], p[1])] = v;
					}
			}
			catch (Exception e) { Log("Could not read the progress backup: " + e.Message); }
		}

		private static void Hook()
		{
			if (hooked) return;
			hooked = true;
			try
			{
				Application.quitting += () => Flush(true);
				SceneManager.sceneUnloaded += s => Flush(true);
			}
			catch (Exception) { }
		}

		internal static float Get(string character, string skill)
		{
			if (string.IsNullOrEmpty(character)) return 0f;
			Load();
			float v;
			return data.TryGetValue(Key(character, skill), out v) ? v : 0f;
		}

		/// <summary>Remember the progress. It only goes up.</summary>
		internal static void Put(string character, string skill, float xp, bool force)
		{
			if (string.IsNullOrEmpty(character) || xp <= 0f) return;
			Load(); Hook();
			string k = Key(character, skill);
			float old;
			if (!data.TryGetValue(k, out old) || xp > old) { data[k] = xp; dirty = true; }
			if (dirty && (force || Time.realtimeSinceStartup - lastFlush > 10f)) Flush(true);
		}

		internal static void Flush(bool force)
		{
			if (!dirty && !force) return;
			try
			{
				var sb = new StringBuilder();
				foreach (var kv in data)
					sb.Append(kv.Key).Append('\t').Append(kv.Value.ToString("0.###", CultureInfo.InvariantCulture)).Append('\n');
				string tmp = FilePath + ".tmp";
				File.WriteAllText(tmp, sb.ToString());
				if (File.Exists(FilePath)) File.Delete(FilePath);
				File.Move(tmp, FilePath);
				dirty = false;
				lastFlush = Time.realtimeSinceStartup;
			}
			catch (Exception e) { Log("Could not write the progress backup: " + e.Message); }
		}

		/// <summary>The most progress known for this character: what was just loaded, what was in memory and the backup.</summary>
		internal static float Best(string character, string skill, params float[] others)
		{
			float best = Get(character, skill);
			foreach (float f in others) if (f > best) best = f;
			return best;
		}
	}

	/// <summary>
	/// Applies a class of [HarmonyPatch] methods one by one. A method the game does not have (the stable branch lacks
	/// some that the playtest has) is skipped with a warning instead of stopping the rest.
	/// </summary>
	internal static class SafePatch
	{
		internal static void All(Type type, string id, BepInEx.Logging.ManualLogSource log)
		{
			var harmony = new Harmony(id + "." + type.FullName);
			foreach (MethodInfo m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
			{
				try
				{
					List<HarmonyMethod> attrs = HarmonyMethodExtensions.GetFromMethod(m);
					if (attrs == null || attrs.Count == 0) continue;
					HarmonyMethod info = HarmonyMethod.Merge(attrs);
					if (info.declaringType == null || string.IsNullOrEmpty(info.methodName)) continue;
					MethodBase original = AccessTools.Method(info.declaringType, info.methodName, info.argumentTypes);
					if (original == null)
					{
						if (log != null) log.LogWarning("Skipped " + type.Name + "." + m.Name + ": " + info.declaringType.Name + "." + info.methodName + " does not exist in this game version.");
						continue;
					}
					bool pre = m.GetCustomAttributes(typeof(HarmonyPrefix), false).Length > 0 || m.Name.EndsWith("Prefix", StringComparison.Ordinal);
					bool post = m.GetCustomAttributes(typeof(HarmonyPostfix), false).Length > 0 || m.Name.EndsWith("Postfix", StringComparison.Ordinal);
					bool fin = m.GetCustomAttributes(typeof(HarmonyFinalizer), false).Length > 0;
					bool tra = m.GetCustomAttributes(typeof(HarmonyTranspiler), false).Length > 0;
					var hm = new HarmonyMethod(m);
					var proc = harmony.CreateProcessor(original);
					if (pre) proc.AddPrefix(hm);
					if (post) proc.AddPostfix(hm);
					if (tra) proc.AddTranspiler(hm);
					if (fin) proc.AddFinalizer(hm);
					proc.Patch();
				}
				catch (Exception e)
				{
					if (log != null) log.LogWarning("Could not patch " + type.Name + "." + m.Name + ": " + e.Message);
				}
			}
		}
	}
}
