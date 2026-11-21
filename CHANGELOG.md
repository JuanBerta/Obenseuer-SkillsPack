# Changelog

## 1.2.0
- New: XP Tiers. Recipe skill XP now scales with the value of the result compared with the other recipes of that skill (4 tiers, x1 / x1.5 / x2.5 / x4 by default, cutoffs and per-recipe overrides in its config), planting included. Mining, jobs, sauna, drinking and furniture blueprints give several times more XP (each has its own multiplier). Own on/off switch.

## 1.1.0
- New: Lockpick Learning. The game's Lockpicking skill grows faster from a lockpick that fails (FailureShare, 0.8 of a success instead of the game's 0.2), and faster again when you are drunk or high (ImpairedBonus, up to double). Own on/off switch like the other skills.

## 1.0.0
- First release: Scavenger 1.0.0, Haggling 1.0.0, Bribery+ 1.0.1, Stealth 1.0.1 and Scam Artist 1.0.6 in one DLL.
- New in Scam Artist: a frozen bank account unfreezes on its own after 7 in-game days (AutoUnfreezeDays, 0 = off). The ledger shows the countdown.
- On/off switch per skill in JuanBerta.Obenseuer.SkillsPack.cfg.
- Saves and each skill's settings carry over from the separate mods. Warns in the log if an old separate DLL is still installed.
