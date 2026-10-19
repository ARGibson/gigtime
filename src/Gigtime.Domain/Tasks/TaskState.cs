namespace Gigtime.Domain.Tasks;

public enum TaskState
{
	Missing,
	Running,
	Paused,
	Stopped,
	Voided,
	Submitted,
}
