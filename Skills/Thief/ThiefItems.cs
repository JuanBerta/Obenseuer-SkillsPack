using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ThiefMod
{
	/// <summary>Adds the Thief items and their recipes to the game's databases while it runs (no game file is changed).</summary>
	internal static class ThiefItems
	{
		private static Item Find(int id)
		{
			foreach (Item i in ItemDatabase.database) if (i != null && i.ID == id) return i;
			return null;
		}

		private static Item Clone(Item src, int id, string title, int value, string desc, string[] cats, int stack)
		{
			MethodInfo mc = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
			Item c = (Item)mc.Invoke(src, null);
			c.ID = id; c.Title = title; c.BaseValue = value; c.Description = desc; c.Categories = cats; c.BaseStackable = stack;
			try { if (src.Meta != null) c.Meta = src.CloneMeta(true, 1, src); } catch (Exception) { }
			return c;
		}

		private static bool gaveUp;

		internal static void Ensure()
		{
			if (gaveUp || ItemDatabase.instance == null || ItemDatabase.database == null || ItemDatabase.database.Count == 0) return;
			if (Find(ThiefPlugin.SetId) == null) AddItems();
			if (RecipeDatabase.Recipes != null && RecipeDatabase.Recipes.Count > 0 && Find(ThiefPlugin.SetId) != null) AddRecipes();
		}

		private static void AddItems()
		{
			Item pick = Find(ThiefPlugin.PickId), crow = Find(ThiefPlugin.CrowbarId), bag = Find(15480), cloth = Find(90020), roll = Find(12700);
			if (pick == null || crow == null) { gaveUp = true; ThiefPlugin.Log.LogWarning("Thief: lock pick or crowbar not found, items not added."); return; }

			// The crowbar can now open locks: the game treats anything in the Lockpick category as a pick.
			if (Array.IndexOf(crow.Categories, "Lockpick") < 0)
			{
				var cats = new List<string>(crow.Categories); cats.Add("Lockpick"); crow.Categories = cats.ToArray();
			}
			var list = ItemDatabase.database;
			list.Add(Clone(pick, ThiefPlugin.SetId, "Lock Pick Set", 160, "A proper set of picks and tension wrenches. Opens locks more often and wears out slower.", new[] { "Lockpick", "Wieldable Primary" }, 5));
			list.Add(Clone(pick, ThiefPlugin.MasterId, "Master Lock Pick Set", 520, "Hand-ground steel picks in a leather roll. The best way to open a lock quietly.", new[] { "Lockpick", "Wieldable Primary" }, 3));
			list.Add(Clone(crow, ThiefPlugin.CuttersId, "Bolt Cutters", 260, "Snaps padlocks and chains in seconds. Very loud. Counts as a pick.", new[] { "Tools", "Lockpick", "Wieldable Primary" }, 1));
			if (bag != null) list.Add(Clone(bag, ThiefPlugin.BagId, "Stealth Bag", 2200, "A dull, padded backpack that muffles what is inside. Wear it in the backpack slot to run cooler (less heat from theft) and palm more from shelves.", new[] { "Backpack", "Storage" }, 1));
			if (cloth != null) list.Add(Clone(cloth, ThiefPlugin.GlovesId, "Thief's Gloves", 180, "Thin dark gloves. Carry them for less heat from crimes and a better touch when picking pockets.", new[] { "Thief Gear" }, 1));
			if (roll != null) list.Add(Clone(roll, ThiefPlugin.ShoesId, "Soft-soled Shoes", 240, "Quiet shoes. Carry them to silence a share of your noise and move better around people.", new[] { "Thief Gear" }, 1));
			foreach (Item it in list)
				if (it != null && it.ID >= ThiefPlugin.SetId && it.ID <= ThiefPlugin.ShoesId)
					try { ItemDatabase.LoadItemReferencedAssets(it); } catch (Exception) { }
			ThiefPlugin.Log.LogInfo("Thief: items added (Lock Pick Set, Master Lock Pick Set, Bolt Cutters, Stealth Bag, Thief's Gloves, Soft-soled Shoes). Crowbar can now open locks.");
		}

		private static Recipe FindRecipe(int id)
		{
			foreach (Recipe r in RecipeDatabase.Recipes) if (r != null && r.ID == id) return r;
			return null;
		}

		private static Dictionary<ItemReference, float> Map(params object[] pairs)
		{
			var d = new Dictionary<ItemReference, float>();
			for (int i = 0; i + 1 < pairs.Length; i += 2)
			{
				Item it = Find((int)pairs[i]);
				if (it != null) d[new ItemReference(it)] = Convert.ToSingle(pairs[i + 1]);
			}
			return d;
		}

		private static void AddRecipes()
		{
			if (FindRecipe(920101) != null) return;
			Recipe machining = FindRecipe(10378), sewing = FindRecipe(10489);
			if (machining == null || sewing == null) return;
			// id, name, template, skill, time, fail, inputs, outputs, failOutputs, display item
			Make(920101, "Lock Pick Set", machining, 5400, 0.4f, Map(35070, 2f, 4, 5f), Map(ThiefPlugin.SetId, 1f), Map(35180, 1f), ThiefPlugin.SetId);
			Make(920102, "Master Lock Pick Set", machining, 9000, 0.5f, Map(35070, 3f, ThiefPlugin.SetId, 1f, 16320, 2f), Map(ThiefPlugin.MasterId, 1f), Map(35180, 2f), ThiefPlugin.MasterId);
			Make(920103, "Bolt Cutters", machining, 10800, 0.45f, Map(35070, 4f, 16320, 3f), Map(ThiefPlugin.CuttersId, 1f), Map(35180, 3f), ThiefPlugin.CuttersId);
			Make(920104, "Thief's Gloves", sewing, 3600, 0.3f, Map(12680, 6f, 12700, 1f), Map(ThiefPlugin.GlovesId, 1f), Map(12680, 3f), ThiefPlugin.GlovesId);
			Make(920105, "Soft-soled Shoes", sewing, 5400, 0.35f, Map(12700, 2f, 12680, 4f), Map(ThiefPlugin.ShoesId, 1f), Map(12680, 3f), ThiefPlugin.ShoesId);
			Make(920106, "Stealth Bag", sewing, 10800, 0.45f, Map(15480, 1f, 12700, 3f), Map(ThiefPlugin.BagId, 1f), Map(12680, 6f), ThiefPlugin.BagId);
			ThiefPlugin.Log.LogInfo("Thief: recipes added (Machining: sets, bolt cutters; Sewing: gloves, shoes, bag).");
		}

		private static void Make(int id, string name, Recipe tpl, int time, float fail, Dictionary<ItemReference, float> input, Dictionary<ItemReference, float> output, Dictionary<ItemReference, float> outFail, int showItem)
		{
			MethodInfo mc = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
			Recipe r = (Recipe)mc.Invoke(tpl, null);
			r.ID = id; r.Name = name; r.RecipeRequired = false;
			r.RecipeTags = new List<string>(); r.RequiredConditions = new List<RecipeCondition>(); r.ExcludingConditions = new List<RecipeCondition>();
			r.input = input; r.output = output; r.outputFail = outFail;
			r.ProcessTime = time; r.FailChance = fail; r.MinimumSkill = 0; r.HardMinimumSkill = false;
			Item show = Find(showItem);
			r.Appearance = new Recipe.RecipeAppearance { SpritePath = tpl.Appearance != null ? tpl.Appearance.SpritePath : null, CraftShowModel = false, itemToShow = show != null ? new ItemReference(show) : null };
			try { r.Appearance.LoadSprite(); } catch (Exception) { }
			r.SetItems();
			RecipeDatabase.Recipes.Add(r);
		}
	}
}

