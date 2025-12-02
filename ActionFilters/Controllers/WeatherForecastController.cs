using ActionFilters.Filters;

using Microsoft.AspNetCore.Mvc;

namespace ActionFilters.Controllers;

[ApiController]
[Route("[controller]")]
public class WeatherForecastController : ControllerBase
{
    private static readonly string[] _summaries =
    [
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    ];

    [HttpGet(Name = "GetWeatherForecast")]
    [ServiceFilter<ExecutionLoggingFilter>]
    public async Task<IEnumerable<WeatherForecast>> Get()
    {
        await Task.Delay(Random.Shared.Next(0, 500));

        if (Random.Shared.Next(0, 5) == 1)
        {
            throw new Exception("Test");
        }

        return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = _summaries[Random.Shared.Next(_summaries.Length)]
            })
            .ToArray();
    }
}
