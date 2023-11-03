# Inventory Sorter

Sort command: `/clownsort`

It's a lot like [Isy's Inventory Manager](https://steamcommunity.com/sharedfiles/filedetails/?id=1226261795) and uses a similar config language.

Broad category container names:

Ores
Ingots
Ammo
Components
Bottles
Tools

Special container tag:

Special

Broad categories and special containers can be combined: `Cargo Container Special, Bottles and Tools` is valid.

Priorities can be from 0 to 255 and are specified with an additional `[P#]` in the container name, and lower numbers are higher priority: `Cargo Container Ore P10` is filled before `Cargo Container Ore P20`.

Special containers use Custom Data to select items to fill the container with.

If you name a container with Special in the name, and the customdata is empty, it will populate the CustomData with whatever is in the container at the time and will attempt to maintain those numbers as tightly as possible every time you run the sort command, moving items out if there are too many, and taking items from lower priority containers if there are too few. Priority does affect special containers, and `broad` containers with a higher priority will steal from special containers to fill themselves.

An example of the custom data is:
```ini
[Inventory]
Component/GVK_Thrust_Tech=All
Component/GVK_SmallCaliber_Tech=All
Component/GVK_Laser_Tech=All
Component/GVK_LargeCaliber_Tech=All
Component/GVK_Missile_Tech=All
Component/GVK_Data_Core=All
Component/GVK_SpecialPackage1=All
Component/ZoneChip=All
Ingot/GVK_Relic_Tech=All
```

The word `All` after an item instead of a number means the container will attempt to take as many as possible until it is full. This also is how you make containers for single or a few item types, like Steel Plate.

There are a few special cases that are also worth mentioning for specific block types. To override these behaviors, add a Special tag or use one of the Locked keywords in the name: `Lock` (or something longer, like `Locked` works too), `Seat`, `Control Station`, `Hidden`, `!manual` are all things that that will make the sorter ignore a block.

* Ice is added to or removed from O2/H2 generators to maintain around 70-80% fullness, but bottles will always be moved from any tanks to a Bottles container if one exists.
* Bottles will always be moved from any tanks to a Bottles container if one exists.
* Refinery inputs are left alone, but outputs are fully emptied if they exceed 20% fullness.
* If an assembler is in disassemble mode, its input will be emptied, but if it's in assemble mode its output will be emptied. The opposite inventory will always be left alone.
* Reactor fuel will be filled or removed to make the reactor contain around `100 * Power Output Multiplier` for Large Grid and `25 * Power Output Multiplier` for Small Grid.
* Weapons will be fully filled with whatever ammo they want.
* Sorters will not be emptied if they're in Drain All mode.