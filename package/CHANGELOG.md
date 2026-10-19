# Changelog

## 1.2.0

- Added a collapsible ShipPins Options menu on the large map using the game's map UI style.
- Added separate switches for vanilla ships, modded ships, vanilla carts, modded carts, and siege carts. Category choices are saved when the menu is closed.
- The menu starts collapsed whenever the map opens. ShowMapToggles can hide it while category settings remain active.
- Overlapping ship and cart labels are hidden while their icons remain visible. Zoom in to separate them, or disable HideOverlappingLabels to show every label.

## 1.1.2

- Much lighter. The pins now follow only the ships and carts already found, and the search for new ones runs slowly in the background. A ship or cart that loads near you is pinned at once, one built far away by someone else within about 30 seconds.
- The pins of destroyed ships are now always cleared from ShipPins' own list when the game reuses their record.

## 1.1.1

- Lighter on big worlds. The search for ships and carts is spread over several frames, so it no longer causes a short stutter every few seconds.

## 1.1.0

- Works on its own now, ValheimPlus is no longer needed. With V+ installed, ShipPins replaces V+'s own boat and cart pins, so each ship has one pin.
- Client-side only: the pins show whatever the server's V+ map setting says.
- Pins of destroyed ships no longer stay stranded on the map when the game reuses their object record.
- Pins show up as soon as the map loads.
- New setting `UpdateSeconds` (default 2): how often the pins follow the ships and carts.
- Pins are 1.5 times the normal pin size, with the name below the icon instead of over it. New setting `IconSize` (1 to 2, V+'s pins are 2).
- Thanks to [aasmall](https://github.com/aasmall) for the client-side mode and both pin fixes, first made in [aasmall's fork](https://github.com/aasmall/ShipPins).

## 1.0.2

- Added links to the GitHub page for feedback and bug reports. Shorter description.

## 1.0.1

- Small build cleanup. No gameplay changes.

## 1.0.0

- First release.
- Runs V+'s boat and cart map pins for every ship and cart prefab, modded ones included, in place of its four vanilla ones.
- Same look as V+'s pins. Modded ones are labeled with their in-game name.
