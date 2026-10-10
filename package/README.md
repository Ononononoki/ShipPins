# ShipPins

**ValheimPlus pins only the four vanilla boats and carts on the map. ShipPins pins all of them.**

V+'s `displayCartsAndBoats` option pins the vanilla Cart, Raft, Karve and Longship. Ships and carts from mods such as OdinShip or LoxWagon get no pin. ShipPins runs the same routine for every ship and cart.

## What it does

- Pins every object built on the game's own ship or cart, vanilla and modded, with no list to maintain. It also pins the Ashlands Drakkar, Catapult and Battering Ram, which V+ misses.
- Uses V+'s own pin code, so the pins look and behave like V+'s. They are never saved to your map.
- Modded ships and carts are labeled with their in-game name. The vanilla four keep V+'s labels.

## Requirements and config

- ValheimPlus (Grantapher fork) with `[Map] displayCartsAndBoats = true`, and BepInExPack Valheim.
- Client side only. It is not needed on the server.
- The file `BepInEx/config/onononoki.ShipPins.cfg` has one setting, `Enabled` (default true). Off gives V+'s own four pins back.

## Compatibility

- Works with mods that build ships and carts on the game's own ship and cart, which is the usual way. Checked against OdinShip and LoxWagon.
- If a V+ update changes its pin code, ShipPins logs a warning and steps aside, and V+ keeps its own pins.
- Safe to add or remove at any time. Nothing is saved.

## Feedback and bug reports

Found a bug or have an idea? Open an issue on [GitHub](https://github.com/Ononononoki/ShipPins/issues). The source code is in the [GitHub repository](https://github.com/Ononononoki/ShipPins).
