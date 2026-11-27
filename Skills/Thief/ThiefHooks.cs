using System;
using HarmonyLib;
using UnityEngine;

namespace ThiefMod
{
	internal static class Gear
	{
		internal static bool Carries(int id)
		{
			try { return Inventory.instance != null && Inventory.instance.GetItemAmountInInventory(id) > 0; }
			catch (Exception) { return false; }
		}

		internal static bool BagWorn
		{
			get
			{
				try
				{
					var s = Inventory.instance.CharacterSlots[5];
					return s != null && s.itemStack != null && s.itemStack.itemId == ThiefPlugin.BagId && s.itemStack.itemAmount > 0;
				}
				catch (Exception) { return false; }
			}
		}

		internal static bool Gloves { get { return Carries(ThiefPlugin.GlovesId); } }
		internal static bool Shoes { get { return Carries(ThiefPlugin.ShoesId); } }
	}

	internal static class Heat
	{
		private static int lastLevel;

		internal static float Value { get { return ThiefPlugin.Save != null ? Mathf.Clamp(ThiefPlugin.Save.heat, 0f, 100f) : 0f; } }

		internal static string LevelName(float h) { return h < 10f ? "Cold" : h < 30f ? "Noticed" : h < 60f ? "Watched" : "Hot"; }
		private static int Level(float h) { return h < 10f ? 0 : h < 30f ? 1 : h < 60f ? 2 : 3; }

		internal static void Add(float amount)
		{
			if (!ThiefPlugin.HeatOn.Value || ThiefPlugin.Save == null || amount <= 0f) return;
			ThiefPlugin.Save.heat = Mathf.Clamp(ThiefPlugin.Save.heat + amount, 0f, 100f);
			int lv = Level(ThiefPlugin.Save.heat);
			if (lv > lastLevel) SkillHost.Notify("Heat", "Heat: " + LevelName(ThiefPlugin.Save.heat) + " (" + Mathf.RoundToInt(ThiefPlugin.Save.heat) + ")");
			lastLevel = lv;
		}

		internal static void OnNewDay()
		{
			if (ThiefPlugin.Save == null) return;
			ThiefPlugin.Save.heat = Mathf.Max(0f, ThiefPlugin.Save.heat - ThiefPlugin.HeatDecayPerDay.Value);
			lastLevel = Level(ThiefPlugin.Save.heat);
		}

		private static GUIStyle style;

		internal static void DrawHud()
		{
			if (!ThiefPlugin.HeatOn.Value || !ThiefPlugin.ShowHeat.Value || Event.current.type != EventType.Repaint) return;
			if (!SkillHost.InGame || Value < 1f) return;
			if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
			float h = Value;
			string text = "Heat " + Mathf.RoundToInt(h) + " - " + LevelName(h);
			Rect r = new Rect(Screen.width - 260f, 8f, 250f, 24f);
			Color old = GUI.color;
			GUI.color = new Color(0f, 0f, 0f, 0.8f);
			GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
			GUI.color = Color.Lerp(Color.white, new Color(1f, 0.35f, 0.2f), h / 100f);
			GUI.Label(r, text, style);
			GUI.color = old;
		}
	}

	internal static class Locks
	{
		internal static float StartedAt;

		internal static Tool Wielded()
		{
			try
			{
				Inventory inv = Inventory.instance;
				for (int slot = 6; slot <= 7; slot++)
				{
					var s = inv.CharacterSlots[slot];
					if (s == null || s.itemStack == null || s.itemStack.itemId < 0 || s.itemStack.itemAmount <= 0) continue;
					Item it = ItemDatabase.instance.FetchItemByID(s.itemStack.itemId);
					if (it == null || Array.IndexOf(it.Categories, "Lockpick") < 0) continue;
					foreach (Tool t in ThiefPlugin.Tools) if (t.Id == it.ID) return t;
				}
			}
			catch (Exception) { }
			return null;
		}

		internal static float Chance(float gameChance)
		{
			Tool t = Wielded();
			float p = gameChance * (t != null ? t.Mult.Value : 1f);
			return Mathf.Clamp(p, ThiefPlugin.MinChance.Value, Mathf.Max(ThiefPlugin.MinChance.Value, ThiefPlugin.MaxChance.Value));
		}

		internal static void Noise(float chance)
		{
			if (chance <= 0f || UnityEngine.Random.value >= chance) return;
			try { var nm = AccessTools.Method(typeof(CharacterHeadController), "PlayerMadeNoise"); if (nm != null) nm.Invoke(null, null); } catch (Exception) { }
			Heat.Add(1.5f);
		}
	}

	internal static class ThiefHooks
	{
		// ---- Lockpicking by percentage ----

		[HarmonyPatch(typeof(LockpickUI), "CheckIfItWasSuccess")]
		[HarmonyPrefix]
		private static bool Check_Prefix(LockpickUI __instance, ref bool __result)
		{
			try
			{
				Tool t = Locks.Wielded();
				if (t != null) Locks.Noise(t.Noise.Value);
				if (!ThiefPlugin.Percent.Value) return true;
				float gameChance = Traverse.Create(__instance).Field("lockpickChange").GetValue<float>();
				__result = UnityEngine.Random.value < Locks.Chance(gameChance);
				return false;
			}
			catch (Exception e) { ThiefPlugin.Log.LogWarning("Thief lock roll: " + e.Message); return true; }
		}

		[HarmonyPatch(typeof(LockpickUI), "StartLockpick", new Type[0])]
		[HarmonyPostfix]
		private static void Start_Postfix(LockpickUI __instance)
		{
			try
			{
				if (!ThiefPlugin.Percent.Value) return;
				Locks.StartedAt = Time.unscaledTime;
				Traverse tr = Traverse.Create(__instance);
				tr.Field("speed").SetValue(0f);   // the pointer stays still: nothing to aim
				float gameChance = tr.Field("lockpickChange").GetValue<float>();
				CustomSkill.SetText(tr.Field("controls").GetValue<Component>(), "Picking... " + Mathf.RoundToInt(Locks.Chance(gameChance) * 100f) + "%");
			}
			catch (Exception) { }
		}

		[HarmonyPatch(typeof(LockpickUI), "Update")]
		[HarmonyPostfix]
		private static void Update_Postfix(LockpickUI __instance)
		{
			try
			{
				if (!ThiefPlugin.Percent.Value || !__instance.lockpicking || __instance.cantCancel) return;
				var ui = Traverse.Create(__instance).Field("lockpickUI").GetValue<GameObject>();
				if (ui == null || !ui.activeInHierarchy) return;
				Tool t = Locks.Wielded();
				float secs = t != null ? t.Seconds.Value : 1.5f;
				if (Time.unscaledTime - Locks.StartedAt >= secs) __instance.OnPressed();
			}
			catch (Exception) { }
		}

		[HarmonyPatch(typeof(PlayerLockpicking), "BreakLockpick")]
		[HarmonyPrefix]
		private static bool Break_Prefix()
		{
			try
			{
				Tool t = Locks.Wielded();
				if (t == null) return true;
				return UnityEngine.Random.value < t.Break.Value;   // false = the tool survives
			}
			catch (Exception) { return true; }
		}

		[HarmonyPatch(typeof(PlayerLockpicking), "ConstructLockpickMessage")]
		[HarmonyPostfix]
		private static void Message_Postfix(float difficulty, ref string __result)
		{
			try
			{
				if (!ThiefPlugin.ShowChance.Value || string.IsNullOrEmpty(__result)) return;
				if (!__result.StartsWith("(Lockpick") || __result.StartsWith("(Lockpicking") || __result.Contains("broke")) return;
				Tool t = Locks.Wielded();
				float p = Locks.Chance(PlayerLockpicking.LockpickChance(difficulty));
				__result = "(" + (t != null ? t.Name : "Lockpick") + " - " + Mathf.RoundToInt(p * 100f) + "%)";
			}
			catch (Exception) { }
		}

		// ---- Heat ----

		[HarmonyPatch(typeof(Crime), "CommitCrime")]
		[HarmonyPostfix]
		private static void Crime_Postfix(int severity, Crime.CrimeType type)
		{
			try
			{
				if (type != Crime.CrimeType.Theft && type != Crime.CrimeType.Burglary && type != Crime.CrimeType.Trespassing && type != Crime.CrimeType.Vandalism && type != Crime.CrimeType.Violence) return;
				float gain = Mathf.Max(1, severity) * ThiefPlugin.HeatPerSeverity.Value;
				if (Gear.Gloves) gain *= 1f - ThiefPlugin.GlovesHeatCut.Value;
				if ((type == Crime.CrimeType.Theft || type == Crime.CrimeType.Burglary) && Gear.BagWorn) gain *= 1f - ThiefPlugin.BagHeatCut.Value;
				Heat.Add(gain);
			}
			catch (Exception) { }
		}

		[HarmonyPatch(typeof(CharacterHeadController), "PlayerMadeNoise")]
		[HarmonyPrefix]
		private static bool Noise_Prefix()
		{
			try { if (ThiefPlugin.ShoesNoiseCut != null && Gear.Shoes && UnityEngine.Random.value < ThiefPlugin.ShoesNoiseCut.Value) return false; }
			catch (Exception) { }
			return true;
		}

		// ---- Prices: heat in shops, fences ----

		private static bool IsFence(Trade t, ItemData d)
		{
			return ThiefPlugin.FencesOn.Value && t.buysStolen && d.owner > 0 && d.owner != t.owner.ID;
		}

		[HarmonyPatch(typeof(Trade), "GetItemStackPrice")]
		[HarmonyPostfix]
		private static void Price_Postfix(Trade __instance, ItemData itemData, ref float __result)
		{
			try
			{
				if (ThiefPlugin.Save == null || itemData == null || itemData.slotController == null) return;
				float heat = Heat.Value / 100f;
				if (itemData.slotController.slot.playerSlot)
				{
					if (!IsFence(__instance, itemData)) return;
					float rep = Mathf.Clamp(ThiefPlugin.Save.fenceRep, 0f, 100f) / 100f;
					__result *= (1f + ThiefPlugin.FenceBonus.Value * rep) * (ThiefPlugin.HeatOn.Value ? 1f - ThiefPlugin.FencePriceCut.Value * heat : 1f);
				}
				else if (ThiefPlugin.HeatOn.Value)
				{
					__result *= 1f + ThiefPlugin.ShopMarkup.Value * heat;
				}
			}
			catch (Exception) { }
		}

		[HarmonyPatch(typeof(Trade), "Sell", new Type[] { typeof(ItemData), typeof(int) })]
		[HarmonyPrefix]
		private static void Sell_Prefix(Trade __instance, ItemData itemData, ref bool __state)
		{
			try { __state = itemData != null && itemData.slotController != null && itemData.slotController.slot.playerSlot && IsFence(__instance, itemData); }
			catch (Exception) { __state = false; }
		}

		[HarmonyPatch(typeof(Trade), "Sell", new Type[] { typeof(ItemData), typeof(int) })]
		[HarmonyPostfix]
		private static void Sell_Postfix(Trade __instance, ItemData itemData, int amount, bool __result, bool __state)
		{
			try
			{
				if (!__result || !__state || ThiefPlugin.Save == null) return;
				float value = Trade.GetItemPrice(itemData.item, itemData.owner, itemData.meta, Money.Currency.OC, 1f, __instance.stolenPriceFactor) * amount;
				float before = ThiefPlugin.Save.fenceRep;
				ThiefPlugin.Save.fenceRep = Mathf.Clamp(before + value / 100f * ThiefPlugin.FenceRepPerOc.Value, 0f, 100f);
				if (Mathf.FloorToInt(ThiefPlugin.Save.fenceRep / 25f) > Mathf.FloorToInt(before / 25f))
					SkillHost.Notify("Fence", "The fence trusts you more: stolen goods pay +" + Mathf.RoundToInt(ThiefPlugin.FenceBonus.Value * ThiefPlugin.Save.fenceRep) + "%.");
			}
			catch (Exception) { }
		}
	}
}
