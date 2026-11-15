# UniversalInput

Модульная кроссплатформенная библиотека устройств ввода для .NET 10: единый API для обнаружения устройств, чтения событий, эмуляции и прямого доступа. Проектный документ (архитектура, решения, ограничения платформ) — Technical Design Document.

## Состояние

Готовы контракты, ядро, интеграция с Generic Host, mock backend, backend последовательных портов, тесты и пример. Клавиатуры, мыши, геймпады и HID библиотека пока не читает; платформы Windows, Linux, macOS, Android и iOS не заявлены как поддерживаемые.

| Проект | Что внутри |
|---|---|
| `UniversalInput.Abstractions` | Модели устройств, `InputEvent`, контракты backend и хаба, ошибки. Ни от чего не зависит |
| `UniversalInput.Core` | Реестр устройств, диспетчер событий, подписки, жизненный цикл backend, `InputHub` |
| `UniversalInput.Hosting` | `AddUniversalInput`, фоновая служба, настройки |
| `UniversalInput.Testing` | `MockInputBackend`, вспомогательные средства для тестов |
| `UniversalInput.Backends.Serial` | Backend COM-портов (System.IO.Ports): обнаружение опросом, сырые байты через прямой доступ |
| `tests/*` | Тесты xUnit v3 (включая проверку правил зависимостей по файлам проектов) |
| `samples/UniversalInput.Samples.ConsoleApp` | Консольный пример на mock backend |

Правила зависимостей: Abstractions — ни на что; Core — только на Abstractions; backend — только на Abstractions, никогда на Core. Нарушение ломает тест `ProjectReferenceTests`.

## Подключение

```csharp
services.AddUniversalInput(input => input
	.AddSerial(options => options.PollInterval = TimeSpan.FromSeconds(1)));
```

Пакет backend зависит только от `UniversalInput.Abstractions`: метод расширения (`AddSerial`) вешается на `IInputBackendBuilder`, который реализует построитель из Hosting.

## Сборка и проверка

```
dotnet build UniversalInput.slnx
dotnet test UniversalInput.slnx
dotnet run --project samples/UniversalInput.Samples.ConsoleApp
```

Проекты xUnit v3 — исполняемые, поэтому каждый тестовый проект можно запустить и напрямую: `dotnet run --project tests/UniversalInput.Core.Tests`.

## Что устроено иначе, чем в первой версии проектного документа

- При переполнении подписки относительное движение мыши и колесо **суммируются**, а не заменяются последним значением (иначе смещение терялось бы); абсолютные значения — позиция, оси, касания, датчики — заменяются последним.
- `StreamGap` для потерь на уровне очереди backend ставится после уже доставленных событий; точное место пропуска относительно соседних событий приблизительное.

## Ещё не сделано

- Арбитраж между backend: сопоставление дескрипторов одного физического устройства и подавление дублей агрегатных событий (`OriginFilter`). Сейчас исполнитель операции выбирается по порядку приоритетов, а ключ устройства должен быть уникален.
- `SubscriptionOptions.MaxContinuousRate`.
- Backend GlobalHook, Hid, Sdl, Native, Bluetooth, Android, iOS и сетевая часть.
