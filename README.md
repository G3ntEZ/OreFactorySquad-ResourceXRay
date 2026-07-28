# Ore Factory Squad — Resource X-Ray

MelonLoader mod that adds **x-ray markers** for ore nodes underground. Choose which ores to highlight; markers show through terrain with name + distance.

**[Русская версия ↓](#русская-версия)**

---

## Features

- See selected ores through walls (ESP-style markers)
- Per-ore toggle: enable only what you need
- Language modes: **Auto** (follows game I2 language), **RU**, **EN**
- Settings are saved between sessions

## Requirements

| Soft | Link |
|------|------|
| Game | [Ore Factory Squad on Steam](https://store.steampowered.com/app/4210580/Ore_Factory_Squad/) |
| Mod loader | [MelonLoader](https://github.com/LavaGang/MelonLoader/releases) **v0.7.3+** (Il2Cpp / net6) |

## Install (players)

1. Install **MelonLoader** into the game folder (select `Ore Factory Squad.exe`).
2. Download `OFSResourceXRay.dll` from the latest [Release](../../releases/latest).
3. Put the DLL into:
   ```
   <GameFolder>\Mods\OFSResourceXRay.dll
   ```
   Example:
   ```
   C:\Program Files (x86)\Steam\steamapps\common\Ore Factory Squad\Mods\
   ```
4. Launch the game. MelonLoader console should list **Resource X-Ray**.

> First MelonLoader launch may take longer while it generates Il2Cpp assemblies.

## Controls

| Key | Action |
|-----|--------|
| **F8** | Open / close ore menu |
| **↑ / ↓** or **W / S** | Move selection |
| **Enter / Space / E** | Toggle selected ore ON/OFF |
| **Mouse click** | Toggle ore / press menu buttons |
| **1** | Enable all ores |
| **2** | Disable all ores |
| **L** | Language: Auto → RU → EN → Auto |
| **F7** | Master ESP on/off |
| **F6** | Refresh ore list / rescan |
| **Esc** | Close menu |

## Language

- **Auto** — matches the game language (Russian / English via I2 Localization)
- **RU** / **EN** — force mod UI language  
Preference is stored in MelonLoader preferences (`ResourceXRay` → `Language`).

## Build from source (developers)

1. Install MelonLoader on the game and run it once (creates `MelonLoader\Il2CppAssemblies`).
2. Install [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0).
3. Edit `OFSResourceXRay.csproj` — set `GameDir` to your game path.
4. Build:
   ```bash
   dotnet build -c Release
   ```
5. Output DLL is copied to `<GameDir>\Mods\` automatically.

## Disclaimer

This is an unofficial fan mod, not affiliated with threeW or PlayWay. Use at your own risk. Single-player / private use recommended; respect the game’s multiplayer rules.

## License

MIT — see [LICENSE](LICENSE).

---

# Русская версия

MelonLoader-мод с **рентгеном руд**: метки сквозь землю, выбор нужных ресурсов, имя и дистанция.

## Возможности

- Метки выбранных руд сквозь стены
- Включение/выключение каждой руды отдельно
- Язык: **Авто** (как в игре), **RU**, **EN**
- Настройки сохраняются

## Что нужно

| Что | Ссылка |
|-----|--------|
| Игра | [Ore Factory Squad в Steam](https://store.steampowered.com/app/4210580/Ore_Factory_Squad/) |
| Загрузчик | [MelonLoader](https://github.com/LavaGang/MelonLoader/releases) **v0.7.3+** |

## Установка

1. Установи **MelonLoader** в папку игры (укажи `Ore Factory Squad.exe`).
2. Скачай `OFSResourceXRay.dll` из последнего [Release](../../releases/latest).
3. Положи файл сюда:
   ```
   <ПапкаИгры>\Mods\OFSResourceXRay.dll
   ```
4. Запусти игру. В консоли MelonLoader должен появиться **Resource X-Ray**.

> Первый запуск с MelonLoader может быть дольше обычного.

## Управление

| Клавиша | Действие |
|---------|----------|
| **F8** | Меню руд |
| **↑ / ↓** или **W / S** | Выбор строки |
| **Enter / Space / E** | Вкл/выкл руду |
| **Клик мышью** | То же + кнопки меню |
| **1** | Включить все |
| **2** | Выключить все |
| **L** | Язык: Авто → RU → EN |
| **F7** | Рентген вкл/выкл |
| **F6** | Обновить список |
| **Esc** | Закрыть меню |

## Язык

- **Авто** — как язык игры  
- **RU** / **EN** — вручную  

## Сборка из исходников

1. Поставь MelonLoader и один раз запусти игру.
2. Установи [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0).
3. В `OFSResourceXRay.csproj` укажи свой `GameDir`.
4. `dotnet build -c Release`

## Отказ от ответственности

Неофициальный фан-мод, не связан с threeW / PlayWay. Используй на свой риск.

## Лицензия

MIT — см. [LICENSE](LICENSE).
