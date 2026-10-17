using JasperFx;
using JasperFx.CodeGeneration;
using Wolverine;
using Wolverine.Http;

var builder = WebApplication.CreateBuilder(args);
builder.Host.ApplyJasperFxExtensions();

builder.Host.UseWolverine(options =>
{
	options.Durability.Mode = DurabilityMode.Solo;
	options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
});

builder.Services.AddWolverineHttp();
var app = builder.Build();
app.MapWolverineEndpoints();

return await app.RunJasperFxCommands(args);

public partial class Program;
