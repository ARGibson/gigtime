using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Hosting;

public static class CorrelatedEvents
{
	public static object[] Wrap(IEnumerable<object> events, string correlationId)
	{
		return events.Select(e => Wrap(e, correlationId)).Cast<object>().ToArray();
	}

	private static IEvent Wrap(object value, string correlationId)
	{
		IEvent wrapped = value switch
		{
			DefaultsChanged e => new Event<DefaultsChanged>(e),
			ClientAdded e => new Event<ClientAdded>(e),
			ClientChanged e => new Event<ClientChanged>(e),
			ClientArchived e => new Event<ClientArchived>(e),
			ProjectAdded e => new Event<ProjectAdded>(e),
			ProjectChanged e => new Event<ProjectChanged>(e),
			ProjectArchived e => new Event<ProjectArchived>(e),
			TaskRecorded e => new Event<TaskRecorded>(e),
			IntervalAmended e => new Event<IntervalAmended>(e),
			TaskReassigned e => new Event<TaskReassigned>(e),
			TaskRefChanged e => new Event<TaskRefChanged>(e),
			TaskVoided e => new Event<TaskVoided>(e),
			TaskStarted e => new Event<TaskStarted>(e),
			TaskPaused e => new Event<TaskPaused>(e),
			TaskResumed e => new Event<TaskResumed>(e),
			TaskStopped e => new Event<TaskStopped>(e),
			OpenTaskSet e => new Event<OpenTaskSet>(e),
			OpenTaskCleared e => new Event<OpenTaskCleared>(e),
			_ => throw new ArgumentException("Unsupported service event.", nameof(value)),
		};

		wrapped.CorrelationId = correlationId;

		return wrapped;
	}
}
