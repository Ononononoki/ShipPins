using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ShipPins
{
    // runs v+'s own boat pin routine for every ship and cart prefab instead of its four vanilla ones
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency(VPlusGuid, BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "onononoki.ShipPins";
        public const string ModName = "ShipPins";
        public const string ModVersion = "1.0.2";
        private const string VPlusGuid = "org.bepinex.plugins.valheim_plus";
        private const string VPlusPins = "ValheimPlus.GameClasses.displayCartsAndBoatsOnMap";
        private const string VPlusConfig = "ValheimPlus.Configurations.Configuration";

        internal static ManualLogSource Log;
        private static ConfigEntry<bool> _enabled;

        // v+ internals, found by name so a v+ update can only switch this add-on off, never break v+
        private static Func<Minimap, Player, ZDO, int, string, bool> _checkPin;
        private static Action _findIcons;
        private static Dictionary<ZDO, Minimap.PinData> _customPins;
        private static Dictionary<int, Sprite> _icons;
        private static float _interval;
        private static PropertyInfo _configCurrent, _configMap, _mapEnabled, _mapDisplay;
        private static MethodInfo _target;

        private static readonly AccessTools.FieldRef<ZDOMan, List<ZDO>[]> ObjectsBySector =
            AccessTools.FieldRefAccess<ZDOMan, List<ZDO>[]>("m_objectsBySector");

        // v+ labels for the four prefabs it already pinned, kept so those pins look the same
        private static readonly Dictionary<string, string> VPlusLabels = new Dictionary<string, string>
        {
            { "Cart", "Cart" }, { "Raft", "Raft" }, { "Karve", "Karve" }, { "VikingShip", "Longship" }
        };

        // prefab hash to pin label, null when the prefab is not a ship or cart
        private static readonly Dictionary<int, string> Labels = new Dictionary<int, string>();

        // build icons of ships and carts that are not in the hammer table v+ reads its icons from
        private static readonly Dictionary<int, Sprite> OwnIcons = new Dictionary<int, Sprite>();

        private static float _timer;
        private static bool _failed;

        private void Awake()
        {
            Log = Logger;
            _enabled = Config.Bind("General", "Enabled", true,
                "Pin every ship and cart on the map, modded ones included, in place of V+'s four vanilla pins. Needs V+ [Map] displayCartsAndBoats = true. Off gives V+'s own pins back.");

            if (!Resolve())
            {
                Logger.LogWarning("could not find V+'s boat pin code (V+ update?), V+ keeps its own pins");
                return;
            }

            new Harmony(ModGuid).Patch(_target, prefix: new HarmonyMethod(typeof(Plugin), nameof(ReplaceVPlusPins)));
            Logger.LogInfo($"{ModName} {ModVersion} loaded, V+ boat pins now cover every ship and cart");
        }

        private static bool Resolve()
        {
            var pins = AccessTools.TypeByName(VPlusPins);
            var update = pins == null ? null : AccessTools.Inner(pins, "Minimap_UpdateMap_Patch");
            var config = AccessTools.TypeByName(VPlusConfig);
            if (update == null || config == null) return false;

            _target = AccessTools.Method(update, "Postfix");
            var checkPin = AccessTools.Method(update, "CheckPin");
            var findIcons = AccessTools.Method(update, "FindIcons");
            _configCurrent = AccessTools.Property(config, "Current");
            _configMap = _configCurrent == null ? null : AccessTools.Property(_configCurrent.PropertyType, "Map");
            _mapEnabled = _configMap == null ? null : AccessTools.Property(_configMap.PropertyType, "IsEnabled");
            _mapDisplay = _configMap == null ? null : AccessTools.Property(_configMap.PropertyType, "displayCartsAndBoats");
            if (_target == null || checkPin == null || findIcons == null || _mapEnabled == null || _mapDisplay == null) return false;

            try
            {
                _checkPin = (Func<Minimap, Player, ZDO, int, string, bool>)Delegate.CreateDelegate(typeof(Func<Minimap, Player, ZDO, int, string, bool>), checkPin);
                _findIcons = (Action)Delegate.CreateDelegate(typeof(Action), findIcons);
                _customPins = (Dictionary<ZDO, Minimap.PinData>)AccessTools.Field(pins, "customPins").GetValue(null);
                _icons = (Dictionary<int, Sprite>)AccessTools.Field(pins, "icons").GetValue(null);
                _interval = (float)AccessTools.Field(pins, "updateInterval").GetValue(null);
            }
            catch (Exception ex)
            {
                Log.LogWarning($"V+ boat pin code changed shape: {ex.Message}");
                return false;
            }
            return _customPins != null && _icons != null;
        }

        // same steps as v+'s pin update, for every ship and cart prefab, then skips v+'s own version
        private static bool ReplaceVPlusPins(ref Minimap __0, Player __1, float __2)
        {
            if (_failed || !_enabled.Value) return true;
            try
            {
                _timer += __2;
                if (_timer < _interval || !VPlusDisplayOn()) return false;
                _timer -= _interval;

                if (_icons.Count == 0) _findIcons();

                foreach (var sector in ObjectsBySector(ZDOMan.instance))
                {
                    if (sector == null) continue;
                    foreach (var zdo in sector)
                    {
                        var hash = zdo.GetPrefab();
                        var label = LabelFor(hash);
                        if (label == null) continue;
                        if (!_icons.ContainsKey(hash) && OwnIcons.TryGetValue(hash, out var icon)) _icons[hash] = icon;
                        _checkPin(__0, __1, zdo, hash, label);
                    }
                }

                // drop pins of ships and carts that no longer exist
                foreach (var pair in _customPins.Where(p => !p.Key.IsValid()).ToList())
                {
                    __0.RemovePin(pair.Value);
                    _customPins.Remove(pair.Key);
                }
                return false;
            }
            catch (Exception ex)
            {
                _failed = true;
                Log.LogError($"pin update failed, V+ takes over its own pins again: {ex}");
                return true;
            }
        }

        private static bool VPlusDisplayOn()
        {
            var map = _configMap.GetValue(_configCurrent.GetValue(null, null), null);
            return map != null && (bool)_mapEnabled.GetValue(map, null) && (bool)_mapDisplay.GetValue(map, null);
        }

        // pin label for a ship or cart prefab, null for anything else, cached per prefab
        private static string LabelFor(int hash)
        {
            if (Labels.TryGetValue(hash, out var label)) return label;
            if (!ZNetScene.instance) return null;

            var prefab = ZNetScene.instance.GetPrefab(hash);
            if (prefab && (prefab.GetComponent<Ship>() || prefab.GetComponent<Vagon>()))
            {
                var piece = prefab.GetComponent<Piece>();
                if (VPlusLabels.TryGetValue(prefab.name, out var vplus)) label = vplus;
                else if (piece && !string.IsNullOrEmpty(piece.m_name)) label = Localization.instance.Localize(piece.m_name);
                else label = prefab.name;
                if (piece && piece.m_icon) OwnIcons[hash] = piece.m_icon;
            }
            Labels[hash] = label;
            return label;
        }
    }
}
