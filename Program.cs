using System;

using Solver.Models;
using HWChargeOptimizer.Reporter;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var configBuilder = new ConfigurationBuilder()
                   .SetBasePath(Directory.GetCurrentDirectory())
                   .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                   .Build();
builder.Configuration.AddConfiguration(configBuilder);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument(config =>
{
    config.DocumentName = "SolverAPI";
    config.Title = "SolverAPI v1";
    config.Version = "v1";
});

// add our scheduler
builder.Services.AddTransient<ChargeScheduleReporter>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseOpenApi();
    app.UseSwaggerUi(config =>
    {
        config.DocumentTitle = "SolverAPI";
        config.Path = "/swagger";
        config.DocumentPath = "/swagger/{documentName}/swagger.json";
        config.DocExpansion = "list";
    });
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();


app.MapPost("/solver", 
   async (HttpRequest request) =>
{
    try
    {
        SolverModel? todo = await request.ReadFromJsonAsync<SolverModel>();
        if (todo == null)
            throw (new Exception("SolverModel is null!"));
#if DEBUG
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(todo, Newtonsoft.Json.Formatting.Indented);
        Console.WriteLine(json);   
#endif
        var reporter = app.Services.GetRequiredService<ChargeScheduleReporter>();

        SolverResults res = await reporter.RunAsync(todo);
#if DEBUG
        var json2 = Newtonsoft.Json.JsonConvert.SerializeObject(res, Newtonsoft.Json.Formatting.Indented);
        Console.WriteLine(json2); 
#endif
        GC.Collect();
        return Results.Created($"/solver/{todo.Id}", res);
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.Message);  
        SolverResults res = new SolverResults {
            IsComplete = false,
            ResultStatus = ex.Message,
            ChargePrice = 0.0f,
            DischargePrice = 0.0f,
        };
       return Results.Created($"/solver/{0}", res);
    }
});

app.Run();


