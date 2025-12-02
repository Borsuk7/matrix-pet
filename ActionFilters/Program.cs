using ActionFilters.Filters;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<ExecutionLoggingFilter>();

builder.Services.AddControllers(/*options => { options.Filters.Add<ExecutionLoggingFilter>(); }*/);
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
