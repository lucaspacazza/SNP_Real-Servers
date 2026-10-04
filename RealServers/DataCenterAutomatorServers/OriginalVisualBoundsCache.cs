using System.Collections.Generic;

namespace DataCenterAutomatorServers;

// Unity instance IDs are scene scoped. Keep only values, never native objects.
internal sealed class OriginalVisualBoundsCache<T> where T : struct
{
	private readonly Dictionary<int, T> _bounds = new Dictionary<int, T>();

	public T? Get(int instanceId) => _bounds.TryGetValue(instanceId, out T value) ? value : null;

	public T? Capture(int instanceId, T? originalBounds)
	{
		T? known = Get(instanceId);
		if (known != null || originalBounds == null) return known;
		_bounds[instanceId] = originalBounds.Value;
		return originalBounds;
	}

	public void Clear() => _bounds.Clear();
}
