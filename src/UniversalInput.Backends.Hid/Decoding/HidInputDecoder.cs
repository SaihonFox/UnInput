using UniversalInput.Backends.Hid.Descriptors;

namespace UniversalInput.Backends.Hid.Decoding;

/// <summary>
/// Превращает входные отчёты клавиатуры и мыши в события UniversalInput по дескриптору отчётов устройства.
/// Состояние (какие клавиши и кнопки уже нажаты) хранится в объекте, поэтому на каждое устройство нужен свой экземпляр,
/// и вызывать <see cref="Decode"/> можно только из одного потока.
/// <para>
/// Что разбирается: клавиши страницы 0x07 (переменные поля — например, модификаторы, и массивы — например, шесть клавиш
/// загрузочной клавиатуры), кнопки 1–5, относительные оси X и Y, колесо (Generic Desktop 0x38) и AC Pan (Consumer 0x238)
/// у приложений «клавиатура» (0x01:0x06 и 0x01:0x07) и «мышь» (0x01:0x02). Остальное (абсолютные оси, геймпады, тачпады,
/// мультимедиа-клавиши) не разбирается.
/// </para>
/// Формат отчёта тот же, что у <see cref="Platform.IHidConnection"/>: первый байт — идентификатор отчёта, затем данные.
/// </summary>
internal sealed class HidInputDecoder
{
	private const int GenericDesktop = 0x01;
	private const int Keyboard = 0x07;
	private const int Button = 0x09;
	private const int Consumer = 0x0C;

	private const int FirstKey = 0x04;
	private const int LastKey = 0xE7;

	private readonly Dictionary<int, List<HidInputField>> _keyFields = [];
	private readonly Dictionary<int, List<HidInputField>> _mouseFields = [];
	private readonly Dictionary<int, int> _requiredBytes = [];
	private readonly Dictionary<int, HashSet<int>> _pressedKeys = [];
	private readonly Dictionary<int, HashSet<int>> _pressedButtons = [];

	private HidInputDecoder(IReadOnlyList<HidInputField> fields)
	{
		foreach (var field in fields)
		{
			var isKeyboard = field.Application is { Page: GenericDesktop, Usage: 0x06 or 0x07 };
			var isMouse = field.Application is { Page: GenericDesktop, Usage: 0x02 };

			if (isKeyboard && field.Usages.Any(u => u.Page == Keyboard))
			{
				Add(_keyFields, field);
			}
			else if (isMouse && field.Usages.Any(IsMouseUsage))
			{
				Add(_mouseFields, field);
			}
			else
			{
				continue;
			}

			var bytes = (field.EndBit + 7) / 8;
			_requiredBytes[field.ReportId] = Math.Max(_requiredBytes.GetValueOrDefault(field.ReportId), bytes);
		}
	}

	/// <summary>Самый длинный отчёт, который нужен декодеру, вместе с байтом идентификатора.</summary>
	public int MaxReportLength => _requiredBytes.Count == 0 ? 0 : _requiredBytes.Values.Max() + 1;

	public bool HasKeyboard => _keyFields.Count > 0;

	public bool HasMouse => _mouseFields.Count > 0;

	/// <summary>Возвращает <c>null</c>, если в дескрипторе нет ни клавиатуры, ни мыши, из отчётов которых можно получать события.</summary>
	public static HidInputDecoder? TryCreate(ReadOnlySpan<byte> descriptor)
	{
		if (descriptor.IsEmpty)
		{
			return null;
		}

		var decoder = new HidInputDecoder(ReportLayoutReader.ReadInputFields(descriptor));
		return decoder.HasKeyboard || decoder.HasMouse ? decoder : null;
	}

	/// <summary>
	/// Разбирает один отчёт и сообщает события. Отчёт с чужим идентификатором или короче, чем нужно полям,
	/// игнорируется целиком: частичный разбор дал бы ложные отпускания клавиш.
	/// </summary>
	public void Decode(DeviceId device, ReadOnlySpan<byte> report, Action<InputEvent> emit)
	{
		if (report.IsEmpty)
		{
			return;
		}

		var reportId = report[0];
		var data = report[1..];
		if (!_requiredBytes.TryGetValue(reportId, out var required) || data.Length < required)
		{
			return;
		}

		if (_mouseFields.TryGetValue(reportId, out var mouseFields))
		{
			DecodeMouse(device, data, reportId, mouseFields, emit);
		}

		if (_keyFields.TryGetValue(reportId, out var keyFields))
		{
			DecodeKeys(device, data, reportId, keyFields, emit);
		}
	}

	private static bool IsMouseUsage(HidUsage usage) => usage switch
	{
		{ Page: Button, Usage: >= 1 and <= 5 } => true,
		{ Page: GenericDesktop, Usage: 0x30 or 0x31 or 0x38 } => true,
		{ Page: Consumer, Usage: 0x238 } => true,
		_ => false,
	};

	private static void Add(Dictionary<int, List<HidInputField>> map, HidInputField field)
	{
		if (!map.TryGetValue(field.ReportId, out var list))
		{
			map[field.ReportId] = list = [];
		}

		list.Add(field);
	}

	private void DecodeKeys(
		DeviceId device, ReadOnlySpan<byte> data, int reportId, List<HidInputField> fields, Action<InputEvent> emit)
	{
		HashSet<int> current = [];
		foreach (var field in fields)
		{
			for (var i = 0; i < field.Count; i++)
			{
				var value = Read(data, field, i);
				if (value == 0)
				{
					continue;
				}

				var usage = field.IsVariable ? field.UsageOfElement(i) : field.UsageOfArrayValue(value);
				if (usage is { Page: Keyboard, Usage: >= FirstKey and <= LastKey } key)
				{
					current.Add(key.Usage);
				}
			}
		}

		var previous = Get(_pressedKeys, reportId);
		var released = previous.Except(current).Order().ToList();
		var pressed = current.Except(previous).Order().ToList();
		_pressedKeys[reportId] = current;

		var modifiers = CurrentModifiers();
		foreach (var usage in released)
		{
			emit(InputEvent.KeyUp(device, usage, modifiers));
		}

		foreach (var usage in pressed)
		{
			emit(InputEvent.KeyDown(device, usage, modifiers: modifiers));
		}
	}

	private void DecodeMouse(
		DeviceId device, ReadOnlySpan<byte> data, int reportId, List<HidInputField> fields, Action<InputEvent> emit)
	{
		HashSet<int> buttons = [];
		long dx = 0, dy = 0, wheel = 0, pan = 0;
		foreach (var field in fields)
		{
			for (var i = 0; i < field.Count; i++)
			{
				var usage = field.UsageOfElement(i);
				if (usage is not { } u || !field.IsVariable)
				{
					continue;
				}

				var value = Read(data, field, i);
				switch (u)
				{
					case { Page: Button, Usage: >= 1 and <= 5 }:
						if (value != 0)
						{
							buttons.Add(u.Usage);
						}

						break;
					case { Page: GenericDesktop, Usage: 0x30 } when field.IsRelative:
						dx += value;
						break;
					case { Page: GenericDesktop, Usage: 0x31 } when field.IsRelative:
						dy += value;
						break;
					case { Page: GenericDesktop, Usage: 0x38 } when field.IsRelative:
						wheel += value;
						break;
					case { Page: Consumer, Usage: 0x238 } when field.IsRelative:
						pan += value;
						break;
				}
			}
		}

		if (dx != 0 || dy != 0)
		{
			emit(InputEvent.MouseMove(device, dx, dy, absolute: false));
		}

		var previous = Get(_pressedButtons, reportId);
		var released = previous.Except(buttons).Order().ToList();
		var pressed = buttons.Except(previous).Order().ToList();
		_pressedButtons[reportId] = buttons;

		foreach (var button in released)
		{
			emit(InputEvent.MouseUp(device, button));
		}

		foreach (var button in pressed)
		{
			emit(InputEvent.MouseDown(device, button));
		}

		if (wheel != 0 || pan != 0)
		{
			emit(InputEvent.Wheel(device, pan, wheel));
		}
	}

	private KeyModifiers CurrentModifiers()
	{
		var result = KeyModifiers.None;
		foreach (var usage in _pressedKeys.Values.SelectMany(set => set))
		{
			result |= usage switch
			{
				0xE0 or 0xE4 => KeyModifiers.Control,
				0xE1 or 0xE5 => KeyModifiers.Shift,
				0xE2 or 0xE6 => KeyModifiers.Alt,
				0xE3 or 0xE7 => KeyModifiers.Meta,
				_ => KeyModifiers.None,
			};
		}

		return result;
	}

	private static HashSet<int> Get(Dictionary<int, HashSet<int>> map, int reportId) =>
		map.TryGetValue(reportId, out var set) ? set : [];

	/// <summary>Значение i-го элемента поля; знаковое, если нижний предел отрицателен. Биты идут от младшего к старшему.</summary>
	private static int Read(ReadOnlySpan<byte> data, HidInputField field, int index)
	{
		var start = field.BitOffset + (index * field.BitSize);
		uint value = 0;
		for (var bit = 0; bit < field.BitSize; bit++)
		{
			var position = start + bit;
			if ((data[position >> 3] & (1 << (position & 7))) != 0)
			{
				value |= 1u << bit;
			}
		}

		if (field.IsSigned && field.BitSize < 32 && (value & (1u << (field.BitSize - 1))) != 0)
		{
			value |= uint.MaxValue << field.BitSize;
		}

		return (int)value;
	}
}
