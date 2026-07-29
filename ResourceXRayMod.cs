using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using Il2Cpp;
using Il2CppI2.Loc;

[assembly: MelonInfo(typeof(OFSResourceXRay.ResourceXRayMod), "Resource X-Ray", "1.7.0", "G3ntEZ")]
[assembly: MelonGame("threeW", "Ore Factory Squad")]

namespace OFSResourceXRay
{
    public class ResourceXRayMod : MelonMod
    {
        private const string IdScrap = "__scrap__";
        private const string IdAntique = "__antique__";
        private const string DonationUrl = "https://www.donationalerts.com/r/g3ntez";
        private const string BrandCredit = "by G3ntEZ";
        private const float FlySpeed = 18f;
        private const float FlyFastMult = 3.5f;

        private MelonPreferences_Category _prefs;
        private MelonPreferences_Entry<string> _selectedPrefs;
        private MelonPreferences_Entry<bool> _espEnabledPrefs;
        private MelonPreferences_Entry<float> _maxDistancePrefs;
        private MelonPreferences_Entry<string> _langPrefs;
        private MelonPreferences_Entry<bool> _lowPerfPrefs;
        private MelonPreferences_Entry<int> _maxMarkersPrefs;

        private bool _espEnabled = true;
        private bool _menuOpen;
        private bool _menuShowHelp;
        private bool _flyEnabled;
        private bool _lowPerf;
        private bool _cursorUnlockedByMenu;
        private CursorLockMode _prevCursorLock;
        private bool _prevCursorVisible;

        private Camera _flyCam;
        private Transform _flyCamParent;
        private Vector3 _flyCamLocalPos;
        private Quaternion _flyCamLocalRot;
        private Transform _flyBody;
        private CharacterController _flyCc;
        private bool _flyCcWasEnabled;
        private Rigidbody _flyRb;
        private bool _flyRbHadGravity;
        private bool _flyRbWasKinematic;
        private Vector3 _flyPendingMove;
        private Vector3 _flyDesiredCamPos;
        private readonly List<Collider> _flyDisabledColliders = new List<Collider>(32);
        private readonly List<bool> _flyColliderWasEnabled = new List<bool>(32);
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
        private GUIStyle _titleStyle;
        private GUIStyle _darkTextStyle;
        private GUIStyle _darkSmallStyle;
        private GUIStyle _brandStyle;
        private Texture2D _pixel;
        private Texture2D _panelBg;
        private Texture2D _panelAccent;
        private Texture2D _chipBg;
        private Texture2D _btnBg;
        private Texture2D _btnAccent;
        private Texture2D _donateBg;
        private Texture2D _rowOn;
        private Texture2D _rowOff;
        private Texture2D _rowHi;
        private GUIStyle _fillStyle;
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
            // Always show the whole map - ignore old saved low distance/marker caps.
            _maxDistancePrefs.Value = 99999f;
            _maxMarkersPrefs.Value = 9999;
            _lowPerf = _lowPerfPrefs.Value;
            _langMode = NormalizeLangMode(_langPrefs.Value);
            ApplyLanguageFromMode(forceLog: false);
            ApplyPerformanceSettings();
            LoadSelectedFromPrefs();

            LoggerInstance.Msg(_russian
                ? "Resource X-Ray v1.7.0 | F8 меню | F10 инструкция | F3 полёт | by G3ntEZ"
                : "Resource X-Ray v1.7.0 | F8 menu | F10 help | F3 fly | by G3ntEZ");
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
                        _menuShowHelp = false;
                        _nameCache.Clear();
                        RefreshOreCatalog(force: true);
                        ClampMenuIndex();
                        UnlockMenuCursor();
                    }
                    else
                    {
                        _menuShowHelp = false;
                        RestoreMenuCursor();
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

                if (WasPressed(Key.F3))
                    SetFlyEnabled(!_flyEnabled);

                if (WasPressed(Key.U))
                    ToggleManualMarker();
                if (WasPressed(Key.I))
                    ClearAllManualMarkers();
                if (WasPressed(Key.F9))
                    ForceUnlockVehiclePurchase();

                if (WasPressed(Key.F10))
                {
                    if (!_menuOpen)
                    {
                        _menuOpen = true;
                        UnlockMenuCursor();
                        _nameCache.Clear();
                        RefreshOreCatalog(force: true);
                    }
                    _menuShowHelp = !_menuShowHelp;
                }

                if (_menuOpen && WasPressed(Key.Escape))
                {
                    if (_menuShowHelp)
                        _menuShowHelp = false;
                    else
                        CloseMenu();
                }

                if (_menuOpen)
                {
                    UnlockMenuCursor();
                    HandleMenuInput();
                }
                else if (_cursorUnlockedByMenu)
                {
                    RestoreMenuCursor();
                }

                if (_flyEnabled)
                    UpdateFlyInput();

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

        public override void OnLateUpdate()
        {
            try
            {
                if (_flyEnabled)
                    ApplyFlyMovement();
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"OnLateUpdate fly failed: {ex}");
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
            Mouse mouse = Mouse.current;

            if (!_menuShowHelp)
            {
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

                if (mouse != null)
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    if (scroll > 0.01f)
                    {
                        _menuIndex--;
                        ClampMenuIndex();
                    }
                    else if (scroll < -0.01f)
                    {
                        _menuIndex++;
                        ClampMenuIndex();
                    }
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
            }

            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                Vector2 sp = mouse.position.ReadValue();
                Vector2 gui = new Vector2(sp.x, Screen.height - sp.y);

                if (_menuShowHelp)
                {
                    if (_backRect.Contains(gui))
                        _menuShowHelp = false;
                    else if (_closeRect.Contains(gui))
                        CloseMenu();
                    else if (_donateRect.Contains(gui))
                        OpenDonationPage();
                    return;
                }

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
                    CloseMenu();
                }
                else if (_langRect.Contains(gui))
                {
                    ToggleLanguage();
                }
                else if (_helpBtnRect.Contains(gui))
                {
                    _menuShowHelp = true;
                }
                else if (_donateRect.Contains(gui))
                {
                    OpenDonationPage();
                }
            }
        }

        private void CloseMenu()
        {
            _menuOpen = false;
            _menuShowHelp = false;
            RestoreMenuCursor();
        }

        private void UnlockMenuCursor()
        {
            if (!_cursorUnlockedByMenu)
            {
                _prevCursorLock = Cursor.lockState;
                _prevCursorVisible = Cursor.visible;
                _cursorUnlockedByMenu = true;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void RestoreMenuCursor()
        {
            if (!_cursorUnlockedByMenu)
                return;
            Cursor.lockState = _prevCursorLock;
            Cursor.visible = _prevCursorVisible;
            _cursorUnlockedByMenu = false;
        }

        private Rect _allOnRect;
        private Rect _allOffRect;
        private Rect _closeRect;
        private Rect _langRect;
        private Rect _helpBtnRect;
        private Rect _donateRect;
        private Rect _backRect;

        private void OpenDonationPage()
        {
            try
            {
                Application.OpenURL(DonationUrl);
                LoggerInstance.Msg(T("Открываю DonationAlerts…", "Opening DonationAlerts…"));
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Donate open failed: {ex.Message}");
            }
        }

        private void ToggleLanguage()
        {
            if (_langMode == "auto") _langMode = "ru";
            else if (_langMode == "ru") _langMode = "en";
            else _langMode = "auto";

            _langPrefs.Value = _langMode;
            MelonPreferences.Save();
            _nameCache.Clear();
            ApplyLanguageFromMode(forceLog: true);
            ResortOreOptions();
            _entries.Clear();
            _nextRefresh = 0f;
        }

        private static string NormalizeLangMode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "auto";
            value = value.Trim().ToLowerInvariant();
            if (value == "ru" || value == "russian" || value == "СЂСѓСЃ" || value == "СЂСѓСЃСЃРєРёР№")
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
                    ? (_russian
                        ? "\u0410\u0432\u0442\u043e \u2192 \u0440\u0443\u0441\u0441\u043a\u0438\u0439 (\u043a\u0430\u043a \u0432 \u0438\u0433\u0440\u0435)"
                        : "Auto \u2192 English (game language)")
                    : (_russian ? "\u0420\u0443\u0441\u0441\u043a\u0438\u0439 (\u0432\u0440\u0443\u0447\u043d\u0443\u044e)" : "English (manual)");
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
                    if (lang.Contains("russ") || lang.Contains("СЂСѓСЃ")) return true;
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
            string perf = _lowPerf ? T("ЭКОН", "LOW") : "";
            string fly = _flyEnabled ? T("ПОЛЁТ", "FLY") : "";
            string status = _espEnabled
                ? T(
                    $"ВКЛ · {_selectedIds.Count} руд · {_entries.Count} меток · U:{_manualMarkers.Count}",
                    $"ON · {_selectedIds.Count} ores · {_entries.Count} marks · U:{_manualMarkers.Count}")
                : T("Открыть меню F8", "Open menu F8");

            float chipH = 22f;
            float padX = 12f;
            float gap = 8f;
            float brandW = 92f;
            float statusW = _espEnabled ? 310f : 160f;
            float flyW = string.IsNullOrEmpty(fly) ? 0f : 70f;
            float perfW = string.IsNullOrEmpty(perf) ? 0f : 60f;

            float contentW = padX + statusW;
            if (flyW > 0f) contentW += gap + flyW;
            if (perfW > 0f) contentW += gap + perfW;
            contentW += gap + brandW + padX;

            Rect bar = new Rect(14f, 12f, contentW, 34f);
            SafeDrawTexture(bar, _panelBg);
            SafeDrawTexture(new Rect(bar.x, bar.y, 5f, bar.height), _panelAccent);

            float chipY = bar.y + 6f;
            float x = bar.x + padX;
            DrawTextChip(new Rect(x, chipY, statusW, chipH), status);
            x += statusW + gap;
            if (flyW > 0f)
            {
                DrawTextChip(new Rect(x, chipY, flyW, chipH), fly);
                x += flyW + gap;
            }
            if (perfW > 0f)
            {
                DrawTextChip(new Rect(x, chipY, perfW, chipH), perf);
                x += perfW + gap;
            }
            DrawBrandLabel(new Rect(x, chipY, brandW, chipH));
        }

        private void DrawMenu()
        {
            float w = 580f;
            float h = _menuShowHelp ? 640f : 118f + VisibleRows * RowH + 36f;
            Rect panel = new Rect(18f, 52f, w, h);

            SafeDrawTexture(panel, _panelBg);
            SafeDrawTexture(new Rect(panel.x, panel.y, panel.width, 4f), _panelAccent);
            SafeDrawTexture(new Rect(panel.x, panel.y, 6f, panel.height), _panelAccent);

            float headerY = panel.y + 14f;
            float headerH = 26f;
            float pad = 16f;
            float gap = 8f;

            if (_menuShowHelp)
            {
                DrawTextChip(new Rect(panel.x + pad, headerY, 300f, headerH),
                    T("Инструкция · Resource X-Ray", "Help · Resource X-Ray"));
                DrawTextChip(new Rect(panel.x + pad + 308f, headerY, 70f, headerH), "v1.7.0");
                DrawBrandLabel(new Rect(panel.x + pad + 386f, headerY, 100f, headerH));

                float hy = panel.y + 50f;
                float helpBtnW = (panel.width - pad * 2f - gap * 2f) / 3f;
                _backRect = new Rect(panel.x + pad, hy, helpBtnW, 28f);
                _donateRect = new Rect(panel.x + pad + helpBtnW + gap, hy, helpBtnW, 28f);
                _closeRect = new Rect(panel.x + pad + 2f * (helpBtnW + gap), hy, helpBtnW, 28f);
                DrawUiButton(_backRect, T("Назад", "Back"), false);
                DrawDonateButton(_donateRect);
                DrawUiButton(_closeRect, T("Закрыть", "Close"), false);

                DrawMenuHelpLines(panel, hy + 40f);
                DrawFooter(panel);
                return;
            }

            float hx = panel.x + pad;
            DrawTextChip(new Rect(hx, headerY, 168f, headerH), "Resource X-Ray");
            hx += 168f + gap;
            DrawTextChip(new Rect(hx, headerY, 64f, headerH), "v1.7.0");
            hx += 64f + gap;
            DrawTextChip(new Rect(hx, headerY, 140f, headerH), T("Бесплатный мод", "Free mod"));
            hx += 140f + gap;
            DrawBrandLabel(new Rect(hx, headerY, 100f, headerH));

            float y = panel.y + 50f;
            float btnW = (panel.width - pad * 2f - gap * 5f) / 6f;
            float bx = panel.x + pad;
            _allOnRect = new Rect(bx, y, btnW, 28f);
            bx += btnW + gap;
            _allOffRect = new Rect(bx, y, btnW, 28f);
            bx += btnW + gap;
            _langRect = new Rect(bx, y, btnW, 28f);
            bx += btnW + gap;
            _helpBtnRect = new Rect(bx, y, btnW, 28f);
            bx += btnW + gap;
            _donateRect = new Rect(bx, y, btnW, 28f);
            bx += btnW + gap;
            _closeRect = new Rect(bx, y, btnW, 28f);

            DrawUiButton(_allOnRect, T("Всё ВКЛ", "All ON"), false);
            DrawUiButton(_allOffRect, T("Всё ВЫКЛ", "All OFF"), false);
            DrawUiButton(_langRect, LangButtonLabel(), false);
            DrawUiButton(_helpBtnRect, T("Помощь", "Help"), false);
            DrawDonateButton(_donateRect);
            DrawUiButton(_closeRect, T("Закрыть", "Close"), false);

            y += 40f;
            SafeDrawTexture(new Rect(panel.x + pad, y - 6f, panel.width - pad * 2f, 1f), _panelAccent);
            _clickRects.Clear();

            if (_oreOptions.Count == 0)
            {
                DrawTextChip(new Rect(panel.x + 16f, y + 8f, panel.width - 32f, 44f),
                    T("Цели не найдены. Зайди на участок и нажми F6.",
                      "No targets yet. Enter a dig property and press F6."));
                DrawFooter(panel);
                return;
            }

            int end = Math.Min(_oreOptions.Count, _menuScrollRows + VisibleRows);
            for (int i = _menuScrollRows; i < end; i++)
            {
                OreOption ore = _oreOptions[i];
                bool on = _selectedIds.Contains(ore.Id);
                bool hi = i == _menuIndex;
                Rect row = new Rect(panel.x + 16f, y, panel.width - 32f, RowH - 2f);
                _clickRects.Add(row);
                SafeDrawTexture(row, hi ? _rowHi : (on ? _rowOn : _rowOff));

                Rect chip = new Rect(row.x + 8f, row.y + 4f, row.width - 120f, row.height - 8f);
                SafeDrawTexture(chip, _chipBg);
                string mark = on ? T("ВКЛ", "ON") : T("ВЫКЛ", "OFF");
                string prefix = hi ? "› " : "";
                string displayName = GetDisplayName(ore.NameKey, ore.Id);
                SafeLabel(new Rect(chip.x + 8f, chip.y + 1f, chip.width - 16f, chip.height),
                    $"{prefix}{displayName}", _darkTextStyle);

                Rect badge = new Rect(row.xMax - 100f, row.y + 4f, 88f, row.height - 8f);
                SafeDrawTexture(badge, _chipBg);
                Color prev = GUI.color;
                GUI.color = ore.Color;
                SafeDrawTexture(new Rect(badge.x + 6f, badge.y + 6f, 10f, 10f), _pixel);
                GUI.color = prev;
                SafeLabel(new Rect(badge.x + 22f, badge.y + 1f, 60f, badge.height), mark, _darkSmallStyle);
                y += RowH;
            }

            if (_oreOptions.Count > VisibleRows)
            {
                DrawTextChip(new Rect(panel.x + 16f, panel.yMax - 52f, 220f, 20f),
                    T($"Список {_menuScrollRows + 1}–{end} / {_oreOptions.Count}",
                      $"List {_menuScrollRows + 1}–{end} / {_oreOptions.Count}"));
            }
            DrawFooter(panel);
        }

        private void DrawFooter(Rect panel)
        {
            float pad = 16f;
            float fy = panel.yMax - 28f;
            float fh = 20f;
            SafeDrawTexture(new Rect(panel.x + pad, fy - 6f, panel.width - pad * 2f, 1f), _panelAccent);
            DrawBrandLabel(new Rect(panel.x + pad, fy, 96f, fh));
            DrawTextChip(new Rect(panel.x + pad + 104f, fy, 280f, fh),
                T("F8 меню · F10 помощь · F3 полёт", "F8 menu · F10 help · F3 fly"));
        }

        private void DrawBrandLabel(Rect r)
        {
            // White credit text, vertically centered in the same band as chips.
            if (_brandStyle != null)
                _brandStyle.alignment = TextAnchor.MiddleLeft;
            SafeLabel(r, BrandCredit, _brandStyle);
        }

        private void DrawMenuHelpLines(Rect panel, float startY)
        {
            string[] lines = _russian
                ? new[]
                {
                    "Как пользоваться:",
                    "1) F8 — меню руд",
                    "2) ↑↓ / W S / колесо мыши — выбрать руду",
                    "3) Enter / E / Space — вкл/выкл",
                    "4) 1 — всё вкл, 2 — всё выкл",
                    "5) F7 — рентген вкл/выкл",
                    "6) F6 — обновить список",
                    "7) F4 — перезагрузить метки",
                    "8) F5 — экономный режим",
                    "9) F3 — полёт / noclip",
                    "   WASD + Space/Ctrl, Shift быстрее",
                    "10) U — метка на прицеле",
                    "11) I — очистить все метки U",
                    "12) L — язык Авто / RU / EN",
                    "13) F10 / Помощь — эта инструкция",
                    "",
                    "Метки видны сквозь землю на любой дистанции.",
                    "Одна метка = одна жила (Золото x12).",
                    "",
                    "Обновлений для текущей версии игры больше не будет.",
                    "Поддержать автора — кнопка Донат"
                }
                : new[]
                {
                    "How to use:",
                    "1) F8 — ore menu",
                    "2) Up/Down, W/S or mouse wheel — select",
                    "3) Enter / E / Space — toggle",
                    "4) 1 — all on, 2 — all off",
                    "5) F7 — ESP on/off",
                    "6) F6 — refresh list",
                    "7) F4 — reload markers",
                    "8) F5 — low performance",
                    "9) F3 — fly / noclip",
                    "   WASD + Space/Ctrl, Shift faster",
                    "10) U — crosshair marker",
                    "11) I — clear all U markers",
                    "12) L — language Auto / RU / EN",
                    "13) F10 / Help — this help",
                    "",
                    "Markers show through terrain at any distance.",
                    "One marker = one vein (Gold x12).",
                    "",
                    "No further updates for current game version.",
                    "Support the author — Donate button"
                };

            float y = startY;
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) { y += 10f; continue; }
                DrawTextChip(new Rect(panel.x + 16f, y, panel.width - 32f, 22f), lines[i]);
                y += 24f;
            }
        }

        private void DrawTextChip(Rect r, string text)
        {
            SafeDrawTexture(r, _chipBg);
            SafeLabel(new Rect(r.x + 8f, r.y + 2f, r.width - 12f, r.height - 2f), text, _darkTextStyle);
        }

        private void DrawUiButton(Rect r, string text, bool active)
        {
            SafeDrawTexture(r, active ? _btnAccent : _btnBg);
            SafeDrawTexture(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), _chipBg);
            SafeLabel(new Rect(r.x + 6f, r.y + 4f, r.width - 10f, r.height - 6f), text, _darkSmallStyle);
        }

        private void DrawDonateButton(Rect r)
        {
            SafeDrawTexture(r, _donateBg);
            SafeDrawTexture(new Rect(r.x + 2f, r.y + 2f, r.width - 4f, r.height - 4f), _chipBg);
            SafeLabel(new Rect(r.x + 6f, r.y + 4f, r.width - 10f, r.height - 6f),
                T("Донат", "Donate"), _darkSmallStyle);
        }

        private void DrawFakeButton(Rect r, string text, bool active)
        {
            DrawUiButton(r, text, active);
        }
        private void SetFlyEnabled(bool enabled)
        {
            if (enabled == _flyEnabled)
                return;

            if (enabled)
            {
                if (!TryBeginFly())
                {
                    LoggerInstance.Warning(T("F3: не найдена камера для полёта.",
                                            "F3: no camera found for fly mode."));
                    return;
                }
                _flyEnabled = true;
                LoggerInstance.Msg(T(
                    $"Полёт/noclip ВКЛ (F3). WASD/стрелки + Space/Ctrl, Shift быстрее. Камера: {_flyCam.name}",
                    $"Fly/noclip ON (F3). WASD/arrows + Space/Ctrl, Shift faster. Cam: {_flyCam.name}"));
            }
            else
            {
                EndFly();
                _flyEnabled = false;
                LoggerInstance.Msg(T("Полёт/noclip ВЫКЛ.", "Fly/noclip OFF."));
            }
        }

        private bool TryBeginFly()
        {
            Camera cam = GetCamera();
            if (cam == null)
                return false;

            _flyCam = cam;
            Transform camT = cam.transform;
            _flyCamParent = camT.parent;
            _flyCamLocalPos = camT.localPosition;
            _flyCamLocalRot = camT.localRotation;

            // Detach camera so game body sync cannot pull the view back.
            try { camT.SetParent(null, true); } catch { }

            _flyBody = FindNearbyMoveBody(camT.position);
            _flyCc = null;
            _flyRb = null;
            _flyDisabledColliders.Clear();
            _flyColliderWasEnabled.Clear();
            _flyPendingMove = Vector3.zero;
            _flyDesiredCamPos = camT.position;

            if (_flyBody != null)
            {
                try
                {
                    CharacterController cc = _flyBody.GetComponent<CharacterController>();
                    if (cc == null)
                        cc = _flyBody.GetComponentInChildren<CharacterController>();
                    if (cc != null)
                    {
                        _flyCc = cc;
                        _flyCcWasEnabled = cc.enabled;
                        cc.enabled = false;
                    }
                }
                catch { }

                try
                {
                    Rigidbody rb = _flyBody.GetComponent<Rigidbody>();
                    if (rb == null)
                        rb = _flyBody.GetComponentInChildren<Rigidbody>();
                    if (rb != null)
                    {
                        _flyRb = rb;
                        _flyRbHadGravity = rb.useGravity;
                        _flyRbWasKinematic = rb.isKinematic;
                        rb.useGravity = false;
                        rb.isKinematic = true;
                        try { rb.velocity = Vector3.zero; } catch { }
                        try { rb.angularVelocity = Vector3.zero; } catch { }
                    }
                }
                catch { }

                try
                {
                    Collider[] cols = _flyBody.GetComponentsInChildren<Collider>(true);
                    if (cols != null)
                    {
                        for (int i = 0; i < cols.Length; i++)
                        {
                            Collider c = cols[i];
                            if (c == null) continue;
                            _flyDisabledColliders.Add(c);
                            _flyColliderWasEnabled.Add(c.enabled);
                            c.enabled = false;
                        }
                    }
                }
                catch { }
            }

            return true;
        }

        private void EndFly()
        {
            try
            {
                for (int i = 0; i < _flyDisabledColliders.Count; i++)
                {
                    Collider c = _flyDisabledColliders[i];
                    if (c == null) continue;
                    c.enabled = _flyColliderWasEnabled[i];
                }
            }
            catch { }

            try
            {
                if (_flyCc != null)
                    _flyCc.enabled = _flyCcWasEnabled;
            }
            catch { }

            try
            {
                if (_flyRb != null)
                {
                    _flyRb.isKinematic = _flyRbWasKinematic;
                    _flyRb.useGravity = _flyRbHadGravity;
                }
            }
            catch { }

            try
            {
                if (_flyCam != null)
                {
                    Transform camT = _flyCam.transform;
                    if (_flyCamParent != null)
                    {
                        camT.SetParent(_flyCamParent, true);
                        camT.localPosition = _flyCamLocalPos;
                        camT.localRotation = _flyCamLocalRot;
                    }
                }
            }
            catch { }

            _flyDisabledColliders.Clear();
            _flyColliderWasEnabled.Clear();
            _flyCc = null;
            _flyRb = null;
            _flyBody = null;
            _flyCam = null;
            _flyCamParent = null;
            _flyPendingMove = Vector3.zero;
        }

        private void UpdateFlyInput()
        {
            if (_menuOpen)
            {
                _flyPendingMove = Vector3.zero;
                return;
            }

            Camera cam = _flyCam != null ? _flyCam : GetCamera();
            if (cam == null)
                return;

            Vector3 move = Vector3.zero;
            Transform t = cam.transform;

            // New Input System
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (IsHeld(kb, Key.W) || IsHeld(kb, Key.UpArrow)) move += t.forward;
                if (IsHeld(kb, Key.S) || IsHeld(kb, Key.DownArrow)) move -= t.forward;
                if (IsHeld(kb, Key.A) || IsHeld(kb, Key.LeftArrow)) move -= t.right;
                if (IsHeld(kb, Key.D) || IsHeld(kb, Key.RightArrow)) move += t.right;
                if (IsHeld(kb, Key.Space)) move += Vector3.up;
                if (IsHeld(kb, Key.LeftCtrl) || IsHeld(kb, Key.RightCtrl) || IsHeld(kb, Key.C))
                    move += Vector3.down;
            }

            // Legacy Input fallback (some builds swallow InputSystem keys)
            try
            {
                float hx = Input.GetAxisRaw("Horizontal");
                float hy = Input.GetAxisRaw("Vertical");
                if (Mathf.Abs(hx) > 0.01f) move += t.right * hx;
                if (Mathf.Abs(hy) > 0.01f) move += t.forward * hy;
                if (Input.GetKey(KeyCode.Space)) move += Vector3.up;
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.C))
                    move += Vector3.down;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) move += t.forward;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) move -= t.forward;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) move -= t.right;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) move += t.right;
            }
            catch { }

            if (move.sqrMagnitude > 0.0001f)
            {
                move.Normalize();
                bool fast = false;
                if (kb != null)
                    fast = IsHeld(kb, Key.LeftShift) || IsHeld(kb, Key.RightShift);
                try { fast = fast || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); } catch { }
                float speed = FlySpeed * (fast ? FlyFastMult : 1f);
                _flyPendingMove = move * speed;
            }
            else
            {
                _flyPendingMove = Vector3.zero;
            }
        }

        private void ApplyFlyMovement()
        {
            if (_flyCam == null)
            {
                _flyCam = GetCamera();
                if (_flyCam == null)
                    return;
                _flyDesiredCamPos = _flyCam.transform.position;
            }

            Transform camT = _flyCam.transform;

            // Game may re-parent camera each frame вЂ” keep it free while flying.
            try
            {
                if (camT.parent != null)
                    camT.SetParent(null, true);
            }
            catch { }

            if (_flyPendingMove.sqrMagnitude > 0.0001f)
                _flyDesiredCamPos += _flyPendingMove * Time.unscaledDeltaTime;

            camT.position = _flyDesiredCamPos;

            if (_flyBody != null)
            {
                try
                {
                    Vector3 bodyPos = _flyDesiredCamPos - camT.up * 1.6f;
                    if (_flyRb != null && !_flyRb.isKinematic)
                    {
                        _flyRb.MovePosition(bodyPos);
                    }
                    else
                    {
                        _flyBody.position = bodyPos;
                    }
                }
                catch { }
            }
        }

        private static bool IsHeld(Keyboard kb, Key key)
        {
            try
            {
                var control = kb[key];
                return control != null && control.isPressed;
            }
            catch
            {
                return false;
            }
        }

        private static Transform FindNearbyMoveBody(Vector3 nearPos)
        {
            try
            {
                CharacterController[] ccs = UnityEngine.Object.FindObjectsOfType<CharacterController>();
                Transform best = null;
                float bestDist = 8f * 8f;
                if (ccs != null)
                {
                    for (int i = 0; i < ccs.Length; i++)
                    {
                        CharacterController cc = ccs[i];
                        if (cc == null || !cc.enabled) continue;
                        float d = (cc.transform.position - nearPos).sqrMagnitude;
                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = cc.transform;
                        }
                    }
                }
                if (best != null)
                    return best;
            }
            catch { }

            try
            {
                Rigidbody[] rbs = UnityEngine.Object.FindObjectsOfType<Rigidbody>();
                Transform best = null;
                float bestDist = 8f * 8f;
                if (rbs != null)
                {
                    for (int i = 0; i < rbs.Length; i++)
                    {
                        Rigidbody rb = rbs[i];
                        if (rb == null) continue;
                        float d = (rb.transform.position - nearPos).sqrMagnitude;
                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = rb.transform;
                        }
                    }
                }
                return best;
            }
            catch
            {
                return null;
            }
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

            Vector3 placePos = GetCrosshairWorldPoint(cam);

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

            if (nearest >= 0 && nearestSq <= 9f)
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
                Label = T($"РњРµС‚РєР° #{index}", $"Marker #{index}")
            });
            LoggerInstance.Msg(T($"Поставлена метка #{index}", $"Placed marker #{index}"));
        }

        private static Vector3 GetCrosshairWorldPoint(Camera cam)
        {
            Ray ray = cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            const float maxDist = 500f;

            if (Physics.Raycast(ray.origin, ray.direction, out RaycastHit hit, maxDist))
                return hit.point + hit.normal * 0.02f;

            return ray.origin + ray.direction * 12f;
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
            try
            {
                Color prev = GUI.color;
                GUI.color = Color.white;
                GUI.Label(r, text ?? "", style ?? GUI.skin.label);
                GUI.color = prev;
            }
            catch
            {
                try { GUI.Label(r, text ?? ""); } catch { }
            }
        }

        private void SafeDrawTexture(Rect r, Texture2D tex)
        {
            if (tex == null || r.width < 1f || r.height < 1f)
                return;
            Color prev = GUI.color;
            GUI.color = Color.white;
            try
            {
                GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true);
            }
            catch
            {
                try
                {
                    if (_fillStyle == null)
                    {
                        _fillStyle = new GUIStyle();
                        _fillStyle.border = new RectOffset(0, 0, 0, 0);
                        _fillStyle.margin = new RectOffset(0, 0, 0, 0);
                        _fillStyle.padding = new RectOffset(0, 0, 0, 0);
                    }
                    _fillStyle.normal.background = tex;
                    GUI.Box(r, GUIContent.none, _fillStyle);
                }
                catch { }
            }
            GUI.color = prev;
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

            ResortOreOptions();
            ClampMenuIndex();
        }

        private void ResortOreOptions()
        {
            if (_oreOptions.Count <= 1)
                return;
            _oreOptions.Sort((a, b) =>
            {
                if (a.IsCategory != b.IsCategory)
                    return a.IsCategory ? -1 : 1;
                return string.Compare(GetDisplayName(a.NameKey, a.Id), GetDisplayName(b.NameKey, b.Id), StringComparison.OrdinalIgnoreCase);
            });
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

                // One label per ore vein / item вЂ” not per rock piece.
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

            // Hidden collect nodes (antiques inside rocks) вЂ” one marker per parent item
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

            // Do NOT merge separate veins вЂ” every deposit must stay visible.
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

            // Manual RU/EN: use our bilingual table so ore names match the menu language.
            // Game I2 always follows the game language and ignores the mod toggle.
            if (_langMode == "en" || _langMode == "ru")
                return FallbackTranslate(nameKey);

            try
            {
                LocalizationManager.InitializeIfNeeded();
                string translated = LocalizationManager.GetTranslation(nameKey);
                if (IsValidTranslation(nameKey, translated))
                    return translated;
            }
            catch { }

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

            // Plain English resource name → Russian
            if (_russian)
            {
                foreach (var kv in FallbackNames)
                {
                    if (kv.Key.Length >= 4 && lower.Contains(kv.Key))
                        return kv.Value.ru;
                }
            }
            else
            {
                // Already-Russian label → English
                foreach (var kv in FallbackNames)
                {
                    if (!string.IsNullOrEmpty(kv.Value.ru) &&
                        string.Equals(kv.Value.ru, nameKey, StringComparison.OrdinalIgnoreCase))
                        return kv.Value.en;
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
            Color black = new Color(0.08f, 0.06f, 0.12f, 1f);
            Color purpleDeep = new Color(0.18f, 0.10f, 0.32f, 0.94f);
            Color purpleAccent = new Color(0.62f, 0.38f, 0.95f, 1f);
            Color purpleRowOff = new Color(0.28f, 0.16f, 0.45f, 0.92f);
            Color purpleRowOn = new Color(0.36f, 0.22f, 0.58f, 0.95f);
            Color purpleRowHi = new Color(0.48f, 0.30f, 0.78f, 0.98f);
            Color btnPurple = new Color(0.42f, 0.24f, 0.68f, 1f);
            Color donatePink = new Color(0.72f, 0.28f, 0.70f, 1f);

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
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle { fontSize = 16, fontStyle = FontStyle.Bold };
                _titleStyle.normal.textColor = black;
            }
            if (_darkTextStyle == null)
            {
                _darkTextStyle = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold };
                _darkTextStyle.normal.textColor = black;
                _darkTextStyle.alignment = TextAnchor.MiddleLeft;
                _darkTextStyle.clipping = TextClipping.Clip;
            }
            if (_darkSmallStyle == null)
            {
                _darkSmallStyle = new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold };
                _darkSmallStyle.normal.textColor = black;
                _darkSmallStyle.alignment = TextAnchor.MiddleLeft;
                _darkSmallStyle.clipping = TextClipping.Clip;
            }
            if (_brandStyle == null)
            {
                _brandStyle = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold };
                _brandStyle.normal.textColor = Color.white;
                _brandStyle.alignment = TextAnchor.MiddleCenter;
                _brandStyle.clipping = TextClipping.Clip;
                _brandStyle.padding = new RectOffset(0, 0, 0, 0);
                _brandStyle.margin = new RectOffset(0, 0, 0, 0);
            }

            if (_pixel == null) _pixel = MakeTex(Color.white);
            if (_panelBg == null) _panelBg = MakeTex(new Color(0.20f, 0.10f, 0.36f, 1f));
            if (_panelAccent == null) _panelAccent = MakeTex(new Color(0.70f, 0.45f, 1f, 1f));
            if (_chipBg == null) _chipBg = MakeTex(new Color(1f, 1f, 1f, 1f));
            if (_btnBg == null) _btnBg = MakeTex(new Color(0.45f, 0.25f, 0.75f, 1f));
            if (_btnAccent == null) _btnAccent = MakeTex(new Color(0.70f, 0.45f, 1f, 1f));
            if (_donateBg == null) _donateBg = MakeTex(new Color(0.85f, 0.30f, 0.75f, 1f));
            if (_rowOn == null) _rowOn = MakeTex(new Color(0.40f, 0.24f, 0.62f, 1f));
            if (_rowOff == null) _rowOff = MakeTex(new Color(0.30f, 0.16f, 0.48f, 1f));
            if (_rowHi == null) _rowHi = MakeTex(new Color(0.55f, 0.35f, 0.88f, 1f));
            if (_fillStyle == null) _fillStyle = new GUIStyle();
        }

        private static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Point;
            t.hideFlags = HideFlags.HideAndDontSave;
            Color[] px = { c, c, c, c };
            t.SetPixels(px);
            t.Apply(false, false);
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
            { "Item_BronzeName", L("\u0411\u0440\u043e\u043d\u0437\u0430", "Bronze") },
            { "Item_SteelName", L("\u0421\u0442\u0430\u043b\u044c", "Steel") },
            { "Item_TitaniumName", L("\u0422\u0438\u0442\u0430\u043d", "Titanium") },
            { "Item_IronName", L("\u0416\u0435\u043b\u0435\u0437\u043e", "Iron") },
            { "Item_CopperName", L("\u041c\u0435\u0434\u044c", "Copper") },
            { "Item_CoalName", L("\u0423\u0433\u043e\u043b\u044c", "Coal") },
            { "Item_GoldName", L("\u0417\u043e\u043b\u043e\u0442\u043e", "Gold") },
            { "Item_SilverName", L("\u0421\u0435\u0440\u0435\u0431\u0440\u043e", "Silver") },
            { "Item_ClayName", L("\u0413\u043b\u0438\u043d\u0430", "Clay") },
            { "Item_StoneName", L("\u041a\u0430\u043c\u0435\u043d\u044c", "Stone") },
            { "Item_LimestoneName", L("\u0418\u0437\u0432\u0435\u0441\u0442\u043d\u044f\u043a", "Limestone") },
            { "Item_SandstoneName", L("\u041f\u0435\u0441\u0447\u0430\u043d\u0438\u043a", "Sandstone") },
            { "Item_DiamondName", L("\u0410\u043b\u043c\u0430\u0437", "Diamond") },
            { "Item_PlatinumName", L("\u041f\u043b\u0430\u0442\u0438\u043d\u0430", "Platinum") },
            { "Item_UraniumName", L("\u0423\u0440\u0430\u043d", "Uranium") },
            { "Item_QuartzName", L("\u041a\u0432\u0430\u0440\u0446", "Quartz") },
            { "Item_SulfurName", L("\u0421\u0435\u0440\u0430", "Sulfur") },
            { "Item_ScrapName", L("\u041b\u043e\u043c", "Scrap") },
            { "Item_AntiqueName", L("\u0410\u043d\u0442\u0438\u043a\u0432\u0430\u0440\u0438\u0430\u0442", "Antique") },
            { "bronze", L("\u0411\u0440\u043e\u043d\u0437\u0430", "Bronze") },
            { "steel", L("\u0421\u0442\u0430\u043b\u044c", "Steel") },
            { "titanium", L("\u0422\u0438\u0442\u0430\u043d", "Titanium") },
            { "iron", L("\u0416\u0435\u043b\u0435\u0437\u043e", "Iron") },
            { "copper", L("\u041c\u0435\u0434\u044c", "Copper") },
            { "coal", L("\u0423\u0433\u043e\u043b\u044c", "Coal") },
            { "gold", L("\u0417\u043e\u043b\u043e\u0442\u043e", "Gold") },
            { "silver", L("\u0421\u0435\u0440\u0435\u0431\u0440\u043e", "Silver") },
            { "quartz", L("\u041a\u0432\u0430\u0440\u0446", "Quartz") },
            { "sulfur", L("\u0421\u0435\u0440\u0430", "Sulfur") },
            { "clay", L("\u0413\u043b\u0438\u043d\u0430", "Clay") },
            { "stone", L("\u041a\u0430\u043c\u0435\u043d\u044c", "Stone") },
            { "limestone", L("\u0418\u0437\u0432\u0435\u0441\u0442\u043d\u044f\u043a", "Limestone") },
            { "sandstone", L("\u041f\u0435\u0441\u0447\u0430\u043d\u0438\u043a", "Sandstone") },
            { "diamond", L("\u0410\u043b\u043c\u0430\u0437", "Diamond") },
            { "platinum", L("\u041f\u043b\u0430\u0442\u0438\u043d\u0430", "Platinum") },
            { "uranium", L("\u0423\u0440\u0430\u043d", "Uranium") },
            { "scrap", L("\u041b\u043e\u043c", "Scrap") },
            { "antique", L("\u0410\u043d\u0442\u0438\u043a\u0432\u0430\u0440\u0438\u0430\u0442", "Antique") },
        };

        private static LocalizedPair L(string ru, string en) => new LocalizedPair { ru = ru, en = en };
    }
}

