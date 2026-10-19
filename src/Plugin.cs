using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

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
        public const string ModVersion = "1.2.0";
        private const string VPlusGuid = "org.bepinex.plugins.valheim_plus";
        private const string VPlusPins = "ValheimPlus.GameClasses.displayCartsAndBoatsOnMap";
        private const string VPlusConfig = "ValheimPlus.Configurations.Configuration";

        internal static ManualLogSource Log;
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _updateSeconds;
        private static ConfigEntry<float> _iconSize;
        private static ConfigEntry<bool> _showMapToggles;
        private static ConfigEntry<bool> _hideOverlappingLabels;
        // one switch per category, in the order of Category
        private static ConfigEntry<bool>[] _shows;

        private static readonly AccessTools.FieldRef<ZDOMan, List<ZDO>[]> ObjectsBySector =
            AccessTools.FieldRefAccess<ZDOMan, List<ZDO>[]>("m_objectsBySector");

        // the four prefabs v+ pins itself, with v+'s labels so the pins look the same with or without it
        private static readonly Dictionary<string, string> VanillaLabels = new Dictionary<string, string>
        {
            { "Cart", "Cart" }, { "Raft", "Raft" }, { "Karve", "Karve" }, { "VikingShip", "Longship" }
        };

        // the vanilla ships and the vanilla siege machines by prefab name, and the labels of their map switches
        private static readonly HashSet<string> VanillaShipNames = new HashSet<string> { "Raft", "Karve", "VikingShip", "VikingShip_Ashlands" };
        private static readonly HashSet<string> SiegeNames = new HashSet<string> { "Catapult", "BatteringRam" };
        private static readonly string[] CategoryLabels = { "Vanilla ships", "Modded ships", "Vanilla carts", "Modded carts", "Siege carts" };

        // the switch a ship or cart belongs to
        private enum Category { VanillaShip, ModdedShip, VanillaCart, ModdedCart, Siege }

        // what a ship or cart prefab is pinned with
        private sealed class Kind
        {
            internal string Label;
            internal Sprite Icon;
            internal bool Vanilla; // one of the four v+ pins itself
            internal Category Category;
        }

        // prefab hash to its kind, null when the prefab is not a ship or cart (cached once the scene is up)
        private static readonly Dictionary<int, Kind> Kinds = new Dictionary<int, Kind>();
        // keyed by the ZDO object itself: a ZDO's own hash follows its id, which the game resets when it reuses the object
        private static readonly Dictionary<ZDO, Minimap.PinData> Pins = new Dictionary<ZDO, Minimap.PinData>(new SameZdo());
        // every ship and cart found so far, with the kind it was found as (a reused ZDO can come back as something else)
        private static readonly Dictionary<ZDO, Kind> Known = new Dictionary<ZDO, Kind>(new SameZdo());
        private static readonly List<ZDO> Gone = new List<ZDO>();
        private static ZDOMan _knownFor; // the world the known ships belong to
        private static Minimap _map;
        private static float _timer = float.MaxValue; // the first update runs at once, so pins show as the map loads
        private static bool _failed;

        // new ships and carts are found by a pass over the world's objects, spread over frames: the first pass after the
        // map loads looks at FirstScanPerFrame sector slots and objects a frame, so the pins show at once, later passes
        // at ScanPerFrame and at most every SearchSeconds. ships and carts that load near the player are added at once.
        // -1 is no pass running
        private const int FirstScanPerFrame = 20000;
        private const int ScanPerFrame = 2000;
        private const float SearchSeconds = 30f;
        private static int _scanAt = -1;
        private static float _searchTimer = float.MaxValue;
        private static bool _firstPass = true;

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
            _showMapToggles = Config.Bind("Map", "ShowMapToggles", true,
                "Shows ShipPins Options on the large map. Click to expand the category switches. Off: the menu is hidden, and the pins still follow the values below.");
            _hideOverlappingLabels = Config.Bind("Map", "HideOverlappingLabels", true,
                "Hide a ship or cart label when it overlaps another ShipPins label. Icons stay visible. Zoom in to separate the labels. Off: show every label.");
            _shows = new[]
            {
                Config.Bind("Map", "ShowVanillaShips", true, "Pins for the vanilla ships: Raft, Karve, Longship and Drakkar."),
                Config.Bind("Map", "ShowModdedShips", true, "Pins for every other ship, modded ones included."),
                Config.Bind("Map", "ShowVanillaCarts", true, "Pins for the vanilla Cart."),
                Config.Bind("Map", "ShowModdedCarts", true, "Pins for every other cart, wagon and siege machine, modded ones included."),
                Config.Bind("Map", "ShowSiegeCarts", true, "Pins for the vanilla Catapult and battering ram."),
            };

            _vplus = Chainloader.PluginInfos.ContainsKey(VPlusGuid);
            var harmony = new Harmony(ModGuid);
            harmony.Patch(AccessTools.Method(typeof(Minimap), "UpdateMap"), postfix: new HarmonyMethod(typeof(Plugin), nameof(UpdatePins)));
            harmony.Patch(AccessTools.Method(typeof(Minimap), "UpdatePins"), postfix: new HarmonyMethod(typeof(Plugin), nameof(FitPins)));
            try
            {
                var loaded = new HarmonyMethod(typeof(Plugin), nameof(Loaded));
                harmony.Patch(AccessTools.Method(typeof(Ship), "Awake"), postfix: loaded);
                harmony.Patch(AccessTools.Method(typeof(Vagon), "Awake"), postfix: loaded);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"ships and carts that load are found by the search only (the game's Awake changed?): {ex.Message}");
            }
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

        // after the game's own map update: the search for new ships and carts goes on a little each frame, and every few
        // seconds each known one gets its pin where it is
        private static void UpdatePins(Minimap __instance, Player player, float dt)
        {
            if (_failed) return;
            try
            {
                if (__instance != _map)
                {
                    // a new map (another world, a relog): the old pins went with the old one, the search starts at once
                    Pins.Clear();
                    _map = __instance;
                    _timer = float.MaxValue;
                    _searchTimer = float.MaxValue;
                    _scanAt = -1;
                    _firstPass = true;
                    _switches = null;
                    _switchRows = null;
                    _optionsButton = null;
                    _optionsPanel = null;
                    _optionsArrow = null;
                    _optionsOpen = false;
                }
                if (_switches == null) MakeSwitches(__instance);
                SetSwitchesActive(_enabled.Value && _showMapToggles.Value);
                if (!_enabled.Value)
                {
                    Clear(__instance);
                    _scanAt = -1;
                    return;
                }
                if (!player || ZDOMan.instance == null || !ZNetScene.instance) return;
                ForgetOtherWorld();

                _searchTimer += dt;
                if (_scanAt < 0 && _searchTimer >= SearchSeconds)
                {
                    _searchTimer = 0f;
                    _scanAt = 0;
                }
                if (_scanAt >= 0) Search(__instance, player);

                _timer += dt;
                if (_timer < _updateSeconds.Value) return;
                _timer = 0f;
                Move(__instance, player);
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

        // this frame's part of the search pass: ships and carts not known yet are added and pinned at once
        private static void Search(Minimap map, Player player)
        {
            var sectors = ObjectsBySector(ZDOMan.instance);
            int budget = _firstPass ? FirstScanPerFrame : ScanPerFrame;
            while (_scanAt < sectors.Length && budget > 0)
            {
                var sector = sectors[_scanAt++];
                budget--;
                if (sector == null) continue;
                budget -= sector.Count;
                foreach (var zdo in sector)
                {
                    var kind = KindOf(zdo.GetPrefab());
                    if (kind == null || Known.ContainsKey(zdo)) continue;
                    Known[zdo] = kind;
                    if (Shown(kind) && (!kind.Vanilla || !VPlusDrawsPins())) Pin(map, player.GetControlledShip(), zdo, kind);
                }
            }
            if (_scanAt < sectors.Length) return;
            _scanAt = -1;
            _firstPass = false;
        }

        // every known ship and cart's pin moves to where it is. one that is gone leaves the map: the game reuses ZDO
        // objects, so a destroyed boat's ZDO can come back valid as something else, and those pins go too, or they stay
        // stranded on the map (aasmall's fix). a ZDO that came back as another ship is found again as that one
        private static void Move(Minimap map, Player player)
        {
            bool skipVanilla = VPlusDrawsPins();
            var steered = player.GetControlledShip();
            Gone.Clear();
            foreach (var pair in Known)
            {
                var zdo = pair.Key;
                if (!zdo.IsValid() || KindOf(zdo.GetPrefab()) != pair.Value)
                {
                    Gone.Add(zdo);
                    continue;
                }
                if (!Shown(pair.Value) || (pair.Value.Vanilla && skipVanilla)) Unpin(map, zdo);
                else Pin(map, steered, zdo, pair.Value);
            }
            foreach (var zdo in Gone)
            {
                Known.Remove(zdo);
                Unpin(map, zdo);
            }
        }

        // false while the map switch of this kind is off
        private static bool Shown(Kind kind) => _shows[(int)kind.Category].Value;

        private static void Unpin(Minimap map, ZDO zdo)
        {
            if (!Pins.TryGetValue(zdo, out var pin)) return;
            map.RemovePin(pin);
            Pins.Remove(zdo);
        }

        // a ship or cart that loads near the player is known at once, its pin comes with the next update
        private static void Loaded(Component __instance)
        {
            if (_failed) return;
            try
            {
                var view = __instance.GetComponent<ZNetView>();
                var zdo = view ? view.GetZDO() : null;
                if (zdo == null || ZDOMan.instance == null) return;
                ForgetOtherWorld();
                var kind = KindOf(zdo.GetPrefab());
                if (kind == null || Known.ContainsKey(zdo)) return;
                Known[zdo] = kind;
                _timer = float.MaxValue;
            }
            catch (Exception ex)
            {
                Log.LogWarning($"a loaded ship or cart was not added, the search finds it: {ex.Message}");
            }
        }

        // another world (a relog): the ships known before belong to the old one
        private static void ForgetOtherWorld()
        {
            if (ReferenceEquals(ZDOMan.instance, _knownFor)) return;
            _knownFor = ZDOMan.instance;
            Known.Clear();
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
            LabelRects.Clear();
            foreach (var pin in Pins.Values)
            {
                if (!pin.m_uiElement) continue;
                pin.m_uiElement.sizeDelta = new Vector2(size, size);
                var name = pin.m_NamePinData?.PinNameRectTransform;
                if (name) name.anchoredPosition = pin.m_uiElement.anchoredPosition + new Vector2(0f, -drop);
                var text = pin.m_NamePinData?.PinNameText;
                if (!name || !text) continue;
                bool show = true;
                if (_hideOverlappingLabels.Value && name.gameObject.activeInHierarchy)
                {
                    var rect = text.rectTransform;
                    var center = name.parent.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
                    float width = Math.Min(text.preferredWidth, rect.rect.width);
                    var extent = name.parent.InverseTransformVector(rect.TransformVector(new Vector3(width, rect.rect.height, 0f))) * 0.5f;
                    float gap = normal * NameGap;
                    show = KeepLabel(new Rect(center.x - extent.x - gap, center.y - extent.y - gap,
                        extent.x * 2f + gap * 2f, extent.y * 2f + gap * 2f));
                }
                if (text.enabled != show) text.enabled = show;
            }
        }

        private const float NameGap = 0.1f; // of a normal pin's size, between the icon and its name
        private static readonly List<Rect> LabelRects = new List<Rect>();

        // keeps one readable label in each occupied area without removing any pin
        internal static bool KeepLabel(Rect bounds)
        {
            foreach (var rect in LabelRects)
                if (rect.Overlaps(bounds)) return false;
            LabelRects.Add(bounds);
            return true;
        }

        // full native rows above the map controls, including their translucent backgrounds
        private static readonly AccessTools.FieldRef<Minimap, Toggle> PublicPosition =
            AccessTools.FieldRefAccess<Minimap, Toggle>("m_publicPosition");
        private static readonly AccessTools.FieldRef<Minimap, GameObject> SharedMapHint =
            AccessTools.FieldRefAccess<Minimap, GameObject>("m_sharedMapHint");
        private static Toggle[] _switches;
        private static RectTransform[] _switchRows;
        private static Button _optionsButton;
        private static RectTransform _optionsPanel;
        private static TMPro.TMP_Text _optionsArrow;
        private static bool _optionsOpen;
        private const float RowGap = 6f;

        // each row clears both native controls, with category zero at the top
        internal static float RowOffset(int index, int count, float bottom, float top, float height) =>
            top - bottom + RowGap + (count - index - 1) * (height + RowGap);

        internal static float OptionsHeight(int count, float height) => count * height + (count + 1) * RowGap;

        // closing the map or hiding the menu also collapses its categories
        internal static bool MenuVisible(bool on, bool large, bool expanded) => on && large && expanded;

        // one native header opens a compact category panel above the map controls
        private static void MakeSwitches(Minimap map)
        {
            var template = PublicPosition(map);
            if (!template) return;
            var templateRect = template.transform.parent as RectTransform;
            if (!templateRect) return;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(templateRect.parent, templateRect);
            float top = bounds.max.y;
            var shared = SharedMapHint(map);
            if (shared)
                top = Math.Max(top, RectTransformUtility.CalculateRelativeRectTransformBounds(templateRect.parent, shared.transform).max.y);
            float height = templateRect.rect.height;
            var header = UnityEngine.Object.Instantiate(templateRect, templateRect.parent);
            header.name = "ShipPins Options";
            header.anchoredPosition = templateRect.anchoredPosition + new Vector2(0f,
                RowOffset(0, 1, bounds.min.y, top, height));
            var headerToggle = header.GetComponentInChildren<Toggle>(true);
            headerToggle.onValueChanged = new Toggle.ToggleEvent();
            headerToggle.gameObject.SetActive(false);
            var label = UnityEngine.Object.Instantiate(template.GetComponentInChildren<TMPro.TMP_Text>(true), header);
            label.text = "ShipPins Options";
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            label.rectTransform.anchoredPosition += template.GetComponent<RectTransform>().anchoredPosition;
            _optionsArrow = UnityEngine.Object.Instantiate(label, header);
            _optionsArrow.name = "ShipPins options arrow";
            _optionsArrow.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _optionsArrow.rectTransform.anchoredPosition = template.GetComponent<RectTransform>().anchoredPosition;
            _optionsArrow.rectTransform.sizeDelta = template.GetComponent<RectTransform>().rect.size;
            _optionsArrow.alignment = TMPro.TextAlignmentOptions.Center;
            _optionsArrow.raycastTarget = false;
            _optionsButton = header.gameObject.AddComponent<Button>();
            _optionsButton.targetGraphic = header.GetComponent<Image>();
            _optionsButton.transition = Selectable.Transition.None;
            _optionsButton.navigation = template.navigation;
            _optionsButton.onClick.AddListener(() =>
            {
                _optionsOpen = !_optionsOpen;
                SetSwitchesActive(_enabled.Value && _showMapToggles.Value);
            });
            _optionsPanel = UnityEngine.Object.Instantiate(templateRect, templateRect.parent);
            _optionsPanel.name = "ShipPins categories";
            _optionsPanel.anchoredPosition = header.anchoredPosition + new Vector2(0f, height + RowGap);
            _optionsPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, OptionsHeight(_shows.Length, height));
            var panelToggle = _optionsPanel.GetComponentInChildren<Toggle>(true);
            panelToggle.onValueChanged = new Toggle.ToggleEvent();
            panelToggle.gameObject.SetActive(false);
            _optionsOpen = false;
            _switches = new Toggle[_shows.Length];
            _switchRows = new RectTransform[_shows.Length];
            for (int i = 0; i < _shows.Length; i++)
            {
                var row = UnityEngine.Object.Instantiate(templateRect, _optionsPanel);
                row.name = "ShipPins " + CategoryLabels[i];
                row.anchoredPosition = new Vector2(0f, RowOffset(i, _shows.Length, 0f, 0f, height));
                row.GetComponent<Image>().enabled = false;
                var toggle = row.GetComponentInChildren<Toggle>(true);
                toggle.name = "ShipPins " + CategoryLabels[i];
                // the copy carries the game's listener, which would flip "visible to other players"
                toggle.onValueChanged = new Toggle.ToggleEvent();
                SetLabel(toggle.gameObject, CategoryLabels[i]);
                toggle.isOn = _shows[i].Value;
                int index = i;
                toggle.onValueChanged.AddListener(on =>
                {
                    _shows[index].Value = on;
                    _timer = float.MaxValue; // the pins follow the switch at the next update
                });
                _switches[i] = toggle;
                _switchRows[i] = row;
            }
            Log.LogInfo("ShipPins Options opens the category panel, collapsed whenever the map opens");
        }

        // the text of a copied toggle, whichever text component the game uses
        private static void SetLabel(GameObject go, string text)
        {
            foreach (var component in go.GetComponentsInChildren<Component>(true))
            {
                var prop = component.GetType().GetProperty("text");
                if (prop == null || prop.PropertyType != typeof(string) || !prop.CanWrite) continue;
                prop.SetValue(component, text, null);
                return;
            }
        }

        // the header follows the settings, and only its expanded panel shows categories
        private static void SetSwitchesActive(bool on)
        {
            bool large = _map && _map.m_mode == Minimap.MapMode.Large;
            _optionsOpen = MenuVisible(on, large, _optionsOpen);
            if (_optionsButton && _optionsButton.gameObject.activeSelf != (on && large))
                _optionsButton.gameObject.SetActive(on && large);
            if (_optionsPanel && _optionsPanel.gameObject.activeSelf != _optionsOpen)
                _optionsPanel.gameObject.SetActive(_optionsOpen);
            if (_optionsArrow) _optionsArrow.text = _optionsOpen ? "v" : ">";
            if (_switchRows == null) return;
            foreach (var row in _switchRows)
                if (row && row.gameObject.activeSelf != _optionsOpen) row.gameObject.SetActive(_optionsOpen);
        }

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
                kind = new Kind { Label = label, Icon = piece ? piece.m_icon : null, Vanilla = vanilla, Category = CategoryOf(prefab) };
                Log.LogInfo($"{prefab.name} is pinned as {kind.Category}");
            }
            Kinds[hash] = kind;
            return kind;
        }

        // the switch a ship or cart belongs to: vanilla ships and the cart by name, the vanilla siege machines by name,
        // every other ship, cart or siege machine counts as modded
        private static Category CategoryOf(GameObject prefab)
        {
            if (prefab.GetComponent<Ship>()) return VanillaShipNames.Contains(prefab.name) ? Category.VanillaShip : Category.ModdedShip;
            if (prefab.name == "Cart") return Category.VanillaCart;
            return SiegeNames.Contains(prefab.name) ? Category.Siege : Category.ModdedCart;
        }

        private sealed class SameZdo : IEqualityComparer<ZDO>
        {
            public bool Equals(ZDO a, ZDO b) => ReferenceEquals(a, b);
            public int GetHashCode(ZDO zdo) => RuntimeHelpers.GetHashCode(zdo);
        }
    }
}
