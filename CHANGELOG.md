# Changelog

## 1.5.2
- Sauna Assist: new Smart mode (on by default). It works out the löyly for you from the game's own overheat formula: the highest level that still lets you stay StayMinutes (default 30 game minutes) before your overheat reaches the guard level. It follows your Sauna skill, the stove's temperature and the overheat you already have, so it rises as your skill rises. SmartMax (default 6) caps it. Turn Smart off to use the fixed Target instead. Farm mode still uses its own target.

## 1.5.1
- Sauna Assist: the sauna box no longer sits on top of the game's notifications in the top-left corner. It now starts at the left edge, 30% down the screen, and you can move it with the new HUD Left and HUD Top settings (percent of the screen).
- Pickpocket and shoplift keys are Delete (the 1.5.0 notes said P and L).

## 1.5.0
- Changed default keys that clashed with the game or other mods: Bribery+ ask key B is now Backspace (B is the game's Head Use), Scam Artist ledger F7 is now Keypad 0 (F7 is Player Shop; the game itself uses F11 and F12 for screenshots). Existing settings files keep your old key until you change it.

- New: Thief module. Six new items (Lock Pick Set, Master Lock Pick Set, Bolt Cutters, Stealth Bag, Thief's Gloves, Soft-soled Shoes) with recipes at the Machining and Sewing stations, and the Crowbar now opens locks. Lockpicking is a percentage roll with no minigame: the lock shows your real chance, the tool works by itself and better tools raise the chance, work faster, break less (bolt cutters and the crowbar are quick but loud). Heat: crimes build heat, which makes shops charge more and fences pay less and falls each day. Fences: traders who buy stolen goods pay more as you sell them stolen items. Pickpocketing (P, look at a person) and shoplifting (L, with a shop open). Gloves, shoes and the bag are carried or worn for their effects. Every number is a setting and the module has its own on/off switch. The new items reuse existing art.
- Made with AI assistance (Claude).

## 1.4.1
- XP Tiers: crafting XP now depends on how the craft went. In the vanilla game the XP of a craft grows with its fail chance, so a drunk crafter who fails a lot earned more than a sober one who succeeds. Now a successful craft pays more (SuccessMultiplier, 2.5) and a failed one less (FailedMultiplier, 0.3), and the fail chance used for XP ignores the alcohol in your blood (IgnoreDrunkness). New settings OutcomeXP, IgnoreDrunkness, SuccessMultiplier and FailedMultiplier under Recipes; OutcomeXP off gives back the vanilla behaviour.
- Made with AI assistance (Claude).

## 1.4.0
- New: Sauna Assist. Auto-löyly throws water on the stones for you while you are in a hot sauna and keeps the löyly at a level you set. The overheat guard stops throwing and warns you before the heat starts to hurt (it scales with your Sauna skill), and can switch the stove off. A small HUD shows temperature, löyly and overheat. Farm mode aims for a much higher löyly, so Sauna skill grows as fast as is safe. Insert toggles auto-löyly, End toggles farm mode (both can be changed). It never touches the sauna contest.
- Made with AI assistance (Claude).

## 1.3.5
- Fix: Bribery+ did nothing on game versions that lack the CharacterHeadController.PlayerMadeNoise method (it threw an error every time you pressed the ask key). It now skips that step when the method is missing.
- Made with AI assistance (Claude).

## 1.3.4
- Fix: the Bribery+ ask key (B) and the Scam Artist ledger key did nothing on non-English keyboard layouts (for example Russian), because the old Unity key check follows the typed letter. Hotkeys now also read the physical key, so B works on any layout. The log now shows the ask key and the keyboard layout at start-up.
- Made with AI assistance (Claude).

## 1.3.3
- Fix: lowering Woodcutting's MaxLevel no longer cuts your stored level. MaxLevel now only caps the effects; the stored level is kept.
- Fix: Woodcutting progress now also has the never-decreasing backup the other skills have (JuanBerta.Obenseuer.SkillsPack.progress.txt), so a missing, older or reset woodcutting.json cannot lose it.
- Fix: the progress backup file is replaced in one step, so it cannot be left missing if the game closes while it is written.
- Made with AI assistance (Claude).

## 1.3.2
- Woodcutting now levels up by XP like the game's own skills: 1000 XP per level (XpPerLevel), 10 XP for each axe hit (XpPerHit) and 40 XP each time a tree or log gives wood (XpPerWood). The Character tab shows your total XP, and the row appears from your first chop, with a message.
- Woodcutting: drinking makes some hits clumsy misses (DrunkMissChance) that do little but give 3x XP (DrunkMissXpFactor), and weaken your chopping and bonus wood (DrunkOutputLoss). Mushrooms raise your effective Woodcutting level while you are high (MushroomsBoostSkill).
- Old Woodcutting settings XpPerLevelStep and XpPerChop are replaced; your Woodcutting level and XP from 1.3.1 carry over (the XP now counts toward the 1000 XP levels).
- Made with AI assistance (Claude).

## 1.3.1
- Woodcutting now shows in the Character tab, with its level and progress to the next level, like the game's own skills. The Home key and its ShowSkillKey setting are gone.
- Made with AI assistance (Claude).

## 1.3.0
- New: Woodcutting skill (moved in from the separate Woodcutting mod). Chopping trees and logs with an axe levels it up to 10 by default (MaxLevel is a setting): the axe hits harder and swings faster, and each tree or log gives extra wood. Each save slot keeps its own level (Saves/<character>/woodcutting.json); press Home to see it. Own on/off switch, and a warning in the log if the separate Woodcutting.dll is still installed.
- Made with AI assistance (Claude).

## 1.2.3
- Bribery+: success odds rise faster with level (ExtraSuccessPerLevel 3%/level on top of the game's 3%, about 90% for a stranger at level 10, never above 95%) and rewards grow faster toward level 10 (RewardPerLevelSquared: 13.1x the level-1 reward at level 10 instead of 4.6x)

## 1.2.2
- Bribery+: if the game does not report the person you are looking at, the mod now looks straight ahead from the camera to find them, and when the ask key finds nobody it writes the reason to the log ("the cursor is free", "too far", "nobody in front of you"...), so a key that does nothing can be diagnosed.

## 1.2.1
- Fix: skill progress could be lost after a reload (a small load between the building and the street, a death, a load of an older save). Every custom skill (Scavenger, Haggling, Bribery+, Stealth, Scam Artist) now keeps a second copy of its XP in memory and in BepInEx/config/JuanBerta.Obenseuer.SkillsPack.progress.txt, and a load that brings back no data or less XP than that copy keeps the higher value. Progress only goes up; delete a line of that file to reset a character by hand.
- Fix: on the stable game build (0.5.08) a missing game method no longer stops the rest of a skill's patches: the one that does not exist is skipped with a warning in the log. Scavenger's extra finds are skipped there if the game has no Storage.AddItemAndReturnRemaining.

## 1.2.0
- New: XP Tiers. Recipe skill XP now scales with the value of the result compared with the other recipes of that skill (4 tiers, x1 / x1.5 / x2.5 / x4 by default, cutoffs and per-recipe overrides in its config), planting included. Mining, jobs, sauna, drinking and furniture blueprints give several times more XP (each has its own multiplier). Own on/off switch.

## 1.1.0
- New: Lockpick Learning. The game's Lockpicking skill grows faster from a lockpick that fails (FailureShare, 0.8 of a success instead of the game's 0.2), and faster again when you are drunk or high (ImpairedBonus, up to double). Own on/off switch like the other skills.

## 1.0.0
- First release: Scavenger 1.0.0, Haggling 1.0.0, Bribery+ 1.0.1, Stealth 1.0.1 and Scam Artist 1.0.6 in one DLL.
- New in Scam Artist: a frozen bank account unfreezes on its own after 7 in-game days (AutoUnfreezeDays, 0 = off). The ledger shows the countdown.
- On/off switch per skill in JuanBerta.Obenseuer.SkillsPack.cfg.
- Saves and each skill's settings carry over from the separate mods. Warns in the log if an old separate DLL is still installed.
