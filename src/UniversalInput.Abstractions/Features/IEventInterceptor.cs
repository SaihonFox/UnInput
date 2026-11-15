namespace UniversalInput.Abstractions.Features;

/// <summary>Подавление событий. Работает синхронно, на потоке хука; доступно не на всех платформах.</summary>
public interface IEventInterceptor : IBackendFeature
{
	/// <summary>Задаёт правило подавления; <c>null</c> отключает подавление.</summary>
	void SetRule(InterceptRule? rule);
}
