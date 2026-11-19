using System.Runtime.InteropServices;
using UniversalInput.Backends.Sdl.Runtime;

namespace UniversalInput.Backends.Sdl.Native;

/// <summary>Работа с настоящей библиотекой SDL3. Без SDL3 на машине не проверялась.</summary>
internal sealed unsafe class SdlNativeRuntime : ISdlRuntime
{
	// Флаги SDL_InitFlags из SDL3: SDL_INIT_GAMEPAD и SDL_INIT_EVENTS.
	private const uint InitFlags = 0x2000 | 0x4000;

	// Имя свойства SDL_PROP_GAMEPAD_CAP_RUMBLE_BOOLEAN (общее со свойством джойстика).
	private static ReadOnlySpan<byte> RumbleProperty => "SDL.joystick.cap.rumble\0"u8;

	private readonly string? _libraryPath;
	private readonly object _gate = new();
	private readonly Dictionary<uint, nint> _gamepads = new();
	private readonly Dictionary<uint, nint> _joysticks = new();
	private SdlApi? _api;
	private bool _initialized;

	public SdlNativeRuntime(string? libraryPath)
	{
		_libraryPath = libraryPath;
	}

	public void Initialize(bool allowBackgroundEvents)
	{
		var api = SdlApi.Load(_libraryPath);
		_api = api;

		if (allowBackgroundEvents)
		{
			fixed (byte* name = "SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS\0"u8)
			fixed (byte* value = "1\0"u8)
			{
				api.SetHint(name, value);
			}
		}

		if (api.InitSubSystem(InitFlags) == 0)
		{
			throw new InputException(
				InputErrorCode.BackendUnavailable,
				"SDL не смог включить подсистему геймпадов: " + LastError(api))
			{
				Remediation = "Проверьте драйверы ввода и права доступа к устройствам (на Linux — /dev/input и udev).",
			};
		}

		_initialized = true;
	}

	public IReadOnlyList<uint> GetGamepadIds()
	{
		var api = Api;
		var count = 0;
		var ids = api.GetGamepads(&count);
		if (ids == null)
		{
			return [];
		}

		try
		{
			var result = new uint[count];
			for (var i = 0; i < count; i++)
			{
				result[i] = ids[i];
			}

			return result;
		}
		finally
		{
			api.Free(ids);
		}
	}

	public IReadOnlyList<uint> GetRawJoystickIds()
	{
		var api = Api;
		var count = 0;
		var ids = api.GetJoysticks(&count);
		if (ids == null)
		{
			return [];
		}

		try
		{
			List<uint> result = [];
			for (var i = 0; i < count; i++)
			{
				if (api.IsGamepad(ids[i]) == 0)
				{
					result.Add(ids[i]);
				}
			}

			return result;
		}
		finally
		{
			api.Free(ids);
		}
	}

	public SdlJoystickInfo? OpenJoystick(uint instanceId)
	{
		var api = Api;

		// Устройство с раскладкой ведёт геймпадный путь: открывать его вторым способом не нужно.
		if (api.IsGamepad(instanceId) != 0)
		{
			return null;
		}

		var handle = api.OpenJoystick(instanceId);
		if (handle == 0)
		{
			return null;
		}

		lock (_gate)
		{
			_joysticks[instanceId] = handle;
		}

		return new SdlJoystickInfo(
			instanceId,
			Text(api.GetJoystickName(handle)) ?? "Joystick",
			api.GetJoystickVendor(handle),
			api.GetJoystickProduct(handle),
			Text(api.GetJoystickSerial(handle)),
			Text(api.GetJoystickPath(handle)),
			Math.Max(api.GetNumJoystickAxes(handle), 0),
			Math.Max(api.GetNumJoystickButtons(handle), 0),
			Math.Max(api.GetNumJoystickHats(handle), 0));
	}

	public void CloseJoystick(uint instanceId)
	{
		nint handle;
		lock (_gate)
		{
			if (!_joysticks.Remove(instanceId, out handle))
			{
				return;
			}
		}

		Api.CloseJoystick(handle);
	}

	public SdlGamepadInfo? OpenGamepad(uint instanceId, bool enableSensors)
	{
		var api = Api;
		var handle = api.OpenGamepad(instanceId);
		if (handle == 0)
		{
			return null;
		}

		lock (_gate)
		{
			_gamepads[instanceId] = handle;
		}

		var hasAccelerometer = api.GamepadHasSensor(handle, Mapping.GamepadMapper.SensorAccelerometer) != 0;
		var hasGyroscope = api.GamepadHasSensor(handle, Mapping.GamepadMapper.SensorGyroscope) != 0;
		if (enableSensors)
		{
			if (hasAccelerometer)
			{
				hasAccelerometer = api.SetGamepadSensorEnabled(handle, Mapping.GamepadMapper.SensorAccelerometer, 1) != 0;
			}

			if (hasGyroscope)
			{
				hasGyroscope = api.SetGamepadSensorEnabled(handle, Mapping.GamepadMapper.SensorGyroscope, 1) != 0;
			}
		}

		var properties = api.GetGamepadProperties(handle);
		var hasRumble = false;
		if (properties != 0)
		{
			fixed (byte* name = RumbleProperty)
			{
				hasRumble = api.GetBooleanProperty(properties, name, 0) != 0;
			}
		}

		return new SdlGamepadInfo(
			instanceId,
			Text(api.GetGamepadName(handle)) ?? "Gamepad",
			api.GetGamepadVendor(handle),
			api.GetGamepadProduct(handle),
			Text(api.GetGamepadSerial(handle)),
			Text(api.GetGamepadPath(handle)),
			hasRumble,
			enableSensors && hasAccelerometer,
			enableSensors && hasGyroscope);
	}

	public void CloseGamepad(uint instanceId)
	{
		nint handle;
		lock (_gate)
		{
			if (!_gamepads.Remove(instanceId, out handle))
			{
				return;
			}
		}

		Api.CloseGamepad(handle);
	}

	public bool TryWaitEvent(TimeSpan timeout, out SdlEvent sdlEvent)
	{
		var api = Api;
		var buffer = stackalloc byte[SdlEventParser.EventSize];
		new Span<byte>(buffer, SdlEventParser.EventSize).Clear();

		if (api.WaitEventTimeout(buffer, (int)Math.Ceiling(timeout.TotalMilliseconds)) == 0)
		{
			sdlEvent = default;
			return false;
		}

		sdlEvent = SdlEventParser.Parse(new ReadOnlySpan<byte>(buffer, SdlEventParser.EventSize));
		return true;
	}

	public void Rumble(uint instanceId, ushort lowFrequency, ushort highFrequency, uint durationMilliseconds)
	{
		var api = Api;
		nint handle;
		lock (_gate)
		{
			if (!_gamepads.TryGetValue(instanceId, out handle))
			{
				throw InputException.DeviceUnavailable("Геймпад SDL отключён.");
			}
		}

		if (api.RumbleGamepad(handle, lowFrequency, highFrequency, durationMilliseconds) == 0)
		{
			throw InputException.Unsupported("Вибрация недоступна для этого геймпада: " + LastError(api));
		}
	}

	public void Dispose()
	{
		var api = _api;
		if (api is null)
		{
			return;
		}

		_api = null;

		nint[] handles;
		lock (_gate)
		{
			handles = [.. _gamepads.Values];
			_gamepads.Clear();
		}

		foreach (var handle in handles)
		{
			api.CloseGamepad(handle);
		}

		nint[] joysticks;
		lock (_gate)
		{
			joysticks = [.. _joysticks.Values];
			_joysticks.Clear();
		}

		foreach (var handle in joysticks)
		{
			api.CloseJoystick(handle);
		}

		if (_initialized)
		{
			api.QuitSubSystem(InitFlags);
		}

		api.Dispose();
	}

	private SdlApi Api => _api ?? throw InputException.BackendFault("SDL не инициализирован.");

	private static string? Text(byte* pointer) =>
		pointer == null ? null : Marshal.PtrToStringUTF8((nint)pointer) is { Length: > 0 } text ? text : null;

	private static string LastError(SdlApi api) => Text(api.GetError()) ?? "причина не сообщена";
}
