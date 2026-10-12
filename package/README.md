# ShipPins

**Pins every ship and cart on the map, modded ones included.**

Ships and carts from mods such as OdinShip or LoxWagon get a map pin like the vanilla ones. Works on its own, and with ValheimPlus too.

## What it does

- Pins every object built on the game's own ship or cart, vanilla and modded, with no list to maintain: Raft, Karve, Longship, Drakkar, Cart, Catapult, Battering Ram, OdinShip boats, LoxWagon wagons.
- Each pin shows the build icon and the in-game name, and follows its ship or cart every 2 seconds. The ship you steer has no pin, your own marker is there.
- Pins are never saved to your map.

## With ValheimPlus

- V+'s `displayCartsAndBoats` option pins only the vanilla Cart, Raft, Karve and Longship. With V+ installed, ShipPins switches that off and pins everything itself, so each ship has exactly one pin. The vanilla four keep V+'s labels.
- It works whatever the server's V+ map setting says.
- If a V+ update changes its pin code, ShipPins leaves V+'s four vanilla pins to V+, pins the rest and logs a warning.

## Requirements and config

- BepInExPack Valheim. ValheimPlus is optional.
- Client-side only. It is not needed on the server.
- It shows ships and carts your game has seen this session. One that moves while you are far away keeps its last known spot until you come near it again.
- The file `BepInEx/config/onononoki.ShipPins.cfg` has three settings: `Enabled` (default true, off gives V+'s own pins back), `UpdateSeconds` (default 2, how often the pins follow the ships and carts) and `IconSize` (default 1.5, 1 is the game's normal pin size, 2 is V+'s).

## Compatibility

- Works with mods that build ships and carts on the game's own ship and cart, which is the usual way. Checked against OdinShip and LoxWagon.
- Safe to add or remove at any time. Nothing is saved.

## Thanks

- [aasmall](https://github.com/aasmall) for the client-side mode and two pin fixes, first made in [aasmall's fork](https://github.com/aasmall/ShipPins).

## Feedback and bug reports

Found a bug or have an idea? Open an issue on [GitHub](https://github.com/Ononononoki/ShipPins/issues). The source code is in the [GitHub repository](https://github.com/Ononononoki/ShipPins).
