using System;

using Solver.Models;
using HWChargeOptimizer.Reporter;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument(config =>
{
    config.DocumentName = "SolverAPI";
    config.Title = "SolverAPI v1";
    config.Version = "v1";
});



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

app.MapPost("/solver", async(SolverModel todo) =>
{
    string json = Newtonsoft.Json.JsonConvert.SerializeObject(todo, Newtonsoft.Json.Formatting.Indented);
   // Console.WriteLine(json);   

    var configBuilder = new ConfigurationBuilder().Build();
    var builder = Host.CreateApplicationBuilder(args);
    builder.Configuration.AddConfiguration(configBuilder);
    builder.Services.AddTransient<ChargeScheduleReporter>();   
    var reportHost = builder.Build();
    var reporter = reportHost.Services.GetRequiredService<ChargeScheduleReporter>();
    SolverResults res = await reporter.RunAsync(todo);

    return Results.Created($"/solver/{todo.Id}", res);
});

app.Run();


