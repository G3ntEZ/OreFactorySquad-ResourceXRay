using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using Il2Cpp;
using Il2CppI2.Loc;

[assembly: MelonInfo(typeof(OFSResourceXRay.ResourceXRayMod), "Resource X-Ray", "1.4.0", "Auto")]
[assembly: MelonGame("threeW", "Ore Factory Squad")]

namespace OFSResourceXRay
{
    public class ResourceXRayMod : MelonMod
    {
        private MelonPreferences_Category _prefs;
        private MelonPreferences_Entry<string> _selectedPrefs;
        private MelonPreferences_Entry<bool> _espEnabledPrefs;
        private MelonPreferences_Entry<float> _maxDistancePrefs;
        private MelonPreferences_Entry<string> _langPrefs;

        private bool _espEnabled = true;
        private bool _menuOpen;
        /// <summary>auto | ru | en</summary>
        private string _langMode = "auto";
        private bool _russian = true;
        private float _maxDistance = 250f;
        private float _refreshInterval = 0.6f;
        private float _nextRefresh;
        private float _nextLangSync;
        private int _menuIndex;
        private int _menuScrollRows;

        private readonly List<EspEntry> _entries = new List<EspEntry>(256);
        private readonly List<OreOption> _oreOptions = new List<OreOption>(64);
        private readonly HashSet<string> _selectedIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Rect> _clickRects = new List<Rect>(64);
        private float _nextCatalogRefresh;
        private GUIStyle _labelStyle;
        private GUIStyle _hudStyle;
        private Texture2D _pixel;
        private Texture2D _panelBg;
        private Texture2D _rowOn;
        private Texture2D _rowOff;
        private Texture2D _rowHi;
        private bool _loggedGuiOnce;

        private const int VisibleRows = 16;
        private const float RowH = 28f;

        private struct EspEntry
        {
            public Vector3 WorldPos;
            public string Label;
            public Color Color;
            public float Distance;
        }

        private class OreOption
        {
            public string Id;
            public string Name;
            public Color Color;
        }

        public override void OnInitializeMelon()
        {
            _prefs = MelonPreferences.CreateCategory("ResourceXRay", "Resource X-Ray");
            _selectedPrefs = _prefs.CreateEntry("SelectedOreIds", "", "Selected ore IDs");
            _espEnabledPrefs = _prefs.CreateEntry("EspEnabled", true, "ESP enabled");
            _maxDistancePrefs = _prefs.CreateEntry("MaxDistance", 250f, "Max ESP distance");
            _langPrefs = _prefs.CreateEntry("Language", "auto", "UI language: auto (follow game) / ru / en");

            _espEnabled = _espEnabledPrefs.Value;
            _maxDistance = _maxDistancePrefs.Value;
            _langMode = NormalizeLangMode(_langPrefs.Value);
            ApplyLanguageFromMode(forceLog: false);
            LoadSelectedFromPrefs();

            LoggerInstance.Msg(_russian
                ? "Resource X-Ray v1.4 | F8 меню | L язык (Авто/RU/EN) | по умолчанию как в игре"
                : "Resource X-Ray v1.4 | F8 menu | L language (Auto/RU/EN) | defaults to game language");
        }

        public override void OnUpdate()
        {
            try
            {
                if (WasPressed(Key.F8))
                {
                    _menuOpen = !_menuOpen;
                    if (_menuOpen)
                    {
                        RefreshOreCatalog(force: true);
                        ClampMenuIndex();
                    }
                }

                if (WasPressed(Key.F7))
                {
                    _espEnabled = !_espEnabled;
                    _espEnabledPrefs.Value = _espEnabled;
                    MelonPreferences.Save();
                    LoggerInstance.Msg(_russian
                        ? $"Рентген {(_espEnabled ? "ВКЛ" : "ВЫКЛ")}"
                        : $"ESP {(_espEnabled ? "ON" : "OFF")}");
                }

                if (WasPressed(Key.F6))
                {
                    _nextRefresh = 0f;
                    RefreshOreCatalog(force: true);
                }

                if (_menuOpen)
                    HandleMenuInput();

                if (!_espEnabled || _selectedIds.Count == 0)
                {
                    _entries.Clear();
                    return;
                }

                if (Time.unscaledTime >= _nextRefresh)
                {
                    _nextRefresh = Time.unscaledTime + _refreshInterval;
                    ScanNodes();
                }

                if (Time.unscaledTime >= _nextCatalogRefresh)
                    RefreshOreCatalog(force: false);

                // Keep Auto language in sync with the game
                if (_langMode == "auto" && Time.unscaledTime >= _nextLangSync)
                {
                    _nextLangSync = Time.unscaledTime + 2f;
                    ApplyLanguageFromMode(forceLog: false);
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"OnUpdate failed: {ex}");
            }
        }

        public override void OnGUI()
        {
            try
            {
                EnsureStyles();
                DrawHud();
                if (_menuOpen)
                    DrawMenu();
                if (_espEnabled)
                    DrawEsp();
            }
            catch (Exception ex)
            {
                if (!_loggedGuiOnce)
                {
                    _loggedGuiOnce = true;
                    LoggerInstance.Error($"OnGUI failed (logged once): {ex}");
                }
            }
        }

        private void HandleMenuInput()
        {
            if (WasPressed(Key.Escape))
            {
                _menuOpen = false;
                return;
            }

            if (WasPressed(Key.UpArrow) || WasPressed(Key.W))
            {
                _menuIndex--;
                ClampMenuIndex();
            }
            if (WasPressed(Key.DownArrow) || WasPressed(Key.S))
            {
                _menuIndex++;
                ClampMenuIndex();
            }
            if (WasPressed(Key.PageUp))
            {
                _menuIndex -= VisibleRows;
                ClampMenuIndex();
            }
            if (WasPressed(Key.PageDown))
            {
                _menuIndex += VisibleRows;
                ClampMenuIndex();
            }

            if (WasPressed(Key.Enter) || WasPressed(Key.Space) || WasPressed(Key.E))
                ToggleIndex(_menuIndex);

            if (WasPressed(Key.Digit1) || WasPressed(Key.Numpad1))
            {
                foreach (OreOption o in _oreOptions)
                    _selectedIds.Add(o.Id);
                SaveSelectedToPrefs();
                _nextRefresh = 0f;
            }
            if (WasPressed(Key.Digit2) || WasPressed(Key.Numpad2))
            {
                _selectedIds.Clear();
                SaveSelectedToPrefs();
                _entries.Clear();
            }

            if (WasPressed(Key.L))
                ToggleLanguage();

            // Mouse click on rows (Input System, no IMGUI Event)
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 sp = mouse.position.ReadValue();
                Vector2 gui = new Vector2(sp.x, Screen.height - sp.y);
                for (int i = 0; i < _clickRects.Count; i++)
                {
                    if (_clickRects[i].Contains(gui))
                    {
                        int absolute = _menuScrollRows + i;
                        _menuIndex = absolute;
                        ToggleIndex(absolute);
                        break;
                    }
                }

                // Top action buttons
                if (_allOnRect.Contains(gui))
                {
                    foreach (OreOption o in _oreOptions)
                        _selectedIds.Add(o.Id);
                    SaveSelectedToPrefs();
                    _nextRefresh = 0f;
                }
                else if (_allOffRect.Contains(gui))
                {
                    _selectedIds.Clear();
                    SaveSelectedToPrefs();
                    _entries.Clear();
                }
                else if (_closeRect.Contains(gui))
                {
                    _menuOpen = false;
                }
                else if (_langRect.Contains(gui))
                {
                    ToggleLanguage();
                }
            }
        }

        private Rect _allOnRect;
        private Rect _allOffRect;
        private Rect _closeRect;
        private Rect _langRect;

        private void ToggleLanguage()
        {
            // Cycle: auto -> ru -> en -> auto
            if (_langMode == "auto") _langMode = "ru";
            else if (_langMode == "ru") _langMode = "en";
            else _langMode = "auto";

            _langPrefs.Value = _langMode;
            MelonPreferences.Save();
            ApplyLanguageFromMode(forceLog: true);
        }

        private static string NormalizeLangMode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "auto";
            value = value.Trim().ToLowerInvariant();
            if (value == "ru" || value == "russian" || value == "рус" || value == "русский")
                return "ru";
            if (value == "en" || value == "english" || value == "eng")
                return "en";
            return "auto";
        }

        private void ApplyLanguageFromMode(bool forceLog)
        {
            bool prev = _russian;
            if (_langMode == "ru")
                _russian = true;
            else if (_langMode == "en")
                _russian = false;
            else
                _russian = DetectGameIsRussian();

            if (forceLog || prev != _russian)
            {
                string modeLabel = _langMode == "auto"
                    ? (_russian ? "Авто → русский (как в игре)" : "Auto → English (game language)")
                    : (_russian ? "Русский (вручную)" : "English (manual)");
                LoggerInstance.Msg(modeLabel);
            }
        }

        private static bool DetectGameIsRussian()
        {
            try
            {
                string code = LocalizationManager.CurrentLanguageCode;
                if (!string.IsNullOrEmpty(code))
                {
                    code = code.ToLowerInvariant();
                    if (code.StartsWith("ru"))
                        return true;
                    if (code.StartsWith("en"))
                        return false;
                }

                string lang = LocalizationManager.CurrentLanguage;
                if (!string.IsNullOrEmpty(lang))
                {
                    lang = lang.ToLowerInvariant();
                    if (lang.Contains("russ") || lang.Contains("рус"))
                        return true;
                    if (lang.Contains("engl") || lang == "en")
                        return false;
                }
            }
            catch
            {
                // I2 not ready yet
            }

            // Fallback: Windows UI culture
            try
            {
                string cul = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (cul == "ru")
                    return true;
            }
            catch { }

            return false;
        }

        private string LangButtonLabel()
        {
            if (_langMode == "auto")
                return _russian ? "Язык: АВТО (RU)" : "Lang: AUTO (EN)";
            if (_langMode == "ru")
                return "Язык: RU";
            return "Lang: EN";
        }

        private string T(string ru, string en) => _russian ? ru : en;

        private void ToggleIndex(int index)
        {
            if (index < 0 || index >= _oreOptions.Count)
                return;
            OreOption ore = _oreOptions[index];
            if (_selectedIds.Contains(ore.Id))
                _selectedIds.Remove(ore.Id);
            else
                _selectedIds.Add(ore.Id);
            SaveSelectedToPrefs();
            _nextRefresh = 0f;
        }

        private void ClampMenuIndex()
        {
            if (_oreOptions.Count == 0)
            {
                _menuIndex = 0;
                _menuScrollRows = 0;
                return;
            }
            if (_menuIndex < 0) _menuIndex = 0;
            if (_menuIndex >= _oreOptions.Count) _menuIndex = _oreOptions.Count - 1;

            if (_menuIndex < _menuScrollRows)
                _menuScrollRows = _menuIndex;
            if (_menuIndex >= _menuScrollRows + VisibleRows)
                _menuScrollRows = _menuIndex - VisibleRows + 1;
            if (_menuScrollRows < 0) _menuScrollRows = 0;
        }

        private static bool WasPressed(Key key)
        {
            Keyboard kb = Keyboard.current;
            if (kb == null)
                return false;
            var control = kb[key];
            return control != null && control.wasPressedThisFrame;
        }

        private void DrawHud()
        {
            string status = _espEnabled
                ? T(
                    $"Рентген ВКЛ | выбрано:{_selectedIds.Count} | меток:{_entries.Count} | F8 меню | F7 выкл | L язык",
                    $"X-Ray ON | selected:{_selectedIds.Count} | markers:{_entries.Count} | F8 menu | F7 off | L lang")
                : T("Рентген ВЫКЛ (F7) | F8 меню руд", "X-Ray OFF (F7) | F8 ore menu");
            SafeLabel(new Rect(12f, 12f, 900f, 28f), status, _hudStyle);
        }

        private void DrawMenu()
        {
            float w = 520f;
            float h = 80f + VisibleRows * RowH;
            Rect panel = new Rect(20f, 48f, w, h);

            SafeDrawTexture(panel, _panelBg);
            SafeLabel(new Rect(panel.x + 12f, panel.y + 8f, w - 24f, 22f),
                T("Рентген руд — клик или ↑↓ + Enter | 1=всё вкл  2=всё выкл  Esc=закрыть  L=язык (Авто/RU/EN)",
                  "Ore X-Ray — click or Up/Down + Enter | 1=All ON  2=All OFF  Esc=Close  L=lang (Auto/RU/EN)"),
                _hudStyle);

            float y = panel.y + 36f;
            _allOnRect = new Rect(panel.x + 12f, y, 110f, 24f);
            _allOffRect = new Rect(panel.x + 130f, y, 110f, 24f);
            _closeRect = new Rect(panel.x + 248f, y, 100f, 24f);
            _langRect = new Rect(panel.x + 356f, y, 140f, 24f);
            DrawFakeButton(_allOnRect, T("Всё ВКЛ", "All ON"), false);
            DrawFakeButton(_allOffRect, T("Всё ВЫКЛ", "All OFF"), false);
            DrawFakeButton(_closeRect, T("Закрыть", "Close"), false);
            DrawFakeButton(_langRect, LangButtonLabel(), false);

            y += 32f;
            _clickRects.Clear();

            if (_oreOptions.Count == 0)
            {
                SafeLabel(new Rect(panel.x + 12f, y, w - 24f, 40f),
                    T("Руд пока нет. Зайди на участок / дождись загрузки мира, затем F6.",
                      "No ores found yet. Enter a dig property / wait for world load, then F6."),
                    _hudStyle);
                return;
            }

            int end = Math.Min(_oreOptions.Count, _menuScrollRows + VisibleRows);
            for (int i = _menuScrollRows; i < end; i++)
            {
                OreOption ore = _oreOptions[i];
                bool on = _selectedIds.Contains(ore.Id);
                bool hi = i == _menuIndex;
                Rect row = new Rect(panel.x + 12f, y, w - 24f, RowH - 2f);
                _clickRects.Add(row);

                Texture2D bg = hi ? _rowHi : (on ? _rowOn : _rowOff);
                SafeDrawTexture(row, bg);

                string mark = on ? T("[ВКЛ]", "[ON] ") : T("[ВЫКЛ]", "[OFF]");
                string prefix = hi ? "> " : "  ";
                string displayName = LocalizeOreName(ore.Name);
                GUI.color = ore.Color;
                SafeLabel(new Rect(row.x + 8f, row.y + 4f, row.width - 16f, row.height),
                    $"{prefix}{mark}  {displayName}", _labelStyle);
                GUI.color = Color.white;
                y += RowH;
            }

            if (_oreOptions.Count > VisibleRows)
            {
                SafeLabel(new Rect(panel.x + 12f, panel.yMax - 22f, w - 24f, 20f),
                    T($"Список: {_menuScrollRows + 1}-{end} / {_oreOptions.Count}  (PgUp/PgDn)",
                      $"Scroll: {_menuScrollRows + 1}-{end} / {_oreOptions.Count}  (PgUp/PgDn)"),
                    _hudStyle);
            }
        }

        private void DrawFakeButton(Rect r, string text, bool active)
        {
            SafeDrawTexture(r, active ? _rowOn : _rowOff);
            SafeLabel(new Rect(r.x + 8f, r.y + 3f, r.width - 10f, r.height), text, _hudStyle);
        }

        private void DrawEsp()
        {
            if (_entries.Count == 0)
                return;

            Camera cam = GetCamera();
            if (cam == null)
                return;

            for (int i = 0; i < _entries.Count; i++)
            {
                EspEntry e = _entries[i];
                Vector3 screen = cam.WorldToScreenPoint(e.WorldPos);
                if (screen.z <= 0.1f)
                    continue;

                float x = screen.x;
                float y = Screen.height - screen.y;
                DrawMarker(x, y, e.Color);
                GUI.color = e.Color;
                SafeLabel(new Rect(x + 10f, y - 10f, 280f, 40f), $"{e.Label}  {e.Distance:0}m", _labelStyle);
                GUI.color = Color.white;
            }
        }

        private void SafeLabel(Rect r, string text, GUIStyle style)
        {
            try { GUI.Label(r, text, style); }
            catch { try { GUI.Label(r, text); } catch { /* stripped */ } }
        }

        private void SafeDrawTexture(Rect r, Texture2D tex)
        {
            if (tex == null) return;
            try { GUI.DrawTexture(r, tex); } catch { /* stripped */ }
        }

        private void RefreshOreCatalog(bool force)
        {
            _nextCatalogRefresh = Time.unscaledTime + 5f;
            var found = new Dictionary<string, OreOption>(StringComparer.Ordinal);

            ItemSOManager mgr = ItemSOManager.Instance;
            if (mgr != null)
            {
                Il2CppSystem.Collections.Generic.List<T_ItemSO> all = null;
                try { all = mgr.GetAllItemSOs(); } catch { /* */ }
                if (all != null)
                {
                    int count = all.Count;
                    for (int i = 0; i < count; i++)
                    {
                        T_ItemSO so = all[i];
                        TryAddOre(found, so);
                    }
                }
            }

            try
            {
                var live = UnityEngine.Object.FindObjectsOfType<T_Item>(true);
                if (live != null)
                {
                    for (int i = 0; i < live.Length; i++)
                    {
                        T_Item item = live[i];
                        if (item == null || !item.isNode)
                            continue;
                        TryAddOre(found, item.so);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Live ore scan failed: {ex.Message}");
            }

            if (!force && found.Count == _oreOptions.Count)
                return;

            _oreOptions.Clear();
            foreach (var kv in found)
                _oreOptions.Add(kv.Value);
            _oreOptions.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            ClampMenuIndex();
        }

        private static void TryAddOre(Dictionary<string, OreOption> found, T_ItemSO so)
        {
            if (so == null)
                return;
            if (!(so.isNode || so.Type == PickupType.Ore))
                return;
            string id = so.GetItemID();
            if (string.IsNullOrEmpty(id) || found.ContainsKey(id))
                return;
            string name = string.IsNullOrEmpty(so.Name) ? id : so.Name;
            found[id] = new OreOption
            {
                Id = id,
                Name = name,
                Color = ColorForName(name)
            };
        }

        private void ScanNodes()
        {
            _entries.Clear();
            if (_selectedIds.Count == 0)
                return;

            Camera cam = GetCamera();
            Vector3 origin = cam != null ? cam.transform.position : Vector3.zero;

            T_Item[] items;
            try
            {
                items = UnityEngine.Object.FindObjectsOfType<T_Item>(true);
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"FindObjectsOfType failed: {ex.Message}");
                return;
            }

            if (items == null)
                return;

            for (int i = 0; i < items.Length; i++)
            {
                T_Item item = items[i];
                if (item == null || !item.isNode)
                    continue;

                T_ItemSO so = item.so;
                if (so == null)
                    continue;

                string id = so.GetItemID();
                if (string.IsNullOrEmpty(id) || !_selectedIds.Contains(id))
                    continue;

                bool addedPiece = false;
                int pieceCount = 0;
                try { pieceCount = item.GetNodePieceCount(); } catch { pieceCount = 0; }

                for (int p = 0; p < pieceCount; p++)
                {
                    T_NodePiece piece = null;
                    try { piece = item.GetNodePiece(p); } catch { continue; }
                    if (piece == null)
                        continue;
                    try
                    {
                        if (piece.IsBroken())
                            continue;
                    }
                    catch { continue; }

                    Transform t = piece.transform;
                    if (t == null)
                        continue;
                    AddEntry(t.position, so, origin);
                    addedPiece = true;
                }

                if (!addedPiece)
                {
                    Transform t = item.transform;
                    if (t != null)
                        AddEntry(t.position, so, origin);
                }
            }
        }

        private void AddEntry(Vector3 worldPos, T_ItemSO so, Vector3 origin)
        {
            float dist = Vector3.Distance(origin, worldPos);
            if (dist > _maxDistance)
                return;

            string name = string.IsNullOrEmpty(so.Name) ? so.GetItemID() : so.Name;
            _entries.Add(new EspEntry
            {
                WorldPos = worldPos,
                Label = LocalizeOreName(name),
                Color = ColorForName(name),
                Distance = dist
            });
        }

        private string LocalizeOreName(string name)
        {
            if (!_russian || string.IsNullOrEmpty(name))
                return name;

            string key = name.Trim().ToLowerInvariant();
            if (OreRu.TryGetValue(key, out string ru))
                return ru;

            if (key.EndsWith(" ore", StringComparison.Ordinal))
            {
                string baseName = key.Substring(0, key.Length - 4).Trim();
                if (OreRu.TryGetValue(baseName, out ru))
                    return ru + " (руда)";
            }

            // Longest-key first to avoid short false positives (e.g. "oil" in "soil")
            string bestKey = null;
            string bestRu = null;
            foreach (var kv in OreRu)
            {
                if (kv.Key.Length < 4)
                    continue;
                if (key.Contains(kv.Key) && (bestKey == null || kv.Key.Length > bestKey.Length))
                {
                    bestKey = kv.Key;
                    bestRu = kv.Value;
                }
            }
            return bestRu ?? name;
        }

        private static readonly Dictionary<string, string> OreRu = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "iron", "Железо" },
            { "iron ore", "Железная руда" },
            { "copper", "Медь" },
            { "copper ore", "Медная руда" },
            { "coal", "Уголь" },
            { "gold", "Золото" },
            { "gold ore", "Золотая руда" },
            { "silver", "Серебро" },
            { "silver ore", "Серебряная руда" },
            { "quartz", "Кварц" },
            { "sulfur", "Сера" },
            { "sulphur", "Сера" },
            { "clay", "Глина" },
            { "stone", "Камень" },
            { "limestone", "Известняк" },
            { "sandstone", "Песчаник" },
            { "dirt", "Земля" },
            { "soil", "Почва" },
            { "sand", "Песок" },
            { "gravel", "Гравий" },
            { "granite", "Гранит" },
            { "bauxite", "Боксит" },
            { "aluminum", "Алюминий" },
            { "aluminium", "Алюминий" },
            { "platinum", "Платина" },
            { "diamond", "Алмаз" },
            { "uranium", "Уран" },
            { "obsidian", "Обсидиан" },
            { "oil", "Нефть" },
            { "crude oil", "Сырая нефть" },
        };

        private static Camera GetCamera()
        {
            Camera cam = Camera.main;
            if (cam != null)
                return cam;
            try
            {
                Camera[] cams = UnityEngine.Object.FindObjectsOfType<Camera>();
                if (cams == null || cams.Length == 0)
                    return null;
                for (int i = 0; i < cams.Length; i++)
                {
                    if (cams[i] != null && cams[i].enabled && cams[i].gameObject.activeInHierarchy)
                        return cams[i];
                }
                return cams[0];
            }
            catch
            {
                return null;
            }
        }

        private static Color ColorForName(string name)
        {
            string n = (name ?? string.Empty).ToLowerInvariant();
            if (n.Contains("iron")) return new Color(0.85f, 0.4f, 0.25f);
            if (n.Contains("copper")) return new Color(1f, 0.55f, 0.2f);
            if (n.Contains("coal")) return new Color(0.55f, 0.55f, 0.55f);
            if (n.Contains("gold")) return new Color(1f, 0.85f, 0.15f);
            if (n.Contains("silver")) return new Color(0.85f, 0.9f, 0.95f);
            if (n.Contains("quartz")) return new Color(0.8f, 0.95f, 1f);
            if (n.Contains("uranium")) return new Color(0.45f, 1f, 0.3f);
            if (n.Contains("diamond")) return new Color(0.5f, 0.85f, 1f);
            if (n.Contains("clay")) return new Color(0.75f, 0.5f, 0.3f);
            if (n.Contains("stone") || n.Contains("limestone")) return new Color(0.75f, 0.75f, 0.7f);
            if (n.Contains("sulfur")) return new Color(1f, 0.95f, 0.2f);
            if (n.Contains("aluminum") || n.Contains("aluminium") || n.Contains("bauxite")) return new Color(0.7f, 0.8f, 0.9f);
            if (n.Contains("platinum")) return new Color(0.9f, 0.9f, 1f);
            return new Color(0.25f, 1f, 0.55f);
        }

        private void LoadSelectedFromPrefs()
        {
            _selectedIds.Clear();
            string raw = _selectedPrefs.Value ?? "";
            foreach (string part in raw.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string id = part.Trim();
                if (!string.IsNullOrEmpty(id))
                    _selectedIds.Add(id);
            }
        }

        private void SaveSelectedToPrefs()
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (string id in _selectedIds)
            {
                if (!first) sb.Append(',');
                sb.Append(id);
                first = false;
            }
            _selectedPrefs.Value = sb.ToString();
            MelonPreferences.Save();
        }

        private void EnsureStyles()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold };
                _labelStyle.normal.textColor = Color.white;
            }

            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold };
                _hudStyle.normal.textColor = Color.white;
            }

            if (_pixel == null)
                _pixel = MakeTex(Color.white);
            if (_panelBg == null)
                _panelBg = MakeTex(new Color(0f, 0f, 0f, 0.75f));
            if (_rowOn == null)
                _rowOn = MakeTex(new Color(0.12f, 0.45f, 0.2f, 0.9f));
            if (_rowOff == null)
                _rowOff = MakeTex(new Color(0.15f, 0.15f, 0.15f, 0.9f));
            if (_rowHi == null)
                _rowHi = MakeTex(new Color(0.2f, 0.35f, 0.55f, 0.95f));
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private void DrawMarker(float x, float y, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            SafeDrawTexture(new Rect(x - 5f, y - 5f, 10f, 10f), _pixel);
            SafeDrawTexture(new Rect(x - 1f, y - 14f, 2f, 28f), _pixel);
            SafeDrawTexture(new Rect(x - 14f, y - 1f, 28f, 2f), _pixel);
            GUI.color = prev;
        }
    }
}
