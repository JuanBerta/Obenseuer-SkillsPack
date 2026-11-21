# Obenseuer Skills Pack

Skill mods for Obenseuer in one BepInEx 6 mod. It replaces the separate Scavenger, Haggling, Bribery+, Stealth and Scam Artist mods.

| Skill | What it adds |
|---|---|
| **Scavenging** (Scavenger) | Search trash to level up; higher levels find more, and better, loot. Gear and light matter. Level 10: Trash King. |
| **Haggling** | XP from trading; up to 12% better buy and sell prices. Level 10: Silver Tongue (traders buy stolen goods). |
| **Bribery+** | Look at a person and press B to ask for money, using the game's Bribery skill. |
| **Lockpick Learning** | The game's own Lockpicking skill grows faster from failed attempts (FailureShare) and when drunk or high (ImpairedBonus). |
| **Stealth** | XP for stealing, lockpicking and trespassing unseen; people and cameras notice you less. Level 10: Shadow. |
| **Fraud** (Scam Artist) | Scam invoices that play out over days, with odds on the invoice, a scam ledger (F7), follow-ups, investigations, and frozen accounts that unfreeze on their own after 7 days. |

## Coming from the separate mods

- **Delete the old DLLs** (`Obenseuer-Scavenger`, `-Haggling`, `-BriberyPlus`, `-Stealth`, `-ScamArtist` in `BepInEx\plugins`). The pack warns in the log if one is still there.
- **Saves carry over.** Skill levels, XP and the scam ledger are kept.
- **Settings carry over.** Each skill keeps its own config file (`JuanBerta.Obenseuer.<Skill>.cfg`).

## Turning skills off

`BepInEx\config\JuanBerta.Obenseuer.SkillsPack.cfg` has an on/off switch per skill (restart needed). A switched-off skill keeps its progress in your saves.

## Install

Extract into the Obenseuer folder (BepInEx 6 be.697 or newer). The DLL goes to `BepInEx\plugins\Obenseuer-SkillsPack\`.

## Works with

Loot Map (searched-trash markers) and Special Orders (Haggling bonus) find the skills inside the pack.

## Build

`dotnet build -c Release` (set `/p:ObenseuerDir=...` if the game isn't in the default Steam folder).
