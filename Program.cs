using System;

using Microsoft.EntityFrameworkCore;
using Solver.Models;
using HWChargeOptimizer.Reporter;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


builder.Services.AddDbContext<TodoDb>(opt => opt.UseInMemoryDatabase("TodoList"));
//builder.Services.AddDatabaseDeveloperPageExceptionFilter();
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


app.MapPost("/solver", async(SolverModel todo, TodoDb db) =>
{
    //db.Todos.Add(todo);
    //await db.SaveChangesAsync();

    string json = Newtonsoft.Json.JsonConvert.SerializeObject(todo, Newtonsoft.Json.Formatting.Indented);
    Console.WriteLine(json);


    
    var configBuilder = new ConfigurationBuilder().AddJsonStream(new MemoryStream(System.Text.Encoding.ASCII.GetBytes(json))).Build();
   


    var builder = Host.CreateApplicationBuilder(args);
    builder.Configuration.AddConfiguration(configBuilder);

    builder.Services.AddTransient<ChargeScheduleReporter>();
    var section = builder.Configuration.GetSection("SolverConfig");
    builder.Services.Configure <SolverModel>(section);
    builder.Services.AddOptions();

   
    //builder.Services.AddSingleton<IHomeWizardBatteryController, HomeWizardBatteryController>();
    var reportHost = builder.Build();
    var option1 = reportHost.Services.GetRequiredService < IOptionsMonitor<SolverModel>>();
    var reporter = reportHost.Services.GetRequiredService<ChargeScheduleReporter>();
    await reporter.RunAsync(todo);

    SolverResults res = new SolverResults();
    res.Id = 100;
    res.Name = "test";
    res.IsComplete = true;


    return Results.Created($"/solver/{todo.Id}", res);
});

app.Run();


