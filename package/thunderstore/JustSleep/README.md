# JustSleep
Sleep in not owned beds. Sleep in front of the fire.

## Features
Allows you to sleep in unclaimed, not owned or your own bed without spawn point setting.

Allows you to sleep in front of the fire.

After you have "Resting" buff for set configurable amount of time (default 20 seconds like for Rested buff) you can interact with fireplace to get to sleep
* you should be sitting on the floor or furniture
* you should not be sensed by monsters
* you should not be wet
* it should not be a daytime

Uses the current in-game alternative action hotkey by default.

Optional client-side hotkey overrides are available for Sleep and Claim. Sleep override applies to beds and fireplace sleeping; Claim override applies to both claiming unclaimed beds and setting the spawn point on owned non-current beds. Custom shortcut hints are displayed with modifiers first and the main key last.

Bed actions keep the vanilla/legacy order: unclaimed beds show Claim then Sleep, owned non-current beds show Set Spawn then Sleep, while foreign beds and the current owned bed show Sleep only when that action is available.

## Installation (manual)
extract JustSleep.dll to your BepInEx\Plugins\ folder.

## Configurating
The best way to handle configs is [Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/).

Or [Official BepInEx Configuration Manager](https://valheim.thunderstore.io/package/Azumatt/Official_BepInEx_ConfigurationManager/).

## Dependencies

- [BepInExPack Valheim 5.4.2350](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- [ConditionalConfigSync 1.0.5](https://thunderstore.io/c/valheim/p/shudnal/ConditionalConfigSync/)

Install ConditionalConfigSync as a separate dependency; do not copy its DLLs into this mod's package.

## Donation
[Buy Me a Coffee](https://buymeacoffee.com/shudnal)

## Discord
[Join server](https://discord.gg/e3UtQB8GFK)
