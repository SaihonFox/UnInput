namespace UniversalInput.Backends.Hid.Descriptors;

/// <summary>
/// Читает из дескриптора отчётов HID только то, что нужно для классификации: страницу и применение каждой
/// коллекции верхнего уровня типа Application. Поля отчётов разбирает <see cref="ReportLayoutReader"/>.
/// </summary>
internal static class ReportDescriptorReader
{
	private const int ApplicationCollection = 1;

	private const int Main = 0;
	private const int Global = 1;
	private const int Local = 2;

	/// <summary>Повреждённый или оборванный дескриптор не вызывает исключения: возвращается то, что успели прочитать.</summary>
	public static IReadOnlyList<HidUsage> ReadApplicationUsages(ReadOnlySpan<byte> descriptor)
	{
		List<HidUsage> result = [];
		Stack<int> savedPages = new();
		var page = 0;
		(int? Page, int Id)? firstUsage = null;
		var depth = 0;
		var position = 0;

		while (position < descriptor.Length)
		{
			var prefix = descriptor[position++];

			// Длинный элемент (0xFE): длина данных, тег, данные. В реальных устройствах не встречается, но пропустить его нужно.
			if (prefix == 0xFE)
			{
				if (position >= descriptor.Length)
				{
					break;
				}

				position += 2 + descriptor[position];
				continue;
			}

			var size = (prefix & 3) == 3 ? 4 : prefix & 3;
			if (position + size > descriptor.Length)
			{
				break;
			}

			uint data = 0;
			for (var i = 0; i < size; i++)
			{
				data |= (uint)descriptor[position + i] << (8 * i);
			}

			position += size;

			var type = (prefix >> 2) & 3;
			var tag = prefix >> 4;

			switch (type)
			{
				case Global:
					if (tag == 0x0)
					{
						page = (int)data;
					}
					else if (tag == 0xA)
					{
						savedPages.Push(page);
					}
					else if (tag == 0xB && savedPages.Count > 0)
					{
						page = savedPages.Pop();
					}

					break;

				case Local:
					// Берём первое применение перед коллекцией; четыре байта — расширенное применение со страницей в старших 16 битах.
					if (tag == 0x0 && firstUsage is null)
					{
						firstUsage = size == 4 ? ((int)(data >> 16), (int)(data & 0xFFFF)) : (null, (int)data);
					}

					break;

				case Main:
					if (tag == 0xA)
					{
						if (depth == 0 && data == ApplicationCollection && firstUsage is { } usage)
						{
							result.Add(new HidUsage(usage.Page ?? page, usage.Id));
						}

						depth++;
					}
					else if (tag == 0xC)
					{
						depth = Math.Max(0, depth - 1);
					}

					// Локальное состояние сбрасывается после каждого основного элемента.
					firstUsage = null;
					break;
			}
		}

		return result;
	}
}
