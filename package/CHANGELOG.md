# Changelog

## 1.1.0

- Client-only: pins show without V+'s [Map] enabled / displayCartsAndBoats, which servers usually control through V+'s config sync. ShipPins' own Enabled setting is the only switch.

## 1.0.3

- Requires ValheimPlus 10.1.2 and BepInExPack 5.4.2350 instead of 10.2.0 / 5.4.2351. The code ShipPins hooks is the same in 10.1.2, and mod managers no longer force a V+ upgrade that can break joining servers still on 10.1.2.
- Pins of destroyed ships no longer get stranded on the map when the game reuses their object record.
- Pins show up as soon as the map loads instead of after the first 5 seconds.

## 1.0.2

- Added links to the GitHub page for feedback and bug reports. Shorter description.

## 1.0.1

- Small build cleanup. No gameplay changes.

## 1.0.0

- First release.
- Runs V+'s boat and cart map pins for every ship and cart prefab, modded ones included, in place of its four vanilla ones.
- Same look as V+'s pins. Modded ones are labeled with their in-game name.
