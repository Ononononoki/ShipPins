# ShipPins

**ValheimPlus shows your boats and carts on the map, but only the four vanilla ones. ShipPins shows all of them.**

V+'s `displayCartsAndBoats` option pins the vanilla Cart, Raft, Karve and Longship. Ships and carts added by other mods, such as the OdinShip boats or the LoxWagon wagons, never get a pin. ShipPins takes over that V+ routine and runs it for every ship and cart in the game.

## What it does

- Pins every object built on the game's own ship or cart, vanilla and modded. There is no list to maintain, so new ship and cart mods are picked up automatically.
- Uses V+'s own pin code, so the pins look and behave exactly like V+'s. Each pin is the ship's build icon at double size, it updates every 5 seconds, it hides for the ship you are steering, and it is never saved to your map.
- Also pins vanilla objects V+ misses: the Ashlands Drakkar, and the Catapult and Battering Ram, which are built on the game's cart.
- Modded ships and carts are labeled with their in-game name. The vanilla four keep V+'s labels.
- Client side only. It is not needed on the server.

## Requirements

- ValheimPlus (Grantapher fork) with `[Map] displayCartsAndBoats = true` in its config.
- BepInExPack Valheim.

## Config

The file is `BepInEx/config/onononoki.ShipPins.cfg`.

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | true | Pins every ship and cart. Off gives V+'s own four pins back. |

## Compatibility

- Works with mods that build their ships and carts on the game's own ship and cart, which is the usual way. Checked against the code of OdinShip and LoxWagon.
- If a V+ update changes its pin code, ShipPins logs a warning and steps aside, and V+ keeps showing its own pins.
- Safe to add or remove at any time. Nothing is saved to the world or character.

## How it works

A Harmony prefix on V+'s `Minimap.UpdateMap` postfix runs the same steps through V+'s own `CheckPin`, icon list and pin list, for every prefab that has a `Ship` or `Vagon` component.
