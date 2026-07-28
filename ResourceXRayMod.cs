using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using Il2Cpp;
using Il2CppI2.Loc;

[assembly: MelonInfo(typeof(OFSResourceXRay.ResourceXRayMod), "Resource X-Ray", "1.6.3", "G3ntEZ")]
[assembly: MelonGame("threeW", "Ore Factory Squad")]

namespace OFSResourceXRay
{
    public class ResourceXRayMod : MelonMod
    {
        private const string IdScrap = "__scrap__";
        private const string IdAntique = "__antique__";

        private MelonPreferences_Category _prefs;
        private MelonPreferences_Entry<string> _selectedPrefs;
        private MelonPreferences_Entry<bool> _espEnabledPrefs;
        private MelonPreferences_Entry<float> _maxDistancePrefs;
        private MelonPreferences_Entry<string> _langPrefs;
        private MelonPreferences_Entry<bool> _lowPerfPrefs;
        private MelonPreferences_Entry<int> _maxMarkersPrefs;

        private bool _espEnabled = true;
        private bool _menuOpen;
        private bool _lowPerf;
        private string _langMode = "auto";
        private bool _russian = true;
        private float _refreshInterval = 0.75f;
        private float _itemCacheTtl = 1.25f;
        private float _nextRefresh;
        private float _nextLangSync;
        private float _nextCatalogRefresh;
        private int _menuIndex;
        private int _menuScrollRows;

        private readonly List<EspEntry> _entries = new List<EspEntry>(256);
        private readonly List<ManualMarker> _manualMarkers = new List<ManualMarker>(64);
        private readonly List<OreOption> _oreOptions = new List<OreOption>(64);
        private readonly HashSet<string> _selectedIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Rect> _clickRects = new List<Rect>(64);
        private readonly Dictionary<string, string> _nameCache = new Dictionary<string, string>(128, StringComparer.Ordinal);

        private T_Item[] _cachedItems;
        private T_NodePiece[] _cachedPieces;
        private float _cachedItemsTime;

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
            public int Count;
        }

        private struct ManualMarker
        {
            public Vector3 WorldPos;
            public string Label;
        }

        private class OreOption
        {
            public string Id;
            public string NameKey;
            public Color Color;
            public bool IsCategory;
        }

        public override void OnInitializeMelon()
        {
            _prefs = MelonPreferences.CreateCategory("ResourceXRay", "Resource X-Ray");
            _selectedPrefs = _prefs.CreateEntry("SelectedOreIds", "", "Selected target IDs");
            _espEnabledPrefs = _prefs.CreateEntry("EspEnabled", true, "ESP enabled");
            _maxDistancePrefs = _prefs.CreateEntry("MaxDistance", 99999f, "Max ESP distance (unused, unlimited)");
            _langPrefs = _prefs.CreateEntry("Language", "auto", "UI language: auto / ru / en");
            _lowPerfPrefs = _prefs.CreateEntry("LowPerformance", false, "Low performance mode");
            _maxMarkersPrefs = _prefs.CreateEntry("MaxMarkers", 9999, "Max on-screen markers (unused, draw all)");

            _espEnabled = _espEnabledPrefs.Value;
            // Always show the whole map — ignore old saved low distance/marker caps.
            _maxDistancePrefs.Value = 99999f;
            _maxMarkersPrefs.Value = 9999;
            _lowPerf = _lowPerfPrefs.Value;
            _langMode = NormalizeLangMode(_langPrefs.Value);
            ApplyLanguageFromMode(forceLog: false);
            ApplyPerformanceSettings();
            LoadSelectedFromPrefs();

            LoggerInstance.Msg(_russian
                ? "Resource X-Ray v1.6.3 | F8 меню | F4 перезагрузка | U метка | I очистка"
                : "Resource X-Ray v1.6.3 | F8 menu | F4 reload | U marker | I clear");
            LoggerInstance.Msg(_russian
                ? "Обновлений для текущей версии игры пока не планируется."
                : "No further updates planned for the current game version.");
            LoggerInstance.Msg(_russian
                ? "Поддержать разработчика: https://www.donationalerts.com/r/g3ntez"
                : "Support the developer: https://www.donationalerts.com/r/g3ntez");
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
                        _nameCache.Clear();
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

                if (WasPressed(Key.F5))
                {
                    _lowPerf = !_lowPerf;
                    _lowPerfPrefs.Value = _lowPerf;
                    MelonPreferences.Save();
                    ApplyPerformanceSettings();
                    InvalidateItemCache();
                    LoggerInstance.Msg(_russian
                        ? $"Экономный режим {(_lowPerf ? "ВКЛ" : "ВЫКЛ")}"
                        : $"Low perf mode {(_lowPerf ? "ON" : "OFF")}");
                }

                if (WasPressed(Key.F6))
                {
                    _nextRefresh = 0f;
                    _nameCache.Clear();
                    InvalidateItemCache();
                    RefreshOreCatalog(force: true);
                }

                if (WasPressed(Key.F4))
                    ForceReloadOreMarkers();

                if (WasPressed(Key.U))
                    ToggleManualMarker();
                if (WasPressed(Key.I))
                    ClearAllManualMarkers();
                if (WasPressed(Key.F9))
                    ForceUnlockVehiclePurchase();

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
                    ScanTargets();
                }

                if (Time.unscaledTime >= _nextCatalogRefresh)
                    RefreshOreCatalog(force: false);

                if (_langMode == "auto" && Time.unscaledTime >= _nextLangSync)
                {
                    _nextLangSync = Time.unscaledTime + 3f;
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
                DrawManualMarkers();
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

        private void ApplyPerformanceSettings()
        {
            if (_lowPerf)
            {
                _refreshInterval = 1.6f;
                _itemCacheTtl = 3.0f;
            }
            else
            {
                _refreshInterval = 0.75f;
                _itemCacheTtl = 1.25f;
            }
        }

        private void InvalidateItemCache()
        {
            _cachedItems = null;
            _cachedPieces = null;
            _cachedItemsTime = 0f;
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
            if (_langMode == "auto") _langMode = "ru";
            else if (_langMode == "ru") _langMode = "en";
            else _langMode = "auto";

            _langPrefs.Value = _langMode;
            MelonPreferences.Save();
            _nameCache.Clear();
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
            if (_langMode == "ru") _russian = true;
            else if (_langMode == "en") _russian = false;
            else _russian = DetectGameIsRussian();

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
                    if (code.StartsWith("ru")) return true;
                    if (code.StartsWith("en")) return false;
                }

                string lang = LocalizationManager.CurrentLanguage;
                if (!string.IsNullOrEmpty(lang))
                {
                    lang = lang.ToLowerInvariant();
                    if (lang.Contains("russ") || lang.Contains("рус")) return true;
                    if (lang.Contains("engl") || lang == "en") return false;
                }
            }
            catch { }

            try
            {
                if (System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru")
                    return true;
            }
            catch { }

            return false;
        }

        private string LangButtonLabel()
        {
            if (_langMode == "auto")
                return _russian ? "Язык: АВТО" : "Lang: AUTO";
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
            if (kb == null) return false;
            var control = kb[key];
            return control != null && control.wasPressedThisFrame;
        }

        private void DrawHud()
        {
            string perf = _lowPerf ? T(" | ЭКОН", " | LOW") : "";
            string status = _espEnabled
                ? T(
                    $"Рентген ВКЛ | выбрано:{_selectedIds.Count} | меток:{_entries.Count} | U:{_manualMarkers.Count}{perf} | F8 | F4 | F7 | I",
                    $"X-Ray ON | selected:{_selectedIds.Count} | markers:{_entries.Count} | U:{_manualMarkers.Count}{perf} | F8 | F4 | F7 | I")
                : T($"Рентген ВЫКЛ (F7) | F8 меню | U:{_manualMarkers.Count} | F4 | I", $"X-Ray OFF (F7) | F8 menu | U:{_manualMarkers.Count} | F4 | I");
            SafeLabel(new Rect(12f, 12f, 920f, 28f), status, _hudStyle);
        }

        private void DrawMenu()
        {
            float w = 540f;
            float h = 80f + VisibleRows * RowH;
            Rect panel = new Rect(20f, 48f, w, h);

            SafeDrawTexture(panel, _panelBg);
            SafeLabel(new Rect(panel.x + 12f, panel.y + 8f, w - 24f, 22f),
                T("Рентген — ↑↓ Enter | 1=всё 2=выкл | L=язык | F5=эконом | U=метка | I=очистка | F4=reload",
                  "X-Ray — ↑↓ Enter | 1=all 2=off | L=lang | F5=low perf | U=marker | I=clear | F4=reload"),
                _hudStyle);

            float y = panel.y + 36f;
            _allOnRect = new Rect(panel.x + 12f, y, 100f, 24f);
            _allOffRect = new Rect(panel.x + 120f, y, 100f, 24f);
            _closeRect = new Rect(panel.x + 228f, y, 90f, 24f);
            _langRect = new Rect(panel.x + 326f, y, 120f, 24f);
            DrawFakeButton(_allOnRect, T("Всё ВКЛ", "All ON"), false);
            DrawFakeButton(_allOffRect, T("Всё ВЫКЛ", "All OFF"), false);
            DrawFakeButton(_closeRect, T("Закрыть", "Close"), false);
            DrawFakeButton(_langRect, LangButtonLabel(), false);

            y += 32f;
            _clickRects.Clear();

            if (_oreOptions.Count == 0)
            {
                SafeLabel(new Rect(panel.x + 12f, y, w - 24f, 40f),
                    T("Цели не найдены. Зайди на участок и нажми F6.",
                      "No targets yet. Enter a dig property and press F6."),
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

                SafeDrawTexture(row, hi ? _rowHi : (on ? _rowOn : _rowOff));

                string mark = on ? T("[ВКЛ]", "[ON]") : T("[ВЫКЛ]", "[OFF]");
                string prefix = hi ? "> " : "  ";
                string displayName = GetDisplayName(ore.NameKey, ore.Id);
                GUI.color = ore.Color;
                SafeLabel(new Rect(row.x + 8f, row.y + 4f, row.width - 16f, row.height),
                    $"{prefix}{mark}  {displayName}", _labelStyle);
                GUI.color = Color.white;
                y += RowH;
            }

            if (_oreOptions.Count > VisibleRows)
            {
                SafeLabel(new Rect(panel.x + 12f, panel.yMax - 22f, w - 24f, 20f),
                    T($"Список: {_menuScrollRows + 1}-{end} / {_oreOptions.Count}",
                      $"List: {_menuScrollRows + 1}-{end} / {_oreOptions.Count}"),
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

            int drawCount = _entries.Count;
            for (int i = 0; i < drawCount; i++)
            {
                EspEntry e = _entries[i];
                Vector3 screen = cam.WorldToScreenPoint(e.WorldPos);
                if (screen.z <= 0.1f)
                    continue;

                float x = screen.x;
                float y = Screen.height - screen.y;
                DrawMarker(x, y, e.Color);
                GUI.color = e.Color;
                SafeLabel(new Rect(x + 10f, y - 10f, 280f, 40f),
                    e.Count > 1 ? $"{e.Label} x{e.Count}  {e.Distance:0}m" : $"{e.Label}  {e.Distance:0}m",
                    _labelStyle);
                GUI.color = Color.white;
            }
        }

        private void DrawManualMarkers()
        {
            if (_manualMarkers.Count == 0)
                return;

            Camera cam = GetCamera();
            if (cam == null)
                return;

            Color markerColor = new Color(0.2f, 1f, 1f, 1f);
            for (int i = 0; i < _manualMarkers.Count; i++)
            {
                ManualMarker m = _manualMarkers[i];
                Vector3 screen = cam.WorldToScreenPoint(m.WorldPos);
                if (screen.z <= 0.1f)
                    continue;

                float x = screen.x;
                float y = Screen.height - screen.y;
                DrawMarker(x, y, markerColor);
                GUI.color = markerColor;
                float dist = Vector3.Distance(cam.transform.position, m.WorldPos);
                SafeLabel(new Rect(x + 10f, y - 10f, 300f, 40f), $"{m.Label}  {dist:0}m", _labelStyle);
                GUI.color = Color.white;
            }
        }

        private void ToggleManualMarker()
        {
            Camera cam = GetCamera();
            if (cam == null)
                return;

            Vector3 origin = cam.transform.position;
            Vector3 dir = cam.transform.forward;
            Vector3 placePos = origin + dir * 8f;

            int nearest = -1;
            float nearestSq = float.MaxValue;
            for (int i = 0; i < _manualMarkers.Count; i++)
            {
                float d = (_manualMarkers[i].WorldPos - placePos).sqrMagnitude;
                if (d < nearestSq)
                {
                    nearestSq = d;
                    nearest = i;
                }
            }

            if (nearest >= 0 && nearestSq <= 16f)
            {
                string removed = _manualMarkers[nearest].Label;
                _manualMarkers.RemoveAt(nearest);
                LoggerInstance.Msg(T($"Убрана метка: {removed}", $"Removed marker: {removed}"));
                return;
            }

            int index = _manualMarkers.Count + 1;
            _manualMarkers.Add(new ManualMarker
            {
                WorldPos = placePos,
                Label = T($"Метка #{index}", $"Marker #{index}")
            });
            LoggerInstance.Msg(T($"Поставлена метка #{index}", $"Placed marker #{index}"));
        }

        private void ClearAllManualMarkers()
        {
            if (_manualMarkers.Count == 0)
            {
                LoggerInstance.Msg(T("Нет меток для очистки (U).", "No U markers to clear."));
                return;
            }

            int removed = _manualMarkers.Count;
            _manualMarkers.Clear();
            LoggerInstance.Msg(T($"Удалены все метки: {removed}", $"Cleared all markers: {removed}"));
        }

        private void ForceReloadOreMarkers()
        {
            _entries.Clear();
            _nameCache.Clear();
            InvalidateItemCache();
            _nextRefresh = 0f;
            RefreshOreCatalog(force: true);
            if (_espEnabled && _selectedIds.Count > 0)
                ScanTargets();
            LoggerInstance.Msg(T(
                $"F4: метки руд перезагружены (найдено {_entries.Count}).",
                $"F4: ore markers reloaded (found {_entries.Count})."));
        }

        private void ForceUnlockVehiclePurchase()
        {
            int touched = 0;
            int objects = 0;
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
                if (all == null || all.Length == 0)
                {
                    LoggerInstance.Warning(T("F9: объекты не найдены (зайди в мир/меню покупки техники).",
                                            "F9: no objects found (open world/vehicle shop first)."));
                    return;
                }

                for (int i = 0; i < all.Length; i++)
                {
                    var mb = all[i];
                    if (mb == null) continue;
                    Type t = mb.GetType();
                    if (t == null) continue;

                    string tn = t.Name ?? "";
                    string tnl = tn.ToLowerInvariant();
                    if (!tnl.Contains("tutorial") && !tnl.Contains("vehicle") && !tnl.Contains("upgrade") && !tnl.Contains("equipment") && !tnl.Contains("shop"))
                        continue;
                    objects++;

                    touched += TrySetStringMember(mb, t, "Network_tutorialLockedItemId", "");
                    touched += TrySetStringMember(mb, t, "tutorialLockedItemId", "");
                    touched += TrySetStringMember(mb, t, "Network_unlockedOptions", "__all__");
                    touched += TrySetStringMember(mb, t, "unlockedOptions", "__all__");

                    touched += TrySetBoolMember(mb, t, "Network_isTutorialActive", false);
                    touched += TrySetBoolMember(mb, t, "isTutorialActive", false);
                }

                LoggerInstance.Msg(T(
                    $"F9 unlock: обработано объектов {objects}, изменено полей/свойств {touched}. Открой покупку техники заново.",
                    $"F9 unlock: processed {objects} objects, changed {touched} members. Reopen vehicle purchase UI."));
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"F9 unlock failed: {ex.Message}");
            }
        }

        private static int TrySetStringMember(object target, Type t, string name, string value)
        {
            int changed = 0;
            try
            {
                PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanWrite && p.PropertyType == typeof(string))
                {
                    p.SetValue(target, value);
                    changed++;
                }
            }
            catch { }

            try
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(string))
                {
                    f.SetValue(target, value);
                    changed++;
                }
            }
            catch { }
            return changed;
        }

        private static int TrySetBoolMember(object target, Type t, string name, bool value)
        {
            int changed = 0;
            try
            {
                PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanWrite && p.PropertyType == typeof(bool))
                {
                    p.SetValue(target, value);
                    changed++;
                }
            }
            catch { }

            try
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType == typeof(bool))
                {
                    f.SetValue(target, value);
                    changed++;
                }
            }
            catch { }
            return changed;
        }

        private void SafeLabel(Rect r, string text, GUIStyle style)
        {
            try { GUI.Label(r, text, style); }
            catch { try { GUI.Label(r, text); } catch { } }
        }

        private void SafeDrawTexture(Rect r, Texture2D tex)
        {
            if (tex == null) return;
            try { GUI.DrawTexture(r, tex); } catch { }
        }

        private void RefreshOreCatalog(bool force)
        {
            _nextCatalogRefresh = Time.unscaledTime + (_lowPerf ? 12f : 6f);
            var found = new Dictionary<string, OreOption>(StringComparer.Ordinal);

            AddCategory(found, IdScrap, "Item_ScrapName", new Color(0.65f, 0.65f, 0.7f));
            AddCategory(found, IdAntique, "Item_AntiqueName", new Color(0.9f, 0.75f, 0.35f));

            ItemSOManager mgr = ItemSOManager.Instance;
            if (mgr != null)
            {
                try
                {
                    var all = mgr.GetAllItemSOs();
                    if (all != null)
                    {
                        for (int i = 0; i < all.Count; i++)
                            TryAddTarget(found, all[i]);
                    }
                }
                catch { }
            }

            try
            {
                foreach (T_Item item in GetCachedItems())
                {
                    if (item == null) continue;
                    TryAddTarget(found, item.so, item.isMysteryItem);
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Live scan failed: {ex.Message}");
            }

            if (!force && found.Count == _oreOptions.Count)
                return;

            _oreOptions.Clear();
            foreach (var kv in found)
                _oreOptions.Add(kv.Value);

            _oreOptions.Sort((a, b) =>
            {
                if (a.IsCategory != b.IsCategory)
                    return a.IsCategory ? -1 : 1;
                return string.Compare(GetDisplayName(a.NameKey, a.Id), GetDisplayName(b.NameKey, b.Id), StringComparison.OrdinalIgnoreCase);
            });
            ClampMenuIndex();
        }

        private static void AddCategory(Dictionary<string, OreOption> found, string id, string nameKey, Color color)
        {
            found[id] = new OreOption
            {
                Id = id,
                NameKey = nameKey,
                Color = color,
                IsCategory = true
            };
        }

        private static void TryAddTarget(Dictionary<string, OreOption> found, T_ItemSO so, bool isMystery = false)
        {
            if (so == null)
                return;

            bool isOre = so.isNode || so.Type == PickupType.Ore;
            bool isScrap = so.Type == PickupType.Scrap || (isMystery && so.mysteryType == MysteryItemType.Scrap);
            bool isAntique = so.Type == PickupType.Antique || (isMystery && so.mysteryType == MysteryItemType.Antique);

            if (!isOre && !isScrap && !isAntique)
                return;

            string id = so.GetItemID();
            if (string.IsNullOrEmpty(id) || found.ContainsKey(id))
                return;

            // Keep Scrap/Antique as top category toggles only.
            // Without this, ItemSO entries duplicate the category rows in menu.
            if (isScrap || isAntique)
                return;

            string nameKey = string.IsNullOrEmpty(so.Name) ? id : so.Name;
            found[id] = new OreOption
            {
                Id = id,
                NameKey = nameKey,
                Color = ColorForItem(so, isScrap, isAntique),
                IsCategory = false
            };
        }

        private void ScanTargets()
        {
            _entries.Clear();
            if (_selectedIds.Count == 0)
                return;

            bool wantScrap = _selectedIds.Contains(IdScrap);
            bool wantAntique = _selectedIds.Contains(IdAntique);

            Camera cam = GetCamera();
            Vector3 origin = cam != null ? cam.transform.position : Vector3.zero;
            var parentCollectSeen = new HashSet<int>();

            foreach (T_Item item in GetCachedItems())
            {
                if (item == null)
                    continue;

                T_ItemSO so = item.so;
                TargetKind kind = Classify(so, item.isMysteryItem, item.isNode);
                if (!IsWanted(kind, so, wantScrap, wantAntique))
                    continue;

                // One label per ore vein / item — not per rock piece.
                // No distance filter: show every selected vein on the map.
                Vector3 sum = Vector3.zero;
                int pieceAlive = 0;
                int pieceCount = 0;
                try { pieceCount = item.GetNodePieceCount(); } catch { }

                for (int p = 0; p < pieceCount; p++)
                {
                    T_NodePiece piece = null;
                    try { piece = item.GetNodePiece(p); } catch { continue; }
                    if (piece == null) continue;
                    try { if (piece.IsBroken()) continue; } catch { continue; }

                    Transform t = piece.transform;
                    if (t == null) continue;
                    sum += t.position;
                    pieceAlive++;
                }

                if (pieceAlive > 0)
                {
                    AddEntry(sum / pieceAlive, so, kind, origin, pieceAlive);
                }
                else
                {
                    Transform t = item.transform;
                    if (t != null)
                        AddEntry(t.position, so, kind, origin, 1);
                }
            }

            // Hidden collect nodes (antiques inside rocks) — one marker per parent item
            if (wantScrap || wantAntique || HasAnyOreSelected())
            {
                foreach (T_NodePiece piece in GetCachedPieces())
                {
                    if (piece == null) continue;
                    try
                    {
                        if (piece.IsBroken()) continue;
                        if (!piece.HasCollectItem()) continue;
                    }
                    catch { continue; }

                    T_Item parent = null;
                    try { parent = piece.GetParentItem(); } catch { }
                    if (parent != null)
                    {
                        int pid = parent.GetInstanceID();
                        if (!parentCollectSeen.Add(pid))
                            continue;
                    }

                    T_ItemSO so = parent != null ? parent.so : null;
                    TargetKind kind = Classify(so, parent != null && parent.isMysteryItem, parent != null && parent.isNode);
                    if (kind == TargetKind.Unknown)
                        kind = TargetKind.Antique;

                    if (!IsWanted(kind, so, wantScrap, wantAntique))
                        continue;

                    Transform t = piece.transform;
                    if (t != null)
                        AddEntry(t.position, so, kind, origin, 1);
                }
            }

            // Do NOT merge separate veins — every deposit must stay visible.
            if (_entries.Count > 1)
                _entries.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        }

        private bool HasAnyOreSelected()
        {
            foreach (string id in _selectedIds)
            {
                if (id != IdScrap && id != IdAntique)
                    return true;
            }
            return false;
        }

        private enum TargetKind { Unknown, Ore, Scrap, Antique }

        private static TargetKind Classify(T_ItemSO so, bool isMystery, bool isNode)
        {
            if (so == null)
                return TargetKind.Unknown;
            if (so.Type == PickupType.Scrap || (isMystery && so.mysteryType == MysteryItemType.Scrap))
                return TargetKind.Scrap;
            if (so.Type == PickupType.Antique || (isMystery && so.mysteryType == MysteryItemType.Antique))
                return TargetKind.Antique;
            if (isNode || so.isNode || so.Type == PickupType.Ore)
                return TargetKind.Ore;
            return TargetKind.Unknown;
        }

        private bool IsWanted(TargetKind kind, T_ItemSO so, bool wantScrap, bool wantAntique)
        {
            if (kind == TargetKind.Scrap)
                return wantScrap;
            if (kind == TargetKind.Antique)
                return wantAntique;
            if (kind != TargetKind.Ore || so == null)
                return false;

            string id = so.GetItemID();
            return !string.IsNullOrEmpty(id) && _selectedIds.Contains(id);
        }

        private bool AddEntry(Vector3 worldPos, T_ItemSO so, TargetKind kind, Vector3 origin, int count)
        {
            float dx = worldPos.x - origin.x;
            float dy = worldPos.y - origin.y;
            float dz = worldPos.z - origin.z;
            float dist = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
            string label = GetLabel(so, kind);
            _entries.Add(new EspEntry
            {
                WorldPos = worldPos,
                Label = label,
                Color = ColorForItem(so, kind == TargetKind.Scrap, kind == TargetKind.Antique),
                Distance = dist,
                Count = Math.Max(1, count)
            });
            return true;
        }

        private string GetLabel(T_ItemSO so, TargetKind kind)
        {
            if (so != null)
                return GetDisplayName(so.Name, so.GetItemID());
            if (kind == TargetKind.Scrap)
                return GetDisplayName("Item_ScrapName", IdScrap);
            if (kind == TargetKind.Antique)
                return GetDisplayName("Item_AntiqueName", IdAntique);
            return "?";
        }

        private string GetDisplayName(string nameKey, string fallbackId)
        {
            string cacheKey = (nameKey ?? "") + "|" + _langMode + "|" + (_russian ? "1" : "0");
            if (_nameCache.TryGetValue(cacheKey, out string cached))
                return cached;

            string result = ResolveDisplayName(nameKey, fallbackId);
            _nameCache[cacheKey] = result;
            return result;
        }

        private string ResolveDisplayName(string nameKey, string fallbackId)
        {
            if (fallbackId == IdScrap)
                nameKey = "Item_ScrapName";
            else if (fallbackId == IdAntique)
                nameKey = "Item_AntiqueName";

            if (string.IsNullOrEmpty(nameKey))
                nameKey = fallbackId;
            if (string.IsNullOrEmpty(nameKey))
                return "?";

            // I2 Localization (game strings like Item_BronzeName)
            try
            {
                LocalizationManager.InitializeIfNeeded();
                string translated = LocalizationManager.GetTranslation(nameKey);
                if (IsValidTranslation(nameKey, translated))
                {
                    return translated;
                }
            }
            catch { }

            // Manual fallback for keys and English names
            return FallbackTranslate(nameKey);
        }

        private static bool IsValidTranslation(string key, string translated)
        {
            if (string.IsNullOrEmpty(translated))
                return false;
            if (translated == key)
                return false;
            if (translated.StartsWith("Item_") && translated.EndsWith("Name"))
                return false;
            return true;
        }

        private string FallbackTranslate(string nameKey)
        {
            if (string.IsNullOrEmpty(nameKey))
                return "?";

            if (FallbackNames.TryGetValue(nameKey, out LocalizedPair direct))
                return _russian ? direct.ru : direct.en;

            string lower = nameKey.ToLowerInvariant();
            if (FallbackNames.TryGetValue(lower, out direct))
                return _russian ? direct.ru : direct.en;

            if (lower.EndsWith("name") && lower.StartsWith("item_"))
            {
                string core = lower.Substring(5, lower.Length - 9);
                if (FallbackNames.TryGetValue(core, out direct))
                    return _russian ? direct.ru : direct.en;
            }

            // Plain English resource name
            if (_russian)
            {
                foreach (var kv in FallbackNames)
                {
                    if (kv.Key.Length >= 4 && lower.Contains(kv.Key))
                        return kv.Value.ru;
                }
            }

            return BeautifyKey(nameKey);
        }

        private static string BeautifyKey(string key)
        {
            if (key.StartsWith("Item_") && key.EndsWith("Name"))
                key = key.Substring(5, key.Length - 9);
            return key.Replace('_', ' ');
        }

        private T_Item[] GetCachedItems()
        {
            if (_cachedItems != null && Time.unscaledTime - _cachedItemsTime < _itemCacheTtl)
                return _cachedItems;

            try
            {
                _cachedItems = UnityEngine.Object.FindObjectsOfType<T_Item>(true) ?? Array.Empty<T_Item>();
                _cachedItemsTime = Time.unscaledTime;
            }
            catch
            {
                _cachedItems = Array.Empty<T_Item>();
            }
            return _cachedItems;
        }

        private T_NodePiece[] GetCachedPieces()
        {
            if (_cachedPieces != null && Time.unscaledTime - _cachedItemsTime < _itemCacheTtl)
                return _cachedPieces;

            try
            {
                _cachedPieces = UnityEngine.Object.FindObjectsOfType<T_NodePiece>(true) ?? Array.Empty<T_NodePiece>();
            }
            catch
            {
                _cachedPieces = Array.Empty<T_NodePiece>();
            }
            return _cachedPieces;
        }

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

        private static Color ColorForItem(T_ItemSO so, bool isScrap, bool isAntique)
        {
            if (isScrap) return new Color(0.7f, 0.72f, 0.78f);
            if (isAntique) return new Color(0.95f, 0.78f, 0.35f);
            return ColorForName(so != null ? so.Name : "");
        }

        private static Color ColorForName(string name)
        {
            string n = (name ?? string.Empty).ToLowerInvariant();
            if (n.Contains("bronze")) return new Color(0.8f, 0.5f, 0.25f);
            if (n.Contains("steel")) return new Color(0.7f, 0.75f, 0.8f);
            if (n.Contains("titanium")) return new Color(0.75f, 0.8f, 0.85f);
            if (n.Contains("iron")) return new Color(0.85f, 0.4f, 0.25f);
            if (n.Contains("copper")) return new Color(1f, 0.55f, 0.2f);
            if (n.Contains("coal")) return new Color(0.55f, 0.55f, 0.55f);
            if (n.Contains("gold")) return new Color(1f, 0.85f, 0.15f);
            if (n.Contains("silver")) return new Color(0.85f, 0.9f, 0.95f);
            if (n.Contains("scrap")) return new Color(0.7f, 0.72f, 0.78f);
            if (n.Contains("antique")) return new Color(0.95f, 0.78f, 0.35f);
            if (n.Contains("diamond")) return new Color(0.5f, 0.85f, 1f);
            if (n.Contains("uranium")) return new Color(0.45f, 1f, 0.3f);
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
            if (_pixel == null) _pixel = MakeTex(Color.white);
            if (_panelBg == null) _panelBg = MakeTex(new Color(0f, 0f, 0f, 0.75f));
            if (_rowOn == null) _rowOn = MakeTex(new Color(0.12f, 0.45f, 0.2f, 0.9f));
            if (_rowOff == null) _rowOff = MakeTex(new Color(0.15f, 0.15f, 0.15f, 0.9f));
            if (_rowHi == null) _rowHi = MakeTex(new Color(0.2f, 0.35f, 0.55f, 0.95f));
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

        private struct LocalizedPair { public string ru; public string en; }

        private static readonly Dictionary<string, LocalizedPair> FallbackNames = new Dictionary<string, LocalizedPair>(StringComparer.OrdinalIgnoreCase)
        {
            { "Item_BronzeName", new LocalizedPair { ru = "Бронза", en = "Bronze" } },
            { "Item_SteelName", new LocalizedPair { ru = "Сталь", en = "Steel" } },
            { "Item_TitaniumName", new LocalizedPair { ru = "Титан", en = "Titanium" } },
            { "Item_ScrapName", new LocalizedPair { ru = "Лом", en = "Scrap" } },
            { "Item_AntiqueName", new LocalizedPair { ru = "Антиквариат", en = "Antique" } },
            { "bronze", new LocalizedPair { ru = "Бронза", en = "Bronze" } },
            { "steel", new LocalizedPair { ru = "Сталь", en = "Steel" } },
            { "titanium", new LocalizedPair { ru = "Титан", en = "Titanium" } },
            { "iron", new LocalizedPair { ru = "Железо", en = "Iron" } },
            { "copper", new LocalizedPair { ru = "Медь", en = "Copper" } },
            { "coal", new LocalizedPair { ru = "Уголь", en = "Coal" } },
            { "gold", new LocalizedPair { ru = "Золото", en = "Gold" } },
            { "silver", new LocalizedPair { ru = "Серебро", en = "Silver" } },
            { "quartz", new LocalizedPair { ru = "Кварц", en = "Quartz" } },
            { "sulfur", new LocalizedPair { ru = "Сера", en = "Sulfur" } },
            { "clay", new LocalizedPair { ru = "Глина", en = "Clay" } },
            { "stone", new LocalizedPair { ru = "Камень", en = "Stone" } },
            { "limestone", new LocalizedPair { ru = "Известняк", en = "Limestone" } },
            { "sandstone", new LocalizedPair { ru = "Песчаник", en = "Sandstone" } },
            { "diamond", new LocalizedPair { ru = "Алмаз", en = "Diamond" } },
            { "platinum", new LocalizedPair { ru = "Платина", en = "Platinum" } },
            { "uranium", new LocalizedPair { ru = "Уран", en = "Uranium" } },
            { "scrap", new LocalizedPair { ru = "Лом", en = "Scrap" } },
            { "antique", new LocalizedPair { ru = "Антиквариат", en = "Antique" } },
        };
    }
}
