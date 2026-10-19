# ShipPins

Pins every ship and cart on the map, modded ones included, such as OdinShip boats and LoxWagon wagons. A collapsible ShipPins Options menu provides category switches, and overlapping labels can be hidden. Works on its own, and with ValheimPlus too.

A Valheim mod by Onononoki. Get it on Thunderstore: https://thunderstore.io/c/valheim/p/Onononoki/ShipPins/

The full description, settings and changelog are in [package/README.md](package/README.md) and [package/CHANGELOG.md](package/CHANGELOG.md).

## Contributors

- [aasmall](https://github.com/aasmall): the client-side mode and two pin fixes in 1.1.0, first made in [aasmall's fork](https://github.com/aasmall/ShipPins).

## Bug reports and feedback

Bug reports and ideas: open an issue at https://github.com/Ononononoki/ShipPins/issues. If something breaks, please add your mod list and the BepInEx log (`BepInEx/LogOutput.log`).

## Building

Needs the .NET SDK, Valheim and a mod manager profile with BepInEx.

```
dotnet build -c Release -p:GamePath="<Valheim folder>" -p:ProfilePath="<mod manager profile folder>"
```

The dll is built into `bin/Release/net472` and copied to `package/`.

## License

MIT, see [LICENSE](LICENSE).
