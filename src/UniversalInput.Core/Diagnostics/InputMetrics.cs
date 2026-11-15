using System.Diagnostics.Metrics;

namespace UniversalInput.Core.Diagnostics;

/// <summary>Счётчики System.Diagnostics.Metrics: принято, отброшено, склеено, доставлено, глубина очередей.</summary>
internal sealed class InputMetrics : IDisposable
{
	public const string MeterName = "UniversalInput";

	private readonly Meter _meter = new(MeterName);
	private readonly Counter<long> _received;
	private readonly Counter<long> _dropped;
	private readonly Counter<long> _coalesced;
	private readonly Counter<long> _dispatched;

	public InputMetrics()
	{
		_received = _meter.CreateCounter<long>("universalinput.events.received", "{event}", "События, принятые очередью backend.");
		_dropped = _meter.CreateCounter<long>("universalinput.events.dropped", "{event}", "События, отброшенные до или во время доставки.");
		_coalesced = _meter.CreateCounter<long>("universalinput.events.coalesced", "{event}", "Непрерывные события, склеенные при переполнении подписки.");
		_dispatched = _meter.CreateCounter<long>("universalinput.events.dispatched", "{event}", "События, обработанные диспетчером.");
	}

	public void Received(string backend) => _received.Add(1, Tag("backend", backend));

	public void Dropped(string backend, string reason, long count = 1) =>
		_dropped.Add(count, Tag("backend", backend), Tag("reason", reason));

	public void Coalesced() => _coalesced.Add(1);

	public void Dispatched(string backend) => _dispatched.Add(1, Tag("backend", backend));

	/// <summary>Регистрирует измерение глубины очередей backend.</summary>
	public void ObserveQueueDepth(Func<IEnumerable<Measurement<int>>> observe) =>
		_meter.CreateObservableGauge("universalinput.queue.depth", observe, "{event}", "Глубина очереди backend.");

	public void Dispose() => _meter.Dispose();

	private static KeyValuePair<string, object?> Tag(string name, object? value) => new(name, value);
}
