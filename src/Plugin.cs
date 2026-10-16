using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ShipPins
{
    // pins every ship and cart on the map (every prefab with a Ship or Vagon component) with its build icon and name.
    // works on its own. with valheim plus installed it switches v+'s own boat and cart pins off, so each ship has one
    // pin, and if that part of v+ cannot be found it leaves v+'s four vanilla pins to v+. client only
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency(VPlusGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "onononoki.ShipPins";
        public const string ModName = "ShipPins";
        public const string ModVersion = "1.1.1";
        private const string VPlusGuid = "org.bepinex.plugins.valheim_plus";
        private const string VPlusPins = "ValheimPlus.GameClasses.displayCartsAndBoatsOnMap";
        private const string VPlusConfig = "ValheimPlus.Configurations.Configuration";

        internal static ManualLogSource Log;
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _updateSeconds;
        private static ConfigEntry<float> _iconSize;

        private static readonly AccessTools.FieldRef<ZDOMan, List<ZDO>[]> ObjectsBySector =
            AccessTools.FieldRefAccess<ZDOMan, List<ZDO>[]>("m_objectsBySector");

        // the four prefabs v+ pins itself, with v+'s labels so the pins look the same with or without it
        private static readonly Dictionary<string, string> VanillaLabels = new Dictionary<string, string>
        {
            { "Cart", "Cart" }, { "Raft", "Raft" }, { "Karve", "Karve" }, { "VikingShip", "Longship" }
        };

        // what a ship or cart prefab is pinned with
        private sealed class Kind
        {
            internal string Label;
            internal Sprite Icon;
            internal bool Vanilla; // one of the four v+ pins itself
        }

        // prefab hash to its kind, null when the prefab is not a ship or cart (cached once the scene is up)
        private static readonly Dictionary<int, Kind> Kinds = new Dictionary<int, Kind>();
        private static readonly Dictionary<ZDO, Minimap.PinData> Pins = new Dictionary<ZDO, Minimap.PinData>();
        private static Minimap _map;
        private static float _timer = float.MaxValue; // the first update runs at once, so pins show as the map loads
        private static bool _failed;

        // a pass over the world's objects is spread over frames: each frame looks at this many sector slots and objects
        // at most, so a big world never stalls the game every few seconds. -1 is no pass running
        private const int ScanPerFrame = 20000;
        private static int _scanAt = -1;

        // valheim plus, found by name so a v+ update can only switch this part off, never break v+
        private static bool _vplus;          // v+ is installed
        private static bool _vplusSilenced;  // v+'s own boat and cart pin update is switched off by this mod
        private static PropertyInfo _configCurrent, _configMap, _mapEnabled, _mapDisplay;

        private void Awake()
        {
            Log = Logger;
            _enabled = Config.Bind("General", "Enabled", true,
                "Pin every ship and cart on the map, modded ones included. With ValheimPlus installed, its own four boat and cart pins are replaced, so each ship has one pin. Off: no pins from this mod (V+ shows its own again).");
            _updateSeconds = Config.Bind("General", "UpdateSeconds", 2f,
                new ConfigDescription("How often the pins move to where the ships and carts are, in seconds (V+ uses 5).", new AcceptableValueRange<float>(0.5f, 10f)));
            _iconSize = Config.Bind("General", "IconSize", 1.5f,
                new ConfigDescription("Size of the ship and cart pins. 1 is the game's normal pin size, 2 is V+'s.", new AcceptableValueRange<float>(1f, 2f)));

            _vplus = Chainloader.PluginInfos.ContainsKey(VPlusGuid);
            var harmony = new Harmony(ModGuid);
            harmony.Patch(AccessTools.Method(typeof(Minimap), "UpdateMap"), postfix: new HarmonyMethod(typeof(Plugin), nameof(UpdatePins)));
            harmony.Patch(AccessTools.Method(typeof(Minimap), "UpdatePins"), postfix: new HarmonyMethod(typeof(Plugin), nameof(FitPins)));
            if (_vplus) SilenceVPlus(harmony);

            if (!_vplus) Logger.LogInfo($"{ModName} {ModVersion} loaded, pinning every ship and cart");
            else if (_vplusSilenced) Logger.LogInfo($"{ModName} {ModVersion} loaded, pinning every ship and cart in place of V+'s boat and cart pins");
            else Logger.LogWarning($"{ModName} {ModVersion} loaded, V+'s boat pin code not found (V+ update?): V+ keeps its four vanilla pins whenever its map setting shows them, ShipPins pins the rest");
        }

        // v+'s boat and cart pin update (a postfix on the map update), and its map settings
        internal static MethodInfo ResolveVPlus()
        {
            var config = AccessTools.TypeByName(VPlusConfig);
            _configCurrent = config == null ? null : AccessTools.Property(config, "Current");
            _configMap = _configCurrent == null ? null : AccessTools.Property(_configCurrent.PropertyType, "Map");
            _mapEnabled = _configMap == null ? null : AccessTools.Property(_configMap.PropertyType, "IsEnabled");
            _mapDisplay = _configMap == null ? null : AccessTools.Property(_configMap.PropertyType, "displayCartsAndBoats");

            var pins = AccessTools.TypeByName(VPlusPins);
            var update = pins == null ? null : AccessTools.Inner(pins, "Minimap_UpdateMap_Patch");
            return update == null ? null : AccessTools.Method(update, "Postfix");
        }

        // a prefix that skips v+'s pin update while this mod pins, so v+ never adds pins of its own
        private static void SilenceVPlus(Harmony harmony)
        {
            try
            {
                var postfix = ResolveVPlus();
                if (postfix == null) return;
                harmony.Patch(postfix, prefix: new HarmonyMethod(typeof(Plugin), nameof(SkipVPlusPins)));
                _vplusSilenced = true;
            }
            catch (Exception ex)
            {
                Log.LogWarning($"V+'s boat pin code changed shape: {ex.Message}");
            }
        }

        // false skips v+'s pin update, so v+ pins again only when this mod is off or stopped
        private static bool SkipVPlusPins() => _failed || !_enabled.Value;

        // v+ draws its own pins of the four vanilla prefabs: it is installed, not switched off, and its map settings show
        // them. unknown settings count as shown, so there are never two pins on one ship
        internal static bool VPlusDrawsPins()
        {
            if (!_vplus || _vplusSilenced) return false;
            try
            {
                if (_configCurrent == null || _configMap == null || _mapEnabled == null || _mapDisplay == null) return true;
                var map = _configMap.GetValue(_configCurrent.GetValue(null, null), null);
                return map == null || (bool)_mapEnabled.GetValue(map, null) && (bool)_mapDisplay.GetValue(map, null);
            }
            catch
            {
                return true;
            }
        }

        // after the game's own map update: every few seconds each ship and cart gets its pin where it is
        private static void UpdatePins(Minimap __instance, Player player, float dt)
        {
            if (_failed) return;
            try
            {
                if (__instance != _map)
                {
                    // a new map (another world, a relog): the old pins went with the old one
                    Pins.Clear();
                    _map = __instance;
                    _timer = float.MaxValue;
                    _scanAt = -1;
                }
                if (!_enabled.Value)
                {
                    Clear(__instance);
                    _scanAt = -1;
                    return;
                }
                _timer += dt;
                if (_scanAt < 0)
                {
                    if (_timer < _updateSeconds.Value) return;
                    _timer = 0f;
                    _scanAt = 0;
                }
                if (!player || ZDOMan.instance == null || !ZNetScene.instance) return;

                // this frame's part of the pass: each ship and cart's pin moves when its sector comes up
                bool skipVanilla = VPlusDrawsPins();
                var steered = player.GetControlledShip();
                var sectors = ObjectsBySector(ZDOMan.instance);
                int budget = ScanPerFrame;
                while (_scanAt < sectors.Length && budget > 0)
                {
                    var sector = sectors[_scanAt++];
                    budget--;
                    if (sector == null) continue;
                    budget -= sector.Count;
                    foreach (var zdo in sector)
                    {
                        var kind = KindOf(zdo.GetPrefab());
                        if (kind == null || kind.Vanilla && skipVanilla) continue;
                        Pin(__instance, steered, zdo, kind);
                    }
                }
                if (_scanAt < sectors.Length) return;
                _scanAt = -1;

                // the pass is done: pins of ships and carts that are gone leave the map
                foreach (var pair in Pins.Where(p => Stale(p.Key, skipVanilla)).ToList())
                {
                    __instance.RemovePin(pair.Value);
                    Pins.Remove(pair.Key);
                }
            }
            catch (Exception ex)
            {
                _failed = true;
                Log.LogError($"pin update failed, ShipPins stops (V+ shows its own pins again if installed): {ex}");
                try
                {
                    Clear(__instance);
                }
                catch (Exception)
                {
                    Pins.Clear();
                }
            }
        }

        // a pin whose ship or cart is gone. the game reuses ZDO objects, so a destroyed boat's ZDO can come back valid as
        // something else: those pins go too, or they stay stranded on the map (aasmall's fix)
        private static bool Stale(ZDO zdo, bool skipVanilla)
        {
            if (!zdo.IsValid()) return true;
            var kind = KindOf(zdo.GetPrefab());
            return kind == null || kind.Vanilla && skipVanilla;
        }

        // like v+'s pins: the build icon at the ship or cart, moved there each update. the ship the player steers has
        // no pin (the player's own marker is there)
        private static void Pin(Minimap map, Ship steered, ZDO zdo, Kind kind)
        {
            var pos = zdo.GetPosition();
            bool has = Pins.TryGetValue(zdo, out var pin);
            if (steered && Vector3.Distance(steered.transform.position, pos) < 0.01f)
            {
                if (has)
                {
                    map.RemovePin(pin);
                    Pins.Remove(zdo);
                }
                return;
            }
            if (has)
            {
                pin.m_pos = pos;
                return;
            }
            // the pin type v+ uses, never saved, with the build icon in place of its own
            pin = map.AddPin(pos, Minimap.PinType.Death, kind.Label, false, false, 0L, default);
            if (kind.Icon) pin.m_icon = kind.Icon;
            pin.m_doubleSize = true;
            Pins.Add(zdo, pin);
        }

        // each frame after the game places the pins: this mod's icons get their own size (the game only knows normal and
        // double), and their names move below them (the game puts a name where a normal size pin ends, so on a bigger
        // icon it covered the icon's lower part)
        private static void FitPins(Minimap __instance)
        {
            if (Pins.Count == 0) return;
            float normal = __instance.m_mode == Minimap.MapMode.Large ? __instance.m_pinSizeLarge : __instance.m_pinSizeSmall;
            float size = normal * _iconSize.Value;
            float drop = (size - normal) * 0.5f + normal * NameGap;
            foreach (var pin in Pins.Values)
            {
                if (!pin.m_uiElement) continue;
                pin.m_uiElement.sizeDelta = new Vector2(size, size);
                var name = pin.m_NamePinData?.PinNameRectTransform;
                if (name) name.anchoredPosition = pin.m_uiElement.anchoredPosition + new Vector2(0f, -drop);
            }
        }

        private const float NameGap = 0.1f; // of a normal pin's size, between the icon and its name

        // takes every pin of this mod off the map
        private static void Clear(Minimap map)
        {
            foreach (var pin in Pins.Values) map.RemovePin(pin);
            Pins.Clear();
            _timer = float.MaxValue;
        }

        // the kind of a prefab, null for anything that is not a ship or cart (asked again until the scene is up)
        private static Kind KindOf(int hash)
        {
            if (Kinds.TryGetValue(hash, out var kind)) return kind;
            if (!ZNetScene.instance) return null;

            var prefab = ZNetScene.instance.GetPrefab(hash);
            if (prefab && (prefab.GetComponent<Ship>() || prefab.GetComponent<Vagon>()))
            {
                var piece = prefab.GetComponent<Piece>();
                bool vanilla = VanillaLabels.TryGetValue(prefab.name, out var label);
                if (!vanilla) label = piece && !string.IsNullOrEmpty(piece.m_name) ? Localization.instance.Localize(piece.m_name) : prefab.name;
                kind = new Kind { Label = label, Icon = piece ? piece.m_icon : null, Vanilla = vanilla };
            }
            Kinds[hash] = kind;
            return kind;
        }
    }
}
