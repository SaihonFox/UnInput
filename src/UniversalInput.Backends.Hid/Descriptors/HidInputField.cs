namespace UniversalInput.Backends.Hid.Descriptors;

/// <summary>
/// Одно поле входного отчёта из дескриптора: <see cref="Count"/> элементов по <see cref="BitSize"/> бит, начиная с
/// <see cref="BitOffset"/> от начала данных отчёта (после байта идентификатора). Константные поля (заполнение) не сохраняются.
/// </summary>
/// <param name="Usages">
/// Для переменного поля — применение каждого элемента (если применений меньше, чем элементов, последнее относится к остальным).
/// Для массива — применения, которые выбирает значение элемента (значение <c>LogicalMin</c> — первое применение).
/// </param>
internal sealed record HidInputField(
	int ReportId,
	int BitOffset,
	int BitSize,
	int Count,
	int LogicalMin,
	int LogicalMax,
	bool IsVariable,
	bool IsRelative,
	IReadOnlyList<HidUsage> Usages,
	HidUsage? Application)
{
	/// <summary>Первый бит после поля.</summary>
	public int EndBit => BitOffset + (BitSize * Count);

	public bool IsSigned => LogicalMin < 0;

	public HidUsage? UsageOfElement(int index)
	{
		if (Usages.Count == 0)
		{
			return null;
		}

		return Usages[Math.Min(index, Usages.Count - 1)];
	}

	/// <summary>Применение, которое выбрал элемент массива; <c>null</c>, если значение вне диапазона.</summary>
	public HidUsage? UsageOfArrayValue(int value)
	{
		var index = value - LogicalMin;
		return index >= 0 && index < Usages.Count ? Usages[index] : null;
	}
}
