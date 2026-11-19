namespace UniversalInput.Backends.GlobalHook.Sources;

/// <summary>
/// Источник событий глобального хука. Отделён от backend, чтобы логику проверять без настоящего хука ОС.
/// Один экземпляр — один запуск: после <see cref="IAsyncDisposable.DisposeAsync"/> хук заново не стартует.
/// </summary>
internal interface IHookSource : IAsyncDisposable
{
	/// <summary>Вызывается из потока хука: обработчик должен быть быстрым и не блокироваться.</summary>
	event Action<HookSignal>? Signal;

	/// <summary>Хук остановился сам, уже после успешного запуска.</summary>
	event Action<Exception>? Failed;

	/// <summary>
	/// Решает, подавить ли клавишу или кнопку мыши. Источник вызывает его на потоке хука до <see cref="Signal"/>
	/// и только для нажатий и отпусканий. Подавление в libuiohook работает на Windows и macOS; на других платформах значение игнорируется.
	/// </summary>
	Func<HookSignal, bool>? ShouldSuppress { get; set; }

	/// <summary>Возвращает управление, когда хук включён; бросает исключение, если запустить его не удалось.</summary>
	ValueTask StartAsync(CancellationToken cancellationToken);
}
