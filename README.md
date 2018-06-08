# CBT Heat

CBT Heat brings Classic Battletech Tabletop heat rules feeling into HBS's BATTLETECH game.  In the Classic Battletech Tabletop game, heat management had a more press-your-luck style component to it.  This mod is an attempt to fit that style of mechanic into the heat system of this game.  Note that this is more of an attempt to blend the 2 systems together than a total reimplementation.

Firstly, a mech will no longer suffer damage from overheating.  This seemed a little unfair considering the rest of the changes and really didn't happen in CBT. To counter not being damaged, your mech will have a chance to shutdown and have its ammo explode every turn you are overheated.  The chances of that happening depend on the number of rounds you have been overheated.  I've tried to convert the original CBT heat chart in to percentages and apply them here.  The original CBT heat scale had 4 Shutdown roll chances and 4 Ammo Explosion chances as well as Heat modifiers to Hit.  So I've converted those chances (which were originally 2d6 rolls) and applied them to the overheat mechanic of the game. The game will roll randomly for each percentage.  Ammo Explosion results are applied first.

| Rounds Overheated | Shutdown Chance | Ammo Explosion Chance | ToHit Modifier |
|:-----------------:|:---------------:|:---------------------:|:--------------:|
| 1                 | 8.3%            | 0                     | +1             |
| 2                 | 27.8%           | 8.3%                  | +2             |
| 3                 | 58.3%           | 27.8%                 | +3             |
| 4+                | 83.3%           | 58.3%                 | +4             |

These chances are also displayed in the Overheat notification badge above the heat bar.  Bringing your heat bar all the way to shutdown still shuts the mech down.

All chances and modifiers are configurable in the mod.json file.

v0.1.1 adds the ability to add a Guts Modifier to the Ammo Explosion and Shutdown chance rolls.  This is disabled by default.  To enable it, change the "UseGuts" setting to true in mod.json.
The Guts modifier is currently calculated by essentially taking Guts / GutsDivisor.  The base GutsDivisor of 20 was too low, so I changed it to 40.  So a Guts of 5 would add 12.5% to the 2 rolls.

## Installation

Install [BTML](https://github.com/Mpstark/BattleTechModLoader) and [ModTek](https://github.com/Mpstark/ModTek). Extract files to `BATTLETECH\Mods\CBTHeat\`.
