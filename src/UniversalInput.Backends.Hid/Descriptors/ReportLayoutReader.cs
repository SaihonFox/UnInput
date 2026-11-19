namespace UniversalInput.Backends.Hid.Descriptors;

/// <summary>
/// Разбирает входные поля дескриптора отчётов HID: где в отчёте лежит каждое поле, его размер, пределы и применения.
/// Выходные и feature-отчёты пропускаются. Повреждённый или оборванный дескриптор не вызывает исключения:
/// возвращается то, что успели прочитать. Разделители (delimiter) игнорируются.
/// </summary>
internal static class ReportLayoutReader
{
	private const int MaxExpandedUsages = 4096;

	private const int Main = 0;
	private const int Global = 1;
	private const int Local = 2;

	public static IReadOnlyList<HidInputField> ReadInputFields(ReadOnlySpan<byte> descriptor)
	{
		List<HidInputField> fields = [];
		Stack<Globals> saved = new();
		Dictionary<int, int> offsets = [];
		List<HidUsage> usages = [];
		HidUsage? usageMin = null;
		HidUsage? usageMax = null;
		HidUsage? application = null;
		var globals = new Globals();
		var depth = 0;
		var position = 0;

		while (position < descriptor.Length)
		{
			var prefix = descriptor[position++];

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

			uint raw = 0;
			for (var i = 0; i < size; i++)
			{
				raw |= (uint)descriptor[position + i] << (8 * i);
			}

			position += size;

			var type = (prefix >> 2) & 3;
			var tag = prefix >> 4;
			var signed = Signed(raw, size);

			switch (type)
			{
				case Global:
					switch (tag)
					{
						case 0x0:
							globals = globals with { Page = (int)raw };
							break;
						case 0x1:
							globals = globals with { LogicalMin = signed };
							break;
						case 0x2:
							// Максимум читается как знаковый, только если минимум отрицателен (иначе 0xFF — это 255, а не −1).
							globals = globals with { LogicalMax = globals.LogicalMin < 0 ? signed : (int)Math.Min(raw, int.MaxValue) };
							break;
						case 0x7:
							globals = globals with { Size = (int)raw };
							break;
						case 0x8:
							globals = globals with { ReportId = (int)raw };
							break;
						case 0x9:
							globals = globals with { Count = (int)raw };
							break;
						case 0xA:
							saved.Push(globals);
							break;
						case 0xB:
							if (saved.Count > 0)
							{
								globals = saved.Pop();
							}

							break;
					}

					break;

				case Local:
					switch (tag)
					{
						case 0x0:
							usages.Add(UsageOf(raw, size, globals.Page));
							break;
						case 0x1:
							usageMin = UsageOf(raw, size, globals.Page);
							break;
						case 0x2:
							usageMax = UsageOf(raw, size, globals.Page);
							break;
					}

					break;

				case Main:
					switch (tag)
					{
						case 0x8:
							AddInput(raw, globals, usages, usageMin, usageMax, application, offsets, fields);
							break;
						case 0xA:
							if (depth == 0)
							{
								application = raw == 1 && usages.Count > 0 ? usages[0] : null;
							}

							depth++;
							break;
						case 0xC:
							depth = Math.Max(0, depth - 1);
							if (depth == 0)
							{
								application = null;
							}

							break;
					}

					usages.Clear();
					usageMin = null;
					usageMax = null;
					break;
			}
		}

		return fields;
	}

	private static void AddInput(
		uint flags,
		Globals globals,
		List<HidUsage> usages,
		HidUsage? usageMin,
		HidUsage? usageMax,
		HidUsage? application,
		Dictionary<int, int> offsets,
		List<HidInputField> fields)
	{
		offsets.TryGetValue(globals.ReportId, out var offset);
		var bits = globals.Size * globals.Count;
		var isConstant = (flags & 1) != 0;

		if (!isConstant && globals.Size > 0 && globals.Count > 0 && globals.Size <= 32)
		{
			var isVariable = (flags & 2) != 0;
			IReadOnlyList<HidUsage> resolved = usages.Count > 0 ? usages.ToList() : ExpandRange(usageMin, usageMax);

			fields.Add(new HidInputField(
				globals.ReportId,
				offset,
				globals.Size,
				globals.Count,
				globals.LogicalMin,
				globals.LogicalMax,
				isVariable,
				(flags & 4) != 0,
				resolved,
				application));
		}

		offsets[globals.ReportId] = offset + bits;
	}

	private static List<HidUsage> ExpandRange(HidUsage? min, HidUsage? max)
	{
		List<HidUsage> result = [];
		if (min is not { } first)
		{
			return result;
		}

		var last = max?.Usage ?? first.Usage;
		for (var id = first.Usage; id <= last && result.Count < MaxExpandedUsages; id++)
		{
			result.Add(new HidUsage(first.Page, id));
		}

		return result;
	}

	/// <summary>Четыре байта — расширенное применение: страница в старших 16 битах.</summary>
	private static HidUsage UsageOf(uint raw, int size, int page) =>
		size == 4 ? new HidUsage((int)(raw >> 16), (int)(raw & 0xFFFF)) : new HidUsage(page, (int)raw);

	private static int Signed(uint raw, int size) => size switch
	{
		1 => (sbyte)raw,
		2 => (short)raw,
		_ => (int)raw,
	};

	private readonly record struct Globals(int Page, int LogicalMin, int LogicalMax, int Size, int Count, int ReportId);
}
