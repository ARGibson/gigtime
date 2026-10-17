using System.Globalization;
using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;
using JasperFx.Events.Documents;

namespace Gigtime.Server.Tasks;

public static class ListTasksEndpoint
{
	public static async Task<LoadedTaskList> LoadAsync(
		IDocumentReadOperations documents,
		[NotBody] HttpContext context,
		CancellationToken cancellationToken)
	{
		var catalogue = (await documents.LoadAsync<CatalogueView>(CatalogueView.StreamId, cancellationToken))!;
		var query = context.Request.Query;
		var last = 10;

		if (query.TryGetValue("last", out var lastText) &&
				(!int.TryParse(lastText, NumberStyles.None, CultureInfo.InvariantCulture, out last) || last < 1))
		{
			return new(catalogue, [], new("log", new("validation", "last must be a positive integer.")));
		}

		var all = false;

		if (query.TryGetValue("all", out var allText) && !bool.TryParse(allText, out all))
			return new(catalogue, [], new("log", new("validation", "all must be true or false.")));

		var client = query.TryGetValue("client", out var clientKey) ? catalogue.FindClient(clientKey.ToString()) : null;

		if (clientKey.Count > 0 && client is null)
			return new(catalogue, [], new("log", new("not_found", "The client does not exist.")));

		var projects = catalogue.Projects.Values.Where(p => client is null || p.ClientId == client.Id).ToArray();

		if (query.TryGetValue("project", out var projectKey))
		{
			projects = projects.Where(p => p.Key == projectKey.ToString()).ToArray();

			if (projects.Length == 0) return new(catalogue, [], new("log", new("not_found", "The project does not exist.")));
		}

		var ids = projects.Select(p => p.Id).ToHashSet();
		var tasks = await documents.Query<TaskView>().ToListAsync(cancellationToken);

		var selected = tasks
			.Where(t => ids.Contains(t.ProjectId) && (all || t.State != TaskState.Voided))
			.OrderByDescending(t => t.StartedAt)
			.ThenByDescending(t => t.Id)
			.Take(last)
			.ToArray();

		return new(catalogue, selected, null);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedTaskList loaded) => loaded.Error;

	[WolverineGet("/v1/tasks")]
	public static Envelope<TasksDto> Handle([NotBody] LoadedTaskList loaded, DateTimeOffset now)
	{
		return Envelopes.Success("log", new TasksDto(loaded.Tasks.Select(t => TaskResponses.From(t, loaded.Catalogue, now)).ToArray()));
	}
}
