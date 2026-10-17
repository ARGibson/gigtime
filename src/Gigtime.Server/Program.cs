using System.Text.Json;
using Gigtime.Platform;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ApplyJasperFxExtensions();
builder.Services.TryAddSingleton<IPlatformPaths, LinuxPlatformPaths>();
builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.ConfigureHttpJsonOptions(options =>
{
	options.SerializerOptions.RespectRequiredConstructorParameters = true;
	options.SerializerOptions.RespectNullableAnnotations = true;
	options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
	options.SerializerOptions.Converters.Add(new UtcInstantConverter());
});

builder.Services.AddExceptionHandler<CommandExceptions>();
builder.Services.AddSingleton<IProblemDetailsWriter, EnvelopeProblems>();
builder.Services.AddProblemDetails();
builder.Services.AddGigtimeStore();
builder.ListenOnGigtimeSocket();

builder.Host.UseWolverine(options =>
{
	options.Durability.Mode = DurabilityMode.Solo;
	options.Policies.AutoApplyTransactions();
	options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
});

builder.Services.AddWolverineHttp();
var app = builder.Build();
app.UseExceptionHandler(_ => { });

app.UseStatusCodePages(async context =>
{
	var code = context.HttpContext.Response.StatusCode == 404 ? "not_found" : "validation";
	await new EnvelopeResult("request", new(code, "The request could not be handled.")).ExecuteAsync(context.HttpContext);
});

app.Use(async (context, next) =>
{
	var correlation = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
	context.TraceIdentifier = string.IsNullOrWhiteSpace(correlation) ? Guid.NewGuid().ToString("N") : correlation;
	context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;
	await next(context);
});

app.MapWolverineEndpoints(options =>
{
	options.AddParameterHandlingStrategy(new ClockParameters());
	options.RejectUnparseableQueryValues = true;
});

app.RestrictSocket();

return await app.RunJasperFxCommands(args);

public partial class Program;
