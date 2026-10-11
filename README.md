# ShipPins

ValheimPlus add-on that pins every ship and cart on the map, modded ones included, such as OdinShip boats and LoxWagon wagons. V+ only pins the vanilla cart, raft, karve and longship.

Fork of [Onononoki/ShipPins](https://github.com/Ononononoki/ShipPins) (MIT). This fork is client-only: it does not need V+'s `[Map] displayCartsAndBoats` setting, which servers usually control. See [package/CHANGELOG.md](package/CHANGELOG.md).

The full description, settings and changelog are in [package/README.md](package/README.md) and [package/CHANGELOG.md](package/CHANGELOG.md).

## Bug reports and feedback

Bug reports and ideas for this fork: https://github.com/aasmall/ShipPins/issues. If something breaks, please add your mod list and the BepInEx log (`BepInEx/LogOutput.log`).

## Building

Needs the .NET SDK, Valheim and a mod manager profile with BepInEx (the mod itself runs with ValheimPlus).

```
dotnet build -c Release -p:GamePath="<Valheim folder>" -p:ProfilePath="<mod manager profile folder>"
```

The dll is built into `bin/Release/net472` and copied to `package/`.

## License

MIT, see [LICENSE](LICENSE).
