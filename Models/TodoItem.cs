namespace Solver.Models;


public class SolverModel
{
    public int Id { get; set; }
    public required SolverConfig SolverConfig { get; set; }
}

public class SolverConfig
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public bool IsComplete { get; set; }
    public required BatteryConfiguration BatteryConfiguration { get; set; }
    public List<Tariff>? Tariffs { get; set; }
    public List<Energy1>? ExpectedProduction { get; set; }
    public List<Energy2>? ExpectedConsumption { get; set; }
}




public class BatteryConfiguration
{
     public int Id { get; set; }
    public required List<Battery> Batteries { get; set; }
    public required double MaxChargeRateKWh { get; set; }
    public required double MaxDischargeRateKWh { get; set; }
    public required double ChargingEfficiency { get; set; }
    public required double DischargingEfficiency { get; set; }
}

public class Battery
{
     public int Id { get; set; }
    /// <summary>
    /// The capacity in kWh of the battery.
    /// </summary>
    public required double CapacityKWh { get; set; }
    /// <summary>
    /// The latest state of charge in kWh. This is stored at runtime.</summary>
    public double? StateOfChargePercentage { get; set; }
    /// <summary>
    /// Date and time when the battery SoC was last updated. This is stored at runtime.
    /// </summary>
    public DateTimeOffset? LastUpdated { get; set; }
}



public class Tariff
{
     public int Id { get; set; }
    [Newtonsoft.Json.JsonProperty("Date", Required = Newtonsoft.Json.Required.Default, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    [Newtonsoft.Json.JsonConverter(typeof(DateFormatConverter))]
    public DateTimeOffset Date { get; set; }
    [Newtonsoft.Json.JsonProperty("Price", Required = Newtonsoft.Json.Required.Default, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    //[Newtonsoft.Json.JsonProperty("Price")]
    public double Price { get; set; }
    public double pv { get; set; }
    public float part_of_hour { get; set; }

    //[Newtonsoft.Json.JsonProperty("dummy")]
    //public double Price { get; set;  }
}


public class Energy1
{
    public int Id { get; set; }
    [Newtonsoft.Json.JsonProperty("Date", Required = Newtonsoft.Json.Required.Default, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    [Newtonsoft.Json.JsonConverter(typeof(DateFormatConverter))]   
    public DateTimeOffset Date { get; set; }
    public double Power { get; set; }
}
public class Energy2
{
    public int Id { get; set; }
    [Newtonsoft.Json.JsonProperty("Date", Required = Newtonsoft.Json.Required.Default, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    [Newtonsoft.Json.JsonConverter(typeof(DateFormatConverter))]
    public DateTimeOffset Date { get; set; }
    public double Power { get; set; }
}

public class SolverResults
{
    public int Id
    {
        get; set;
    }
    public string? Name { get; set; }
    public bool IsComplete { get; set; }
}


internal class DateFormatConverter : Newtonsoft.Json.Converters.IsoDateTimeConverter
{
    public DateFormatConverter()
    {
        DateTimeFormat = "yyyy-MM-ddTHH:mm:ssZ"; // ISO 8601 format
    }

    public override void WriteJson(Newtonsoft.Json.JsonWriter writer, object? value, Newtonsoft.Json.JsonSerializer serializer)
    {
        if (value is DateTimeOffset dateTime)
        {
            writer.WriteValue(dateTime.ToString(DateTimeFormat));
        }
        else
        {
            base.WriteJson(writer, value, serializer);
        }
    }
}

