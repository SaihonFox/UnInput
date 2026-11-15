using System.Collections.Frozen;

namespace UniversalInput.Abstractions.Features;

/// <summary>
/// Декларативное правило подавления событий. Проверяется без аллокаций прямо на потоке хука,
/// поэтому это набор кодов, а не делегат.
/// </summary>
public sealed class InterceptRule
{
	private readonly FrozenSet<int> _keys;
	private readonly FrozenSet<int> _mouseButtons;

	public InterceptRule(IEnumerable<int>? keys = null, IEnumerable<int>? mouseButtons = null)
	{
		_keys = (keys ?? []).ToFrozenSet();
		_mouseButtons = (mouseButtons ?? []).ToFrozenSet();
	}

	public IReadOnlyCollection<int> Keys => _keys;

	public IReadOnlyCollection<int> MouseButtons => _mouseButtons;

	/// <summary>Нужно ли подавить событие.</summary>
	public bool Matches(in InputEvent inputEvent) => inputEvent.Kind switch
	{
		InputEventKind.KeyPressed or InputEventKind.KeyReleased => _keys.Contains(inputEvent.Int0),
		InputEventKind.MouseButtonPressed or InputEventKind.MouseButtonReleased => _mouseButtons.Contains(inputEvent.Int0),
		_ => false,
	};
}
