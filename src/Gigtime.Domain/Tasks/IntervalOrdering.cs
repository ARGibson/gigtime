namespace Gigtime.Domain.Tasks;

internal static class IntervalOrdering
{
	internal static bool IsValid(IEnumerable<Interval> timeline)
	{
		var intervals = timeline.ToArray();

		if (intervals.Any(interval => interval.End < interval.Start)) return false;

		// #36: zero-length spans remain numbered history, but hold no time and cannot block an edit.
		var visible = intervals
			.Where(interval => interval.End is null || interval.End > interval.Start)
			.OrderBy(interval => interval.Start)
			.ToArray();

		for (var index = 1; index < visible.Length; index++)
		{
			if (visible[index - 1].End is null || visible[index - 1].End > visible[index].Start) return false;
		}

		return true;
	}
}
