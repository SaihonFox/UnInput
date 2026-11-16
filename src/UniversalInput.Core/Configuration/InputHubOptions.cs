namespace UniversalInput.Core.Configuration;

/// <summary>Настройки Core. Значения по умолчанию — стартовые предположения, их уточняет профилирование.</summary>
public sealed class InputHubOptions
{
	/// <summary>Ёмкость очереди каждого backend (события, ещё не взятые диспетчером).</summary>
	public int BackendQueueCapacity { get; set; } = 4096;

	/// <summary>Начальная задержка перезапуска backend после восстановимой ошибки.</summary>
	public TimeSpan RestartMinDelay { get; set; } = TimeSpan.FromSeconds(1);

	/// <summary>Предельная задержка перезапуска (задержка растёт экспоненциально).</summary>
	public TimeSpan RestartMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>Сколько ждать <c>StopAsync</c> backend, прежде чем освободить его принудительно.</summary>
	public TimeSpan BackendStopTimeout { get; set; } = TimeSpan.FromSeconds(5);

	/// <summary>Как долго помнить удалённое устройство, чтобы вернуть ему прежний <see cref="DeviceId"/>.</summary>
	public TimeSpan RemovedDeviceRetention { get; set; } = TimeSpan.FromMinutes(5);

	/// <summary>Строгий режим: остановить запуск, если не стартовал backend, помеченный обязательным.</summary>
	public bool FailFast { get; set; }

	/// <summary>Идентификаторы backend в порядке убывания приоритета при выборе исполнителя операции.</summary>
	public List<string> BackendPriority { get; set; } = [];

	/// <summary>
	/// Приоритет backend по видам устройств (ключ — <see cref="InputDeviceKind.Name"/>) при выборе владельца роли
	/// для устройства, которое видят несколько backend. Backend, которых в списке нет, идут после перечисленных.
	/// По умолчанию: геймпады и джойстики — Sdl, затем Hid; клавиатуры и мыши по устройствам — Native, затем Hid.
	/// </summary>
	public Dictionary<string, List<string>> KindPriority { get; set; } = new()
	{
		["gamepad"] = ["sdl", "hid"],
		["joystick"] = ["sdl", "hid"],
		["keyboard"] = ["native", "hid"],
		["mouse"] = ["native", "hid"],
	};

	/// <summary>Приоритет backend для сырых reports (<see cref="InputOperation.DirectAccess"/>): по умолчанию Hid, затем Native.</summary>
	public List<string> RawAccessPriority { get; set; } = ["hid", "native"];

	public void Validate()
	{
		ArgumentNullException.ThrowIfNull(KindPriority);
		ArgumentNullException.ThrowIfNull(RawAccessPriority);
		ArgumentNullException.ThrowIfNull(BackendPriority);

		if (BackendQueueCapacity < 2)
		{
			throw new ArgumentOutOfRangeException(nameof(BackendQueueCapacity), BackendQueueCapacity, "Ёмкость очереди должна быть не меньше 2.");
		}

		if (RestartMinDelay <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(RestartMinDelay), RestartMinDelay, "Задержка должна быть положительной.");
		}

		if (RestartMaxDelay < RestartMinDelay)
		{
			throw new ArgumentOutOfRangeException(nameof(RestartMaxDelay), RestartMaxDelay, "Предельная задержка меньше начальной.");
		}

		if (BackendStopTimeout <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(BackendStopTimeout), BackendStopTimeout, "Тайм-аут должен быть положительным.");
		}

		if (RemovedDeviceRetention < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(RemovedDeviceRetention), RemovedDeviceRetention, "Срок хранения не может быть отрицательным.");
		}
	}
}
