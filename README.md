# Obenseuer Skills Pack

Skill mods for Obenseuer in one BepInEx 6 mod. It replaces the separate Scavenger, Haggling, Bribery+, Stealth and Scam Artist mods, and includes Woodcutting.

| Skill | What it adds |
|---|---|
| **Scavenging** (Scavenger) | Search trash to level up; higher levels find more, and better, loot. Gear and light matter. Level 10: Trash King. |
| **Haggling** | XP from trading; up to 12% better buy and sell prices. Level 10: Silver Tongue (traders buy stolen goods). |
| **Bribery+** | Look at a person and press Backspace to ask for money, using the game's Bribery skill. |
| **Lockpick Learning** | The game's own Lockpicking skill grows faster from failed attempts (FailureShare) and when drunk or high (ImpairedBonus). |
| **Thief** | New stealing items and features: lock pick sets, bolt cutters, stealth bag, gloves and soft-soled shoes; lockpicking by percentage (no minigame); heat; fences; pickpocketing and shoplifting (Delete key). |
| **Stealth** | XP for stealing, lockpicking and trespassing unseen; people and cameras notice you less. Level 10: Shadow. |
| **Fraud** (Scam Artist) | Scam invoices that play out over days, with odds on the invoice, a scam ledger (F11), follow-ups, investigations, and frozen accounts that unfreeze on their own after 7 days. |
| **Woodcutting** | Chop trees and logs with an axe to level up (10 by default, a setting): faster, harder swings and extra wood per tree. Levels by XP (1000 per level, like the game's skills); drinking makes some hits clumsy misses that give more XP, mushrooms boost your level. Each save slot has its own level, shown in the Character tab. |
| **XP Tiers** | Recipe skill XP scales with how valuable the result is (4 tiers, planting included); mining, jobs, sauna, drinking and furniture blueprints give several times more XP. Every number is a setting. |
| **Sauna Assist** | Throws water on the stones for you (auto-löyly) and keeps the löyly at a level you set. An overheat guard stops before the heat hurts you (it scales with your Sauna skill) and can switch the stove off. A small HUD shows temperature, löyly and overheat. Farm mode aims for a higher löyly so Sauna skill grows as fast as is safe. Insert toggles auto-löyly, End farm mode. |

## Coming from the separate mods

- **Delete the old DLLs** (`Obenseuer-Scavenger`, `-Haggling`, `-BriberyPlus`, `-Stealth`, `-ScamArtist` in `BepInEx\plugins`, and `Woodcutting.dll` if you installed that mod). The pack warns in the log if one is still there.
- **Saves carry over.** Skill levels, XP and the scam ledger are kept.
- **Settings carry over.** Each skill keeps its own config file (`JuanBerta.Obenseuer.<Skill>.cfg`).

## Turning skills off

`BepInEx\config\JuanBerta.Obenseuer.SkillsPack.cfg` has an on/off switch per skill (restart needed). A switched-off skill keeps its progress in your saves.

## Install

Extract into the Obenseuer folder (BepInEx 6 be.697 or newer). The DLL goes to `BepInEx\plugins\Obenseuer-SkillsPack\`.

## Works with

Loot Map (searched-trash markers) and Special Orders (Haggling bonus) find the skills inside the pack.

## Made with AI

This pack was written with the assistance of AI (Claude by Anthropic) under my direction.

## Build

`dotnet build -c Release` (set `/p:ObenseuerDir=...` if the game isn't in the default Steam folder).
