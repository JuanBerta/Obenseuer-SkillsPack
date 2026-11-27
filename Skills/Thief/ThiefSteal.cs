using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace ThiefMod
{
	/// <summary>Pickpocketing (look at a person) and shoplifting (a trader's window is open).</summary>
	internal static class Steal
	{
		private static int Today
		{
			get { try { return TimeOfDayAzure.instance.currentTimeAndDay.currentDay; } catch (Exception) { return 0; } }
		}

		private static int Level(PlayerStats.PlayerSkillType t)
		{
			try { return Mathf.Max(0, PlayerStats.instance.GetSkill(t)); } catch (Exception) { return 0; }
		}

		private static float Drunk()
		{
			try { return Mathf.Clamp(PlayerStats.instance.alcohol.AlcoholInBlood, 0f, 100f) / 100f; } catch (Exception) { return 0f; }
		}

		private static void Noise()
		{
			try { var nm = AccessTools.Method(typeof(CharacterHeadController), "PlayerMadeNoise"); if (nm != null) nm.Invoke(null, null); } catch (Exception) { }
		}

		private static void Learn(float amount)
		{
			try { PlayerStats.instance.IncreaseSkill(PlayerStats.PlayerSkillType.Lockpicking, amount); } catch (Exception) { }
		}

		// ---- Pickpocketing ----

		private struct Target { public NPCInfo npc; public int id; public string name; public bool asleep; public string blocked; }

		private static Target? Current()
		{
			if (!ThiefPlugin.Pickpocket.Value || !SkillHost.InGame || PlayerStats.instance == null) return null;
			if (Time.timeScale <= 0f || Cursor.lockState != CursorLockMode.Locked) return null;
			if (TradePanel.instance != null && TradePanel.instance.activeTrade != null) return null;
			GameObject go = InteractObjects.instance != null ? InteractObjects.instance.currentGameObject : null;
			NPCInfo npc = go != null ? go.GetComponentInParent<NPCInfo>() : null;
			if (npc == null)
			{
				Camera cam = Camera.main;
				RaycastHit hit;
				if (cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, ThiefPlugin.PpDistance.Value + 1f, ~0, QueryTriggerInteraction.Ignore) && hit.collider != null)
					npc = hit.collider.GetComponentInParent<NPCInfo>();
			}
			if (npc == null || npc.character == null || !npc.isActiveAndEnabled) return null;
			if (PlayerLocator.instance != null && Vector3.Distance(PlayerLocator.instance.transform.position, npc.transform.position) > ThiefPlugin.PpDistance.Value) return null;
			var t = new Target { npc = npc, id = npc.character.ID };
			try { t.name = string.IsNullOrEmpty(npc.character.FullName) ? npc.character.Name : npc.character.FullName; } catch (Exception) { t.name = "They"; }
			if (string.IsNullOrEmpty(t.name)) t.name = "They";
			try { t.asleep = npc.Sleeping(); } catch (Exception) { }
			if (ThiefPlugin.Save != null && ThiefPlugin.Save.GetDaily("pp@" + t.id) > 0f) t.blocked = "already tried today";
			return t;
		}

		private static float PickpocketChance(Target t)
		{
			float p = ThiefPlugin.PpBase.Value + 0.03f * Level(PlayerStats.PlayerSkillType.Lockpicking);
			if (t.asleep) p += ThiefPlugin.PpSleeping.Value;
			if (Gear.Gloves) p += ThiefPlugin.PpGloves.Value;
			if (Gear.Shoes) p += ThiefPlugin.PpShoes.Value;
			p -= 0.3f * Drunk();
			return Mathf.Clamp(p, 0.03f, 0.95f);
		}

		private static void Pickpocket(Target t)
		{
			if (t.blocked != null) { SkillHost.Notify("Pickpocket", t.name + ": " + t.blocked + "."); return; }
			ThiefPlugin.Save.SetDaily("pp@" + t.id, 1f);
			float p = PickpocketChance(t);
			if (UnityEngine.Random.value < p)
			{
				float oc = UnityEngine.Random.Range(ThiefPlugin.PpMinOc.Value, Mathf.Max(ThiefPlugin.PpMinOc.Value, ThiefPlugin.PpMaxOc.Value));
				oc = Mathf.Max(1f, Mathf.Round(oc * (1f + 0.1f * Level(PlayerStats.PlayerSkillType.Lockpicking))));
				Money.instance.AddMoney(oc, Money.Currency.OC);
				Heat.Add(ThiefPlugin.PpHeat.Value * (Gear.Gloves ? 1f - ThiefPlugin.GlovesHeatCut.Value : 1f));
				Learn(0.03f);
				SkillHost.Notify("Pickpocket", "You lift " + oc.ToString("0", CultureInfo.InvariantCulture) + " OC from " + t.name + ".");
				return;
			}
			Learn(0.01f);
			if (t.asleep || UnityEngine.Random.value < 0.5f)
			{
				SkillHost.Notify("Pickpocket", "Nothing worth taking, or you lost your nerve. " + t.name + " noticed nothing.");
				return;
			}
			Noise();
			try { Crime.instance.CommitCrime(new NPC.NPCReference(t.id), ThiefPlugin.PpSeverity.Value, Crime.CrimeType.Theft); } catch (Exception) { }
			SkillHost.Notify("Pickpocket", t.name + " catches your hand!");
		}

		// ---- Shoplifting ----

		private static Trade Shop { get { return TradePanel.instance != null ? TradePanel.instance.activeTrade : null; } }

		private static void Shoplift()
		{
			Trade trade = Shop;
			if (trade == null || ThiefPlugin.Save == null) return;
			int shopId = trade.owner != null ? trade.owner.ID : 0;
			Storage storage = Traverse.Create(trade).Field("storage").GetValue<Storage>();
			if (storage == null || storage.storageSlots == null) { SkillHost.Notify("Shoplifting", "There is nothing within reach."); return; }
			var picks = new List<ItemStack>();
			foreach (ItemStack s in storage.storageSlots)
			{
				if (s == null || s.itemId < 0 || s.itemAmount <= 0 || s.ownerId > 0) continue;
				Item it = ItemDatabase.instance.FetchItemByID(s.itemId);
				if (it != null && it.BaseValue <= ThiefPlugin.SlMaxValue.Value) picks.Add(s);
			}
			if (picks.Count == 0) { SkillHost.Notify("Shoplifting", "Nothing here is small enough to palm."); return; }
			float hit = ThiefPlugin.Save.GetDaily("sl@" + shopId);
			ItemStack stack = picks[UnityEngine.Random.Range(0, picks.Count)];
			Item item = ItemDatabase.instance.FetchItemByID(stack.itemId);
			float p = ThiefPlugin.SlBase.Value + 0.02f * Level(PlayerStats.PlayerSkillType.Lockpicking);
			if (Gear.Gloves) p += ThiefPlugin.SlGloves.Value;
			if (Gear.Shoes) p += ThiefPlugin.SlShoes.Value;
			if (Gear.BagWorn) p += ThiefPlugin.SlBag.Value;
			p -= 0.3f * Mathf.Clamp01(item.BaseValue / Mathf.Max(1f, ThiefPlugin.SlMaxValue.Value));
			p -= 0.15f * hit + 0.3f * Drunk();
			p = Mathf.Clamp(p, 0.03f, 0.9f);
			ThiefPlugin.Save.AddDaily("sl@" + shopId, 1f);
			if (UnityEngine.Random.value < p)
			{
				object[] meta = stack.Meta;
				ItemOperations.AddItem(item, shopId, null, meta, true, true);   // owner = the shop, so it counts as stolen
				stack.RemoveItemAmount(1);
				Heat.Add(ThiefPlugin.SlHeat.Value * (Gear.Gloves ? 1f - ThiefPlugin.GlovesHeatCut.Value : 1f));
				Learn(0.03f);
				SkillHost.Notify("Shoplifting", "You palm a " + item.Title + ". Close and reopen the shop to see the shelf update.");
				return;
			}
			Learn(0.01f);
			Noise();
			if (UnityEngine.Random.value < 0.6f)
			{
				try { Crime.instance.CommitCrime(new NPC.NPCReference(shopId), ThiefPlugin.SlSeverity.Value, Crime.CrimeType.Theft); } catch (Exception) { }
				SkillHost.Notify("Shoplifting", "The shopkeeper saw you reach for the " + item.Title + "!");
			}
			else SkillHost.Notify("Shoplifting", "You could not get the " + item.Title + " out unseen.");
		}

		// ---- Input and hint ----

		internal static void Tick()
		{
			if (!SkillHost.InGame || ThiefPlugin.Save == null) return;
			if (ThiefPlugin.Shoplift.Value && Shop != null && SkillsPackMod.KeyUtil.IsDown(ThiefPlugin.ShopliftKey.Value)) { Shoplift(); return; }
			if (!SkillsPackMod.KeyUtil.IsDown(ThiefPlugin.PickpocketKey.Value)) return;
			Target? t = Current();
			if (t != null) Pickpocket(t.Value);
		}

		private static GUIStyle style;
		private static Target? shown;
		private static float nextLook;

		internal static void DrawHint()
		{
			if (Event.current.type != EventType.Repaint || !SkillHost.InGame) return;
			string text = null;
			if (ThiefPlugin.Shoplift.Value && Shop != null)
				text = "[" + ThiefPlugin.ShopliftKey.Value + "] Palm something from the shelf";
			else
			{
				if (Time.unscaledTime >= nextLook) { nextLook = Time.unscaledTime + 0.25f; try { shown = Current(); } catch (Exception) { shown = null; } }
				if (shown != null && shown.Value.npc != null)
					text = shown.Value.blocked != null ? shown.Value.name + " (" + shown.Value.blocked + ")" : "[" + ThiefPlugin.PickpocketKey.Value + "] Pickpocket " + shown.Value.name + " (" + Mathf.RoundToInt(PickpocketChance(shown.Value) * 100f) + "%)";
			}
			if (text == null) return;
			if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold };
			Rect r = new Rect(0f, Screen.height * 0.5f + 90f, Screen.width, 28f);
			Color old = GUI.color;
			GUI.color = new Color(0f, 0f, 0f, 0.8f);
			GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
			GUI.color = new Color(1f, 0.85f, 0.6f);
			GUI.Label(r, text, style);
			GUI.color = old;
		}
	}
}
