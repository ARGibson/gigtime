using System.CommandLine;
using Gigtime.Contracts;
using Gigtime.Contracts.Catalogue;
using static Gigtime.Cli.CommandOptions;

namespace Gigtime.Cli;

internal static class CatalogueCommands
{
	internal static void Configure(RootCommand root, CliExchange exchange)
	{
		var defaults = Add(root, "defaults", "Global time tracking defaults.");
		var show = Add(defaults, "show", "Show defaults.");
		show.SetAction((r, ct) => exchange.GetAsync(r, "defaults show", "/v1/defaults", GigtimeJsonContext.Default.DefaultsResponse, ct));
		var set = Add(defaults, "set", "Change defaults.");
		Text(set, "timezone");
		Text(set, "week-starts");
		Text(set, "rounding");

		set.SetAction((r, ct) => exchange.WriteAsync(r,
			"defaults set",
			HttpMethod.Post,
			"/v1/defaults",
			new SetDefaultsRequest(r.GetValue<string>("--timezone"), r.GetValue<string>("--week-starts"), r.GetValue<string>("--rounding")),
			GigtimeJsonContext.Default.SetDefaultsRequest,
			GigtimeJsonContext.Default.DefaultsResponse,
			ct));

		AddClients(root, exchange);
		AddProjects(root, exchange);
	}

	private static void AddClients(RootCommand root, CliExchange exchange)
	{
		var client = Add(root, "client", "Manage clients.");
		var add = Add(client, "add", "Add a client.");
		Argument(add, "key");
		Text(add, "name", true);
		Text(add, "currency", true);
		Text(add, "timezone");
		Text(add, "rounding");

		add.SetAction((r, ct) => exchange.WriteAsync(r,
			"client add",
			HttpMethod.Post,
			"/v1/clients",
			new AddClientRequest(r.GetValue<string>("key")!,
				r.GetValue<string>("--name")!,
				r.GetValue<string>("--currency")!,
				r.GetValue<string>("--timezone"),
				r.GetValue<string>("--rounding")),
			GigtimeJsonContext.Default.AddClientRequest,
			GigtimeJsonContext.Default.ClientResponse,
			ct));

		var set = Add(client, "set", "Change a client.");
		Argument(set, "key");
		Text(set, "key");
		Text(set, "name");
		Text(set, "currency");
		Exclusive(set, "timezone", "clear-timezone");
		Exclusive(set, "rounding", "clear-rounding");

		set.SetAction((r, ct) => exchange.WriteAsync(r,
			"client set",
			HttpMethod.Patch,
			"/v1/clients/" + Segment(r.GetValue<string>("key")!),
			new ChangeClientRequest(r.GetValue<string>("--key"),
				r.GetValue<string>("--name"),
				r.GetValue<string>("--currency"),
				r.GetValue<string>("--timezone"),
				r.GetValue<bool>("--clear-timezone"),
				r.GetValue<string>("--rounding"),
				r.GetValue<bool>("--clear-rounding")),
			GigtimeJsonContext.Default.ChangeClientRequest,
			GigtimeJsonContext.Default.ClientResponse,
			ct));

		var list = Add(client, "list", "List clients and resolved settings.");
		Flag(list, "all");

		list.SetAction((r, ct) => exchange.GetAsync(r,
			"client list",
			"/v1/clients" + Query(("all", r.GetValue<bool>("--all") ? "true" : null)),
			GigtimeJsonContext.Default.ClientsResponse,
			ct));

		var archive = Add(client, "archive", "Archive a client and its projects.");
		Argument(archive, "key");

		archive.SetAction((r, ct) => exchange.SendAsync(r,
			"client archive",
			HttpMethod.Post,
			"/v1/clients/" + Segment(r.GetValue<string>("key")!) + "/archive",
			null,
			GigtimeJsonContext.Default.AcknowledgementResponse,
			ct));
	}

	private static void AddProjects(RootCommand root, CliExchange exchange)
	{
		var project = Add(root, "project", "Manage projects.");
		var add = Add(project, "add", "Add a project.");
		Argument(add, "client");
		Argument(add, "key");
		Text(add, "name");
		Text(add, "rate");
		Text(add, "rounding");

		add.SetAction((r, ct) => exchange.WriteAsync(r,
			"project add",
			HttpMethod.Post,
			"/v1/clients/" + Segment(r.GetValue<string>("client")!) + "/projects",
			new AddProjectRequest(r.GetValue<string>("key")!,
				r.GetValue<string>("--name"),
				r.GetValue<string>("--rate"),
				r.GetValue<string>("--rounding")),
			GigtimeJsonContext.Default.AddProjectRequest,
			GigtimeJsonContext.Default.ProjectResponse,
			ct));

		var set = Add(project, "set", "Change a project.");
		Argument(set, "client");
		Argument(set, "key");
		Text(set, "key");
		Text(set, "name");
		Exclusive(set, "rate", "clear-rate");
		Exclusive(set, "rounding", "clear-rounding");

		set.SetAction((r, ct) => exchange.WriteAsync(r,
			"project set",
			HttpMethod.Patch,
			ProjectPath(r),
			new ChangeProjectRequest(Key: r.GetValue<string>("--key"),
				Name: r.GetValue<string>("--name"),
				Rate: r.GetValue<string>("--rate"),
				ClearRate: r.GetValue<bool>("--clear-rate"),
				Rounding: r.GetValue<string>("--rounding"),
				ClearRounding: r.GetValue<bool>("--clear-rounding")),
			GigtimeJsonContext.Default.ChangeProjectRequest,
			GigtimeJsonContext.Default.ProjectResponse,
			ct));

		var list = Add(project, "list", "List projects and resolved settings.");
		Argument(list, "client", true);
		Flag(list, "all");

		list.SetAction((r, ct) => exchange.GetAsync(r,
			"project list",
			"/v1/projects" + Query(("client", r.GetValue<string>("client")), ("all", r.GetValue<bool>("--all") ? "true" : null)),
			GigtimeJsonContext.Default.ProjectsResponse,
			ct));

		var archive = Add(project, "archive", "Archive a project.");
		Argument(archive, "client");
		Argument(archive, "key");

		archive.SetAction((r, ct) => exchange.SendAsync(r,
			"project archive",
			HttpMethod.Post,
			ProjectPath(r) + "/archive",
			null,
			GigtimeJsonContext.Default.AcknowledgementResponse,
			ct));
	}

	private static string ProjectPath(ParseResult parsed)
	{
		return "/v1/clients/" + Segment(parsed.GetValue<string>("client")!) + "/projects/" + Segment(parsed.GetValue<string>("key")!);
	}
}
