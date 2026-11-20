# GaGa — состояние

Форк Beluki/GaGa: Solomag/GaGa. Локальная ветка `fork-master` → push `fork-master:master` в remote `fork`.
- Windows: трей-плеер, C# .NET 4, `Source/GaGa`.
- macOS: Swift/AppKit порт, `macOS/`.

## Версия
- Выпущена 2026.11.17: Ogg Vorbis/Opus через декодирование в WAV (NVorbis + Concentus вшиты в exe).
- После неё в работе: один экземпляр на пользователя (Mutex в `Program.cs`). Повод — у Игоря два пункта автозагрузки, и оба GaGa обновлялись одновременно. Не выпущено.

## Сборка и выпуск
- Сборка: `powershell -NoProfile -Command "& cmd.exe /c E:\Radio\GaGa\build.cmd [outdir]"` (csc из .NET 4, без VS).
- Выпуск только с согласия пользователя:
  1. версия в `Source/GaGa/Assembly/AssemblyInfo.cs` и `macOS/build.sh` (CRLF в AssemblyInfo не ломать, без sed);
  2. commit, `git push fork fork-master:master`;
  3. `gh run watch`, затем `gh run download`;
  4. переименовать mac-файлы в `GaGa-macOS-<ver>.dmg/.zip`;
  5. `gh release create <ver> --repo Solomag/GaGa`;
  6. скопировать exe в `Build\GaGa.exe`.
- Схема версий: дата (2026.11.17).

## Ключевые места
- `NotifyIconPlayer/HttpsRelay.cs`: локальный relay для https, ICY, HLS, Ogg FLAC→FLAC (`OggFlacRemuxer`), Vorbis/Opus→WAV (`OggDecoder`), запись (`StreamRecorder`).
- `Program.cs`: загрузка вшитых DLL (AssemblyResolve), single-instance Mutex.
- `Updater.cs`: проверка релизов GitHub, замена exe (.old/.new), перезапуск.
- `Search/`: окно поиска radio-browser (просмотр страны, фильтр, сортировка).

## Решения пользователя
- Отвечать по-русски. Push, релиз и публикация — только с явного «да».
- В git-коммитах noreply-почта, не личная.
- Режим «весь каталог» (61 МБ) отброшен, оставлен просмотр по стране.

## Открыто
- Откуда у Игоря второй пункт автозагрузки GaGa — не выяснено (GaGa пишет только `HKCU\...\Run\GaGa`). Возможно, ярлык в папке «Автозагрузка».
- macOS-сборка на реальном Mac не проверялась.
