namespace UniversalInput.Core.Events;

/// <summary>
/// Склейка непрерывных событий при переполнении подписки.
/// Относительное движение мыши и колесо суммируются (иначе смещение потерялось бы),
/// абсолютные значения (позиция, оси, касание, датчики) заменяются последним.
/// </summary>
internal static class EventCoalescer
{
	public static bool TryMerge(in InputEvent existing, in InputEvent incoming, out InputEvent merged)
	{
		merged = default;

		if (existing.Device != incoming.Device || existing.Kind != incoming.Kind)
		{
			return false;
		}

		switch (incoming.Kind)
		{
			case InputEventKind.MouseMoved:
				var absolute = (incoming.Flags & InputEventFlags.Absolute) != 0;
				if (absolute != ((existing.Flags & InputEventFlags.Absolute) != 0))
				{
					return false;
				}

				merged = absolute
					? incoming
					: incoming with { V0 = existing.V0 + incoming.V0, V1 = existing.V1 + incoming.V1 };
				break;

			case InputEventKind.MouseWheel:
				merged = incoming with { V0 = existing.V0 + incoming.V0, V1 = existing.V1 + incoming.V1 };
				break;

			case InputEventKind.GamepadAxisChanged:
			case InputEventKind.TouchMoved:
			case InputEventKind.SensorChanged:
				if (existing.Int0 != incoming.Int0)
				{
					return false;
				}

				merged = incoming;
				break;

			default:
				return false;
		}

		merged = merged with { Flags = merged.Flags | InputEventFlags.Coalesced };
		return true;
	}
}
