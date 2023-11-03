# Inventory Sorter

Sort command: `/clownsort`

It's a lot like [Isy's Inventory Manager](https://steamcommunity.com/sharedfiles/filedetails/?id=1226261795) and uses a similar config language.

## Usage

Type container category names:

* `Ores`
* `Ingots`
* `Ammo`
* `Components`
* `Bottles`
* `Tools`

Container tags that use CustomData to select items are:

* `Special`
* `Limited`

Special is an **exclusive** tag - combining Special with another tag such as Tools will not work and the container will be treated as Special. Special containers use unique priority handling:
* Special containers are considered highest priority above all other blocks except other Special containers.
* If the config option `AllowSpecialSteal` is `true` (the default), Special containers will pull items from other special containers if necessary, depending on their relative priority.
* If the config option `AllowSpecialSteal` is `false`, Special containers will only remove excess items and pull in needed items from other non-Special containers.

Limited is an **inclusive** tag: Type container categories and Limited tagging can be combined. `Cargo Container Limited, Bottles and Tools` is valid and will try to contain all bottles, all tools, and any items specified in the CustomData. Priority affects limited containers normally and both limited and type containers participate in the same priority system.

Priorities can be from 0 to 255 and are specified with an additional `[P#]` in the container name, and lower numbers are higher priority: `Cargo Container Ore [P10]` is filled before `Cargo Container Ore [P20]`. Not specifying a priority generally means that a container has the lowest possible priority. Unprioritized functional blocks such as O2/H2 Generators will adjust their own default priority to the highest possible priority to maintain fullness if possible, but any specified priority will override that with the specified value.

Special and Limited containers use Custom Data to select items to fill the container with.

If you name a container with Special or Limited in the name, and the CustomData is empty, it will populate the CustomData with whatever is in the container at the time and will attempt to maintain those numbers as tightly as possible every time you run the sort command, moving items out if there are too many, and taking items from lower priority containers if there are too few.

An example of the custom data is:
```ini
[Inventory]
Component/GVK_Thrust_Tech=All
Component/GVK_SmallCaliber_Tech=All
Component/GVK_Laser_Tech=All
Component/GVK_LargeCaliber_Tech=All
Component/GVK_Missile_Tech=All
Component/GVK_Data_Core=All
Component/GVK_SpecialPackage1=100M
Component/ZoneChip=100
Ingot/GVK_Relic_Tech=500L
```

Special containers can have four different modes on a per-item-basis:

* Normal: pulls in and stores the specified amount, removes any excess. Usage: `item=100`
* Minimum: pulls in and stores the specified amount, ignores excess. Usage: `item=100M`
* Limit: Only removes items in excess of the specified value. Any value below the limit is kept, but more is not pulled in. Usage: `item=100L`
* All: Pulls in as many of the specified item as it can until the inventory is full. Usage: `item=All`

There are a few special cases that are also worth mentioning for specific block types. To override these behaviors, add a Special tag or use one of the Locked keywords in the name: `Lock` (or something longer, like `Locked` works too), `Seat`, `Control Station`, `Hidden`, `!manual` are all things that that will make the sorter ignore a block.

* Ice is added to or removed from O2/H2 generators to maintain around 70-80% fullness, but bottles will always be moved from any tanks to a Bottles container if one exists.
* Bottles will always be moved from any tanks to a Bottles container if one exists.
* Refinery inputs are left alone, but outputs are fully emptied if they exceed 20% fullness.
* If an assembler is in disassemble mode, its input will be emptied, but if it's in assemble mode its output will be emptied. The opposite inventory will always be left alone.
* Reactor fuel will be filled or removed to make the reactor contain around `100 * Power Output Multiplier` for Large Grid and `25 * Power Output Multiplier` for Small Grid.
* Weapons will be fully filled with whatever ammo they want.
* Sorters will not be emptied if they're in Drain All mode.

## Configuration

A configuration file is generated the first time the mod is used in `%appdata%\Storage\####.sbm_CargoSorter\CargoSort.xml` where #### is the steam mod ID.

The default looks like this:
```xml
<?xml version="1.0" encoding="utf-16"?>
<CargoSorterConfiguration xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <SpecialContainerKeyword>Special</SpecialContainerKeyword>
  <LimitedContainerKeyword>Limited</LimitedContainerKeyword>
  <OreContainerKeyword>Ores</OreContainerKeyword>
  <IngotContainerKeyword>Ingots</IngotContainerKeyword>
  <ComponentContainerKeyword>Components</ComponentContainerKeyword>
  <ToolContainerKeyword>Tools</ToolContainerKeyword>
  <AmmoContainerKeyword>Ammo</AmmoContainerKeyword>
  <BottleContainerKeyword>Bottles</BottleContainerKeyword>
  <LockedContainerKeywords>
    <string>Lock</string>
    <string>Seat</string>
    <string>Control Station</string>
    <string>Hidden</string>
    <string>!manual</string>
  </LockedContainerKeywords>
  <EmptyRefineryPercent>0.5</EmptyRefineryPercent>
  <EmptyAssemblerPercent>0.5</EmptyAssemblerPercent>
  <GasGeneratorFillPercent>0.8</GasGeneratorFillPercent>
  <ExpectedLargeGridReactorFuel>100</ExpectedLargeGridReactorFuel>
  <ExpectedSmallGridReactorFuel>25</ExpectedSmallGridReactorFuel>
  <AllowSpecialSteal>true</AllowSpecialSteal>
</CargoSorterConfiguration>
```

All keywords can be changed (e.g. `Ammo` to `Munitions`) as well as a few different other options. If the configuration is not valid, it will be reset to the default on next load, and will only take effect via exiting to main menu and reloading/reconnecting.