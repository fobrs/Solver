namespace Solver.Models;


public class SolverModel
{   public int Id
    {
        get; set;
    }
    public required SolverConfig SolverConfig { get; set; }
}

public class SolverConfig
{
    public string? Name { get; set; }
    public bool IsComplete { get; set; }
    public float DefaultConsumptionWithSolar  { get; set; }
    public float MaxDischarge { get; set; }
    public bool UseSolarPowerOnly {get; set;}
    public bool Pv90 { get; set; }
    public float Taxes { get; set; }
    public required BatteryConfiguration BatteryConfiguration { get; set; }
    public List<Tariff>? Tariffs { get; set; }
}

public class BatteryConfiguration
{
    public required List<Battery> Batteries { get; set; }
    public required double MaxChargeRateKWh { get; set; }
    public required double MaxDischargeRateKWh { get; set; }
    public required double ChargingEfficiency { get; set; }
    public required double DischargingEfficiency { get; set; }

    /// <summary>
    /// Stores the latest battery mode.
    /// Possible values: 'zero', 'to_full', 'standby'
    /// </summary>
    public string? BatteryMode { get; set; }
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
    [Newtonsoft.Json.JsonProperty("Date", Required = Newtonsoft.Json.Required.Default, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    [Newtonsoft.Json.JsonConverter(typeof(DateFormatConverter))]
    public DateTimeOffset Date { get; set; }
    [Newtonsoft.Json.JsonProperty("Price", Required = Newtonsoft.Json.Required.Default, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    //[Newtonsoft.Json.JsonProperty("Price")]
    public double Price { get; set; }
    public double PriceExported { get; set; } = 0; // calculated
    public bool PricePredicted { get; set; } = false; // indicate if price is from price forecast data
    public double Pv { get; set; }
    public double Pv90 { get; set; }
    public double PvMinUsed { get; set; }
    public double Consumption { get; set; }
    public double ConsumptionStDev { get; set; }
    public float Part_of_hour { get; set; } // 1 is 1 hour intrevals, 4 is 15 minute intervals, we be filled in when used

}

public struct BatteryMode
{
    /// <summary>
    /// NOM modus, battery tries to keep house at 0 kWh consumption.
    /// </summary>
    public const string Zero = "zero";

    /// <summary>
    /// Forced full charge mode, battery will charge to 100% regardless of consumption.
    /// </summary>
    public const string ToFull = "to_full";

    /// <summary>
    /// Battery will not charge or discharge, it will only keep the current state.
    /// </summary>
    public const string Standby = "standby";
}
public class Result
{
    [Newtonsoft.Json.JsonProperty("Date", Required = Newtonsoft.Json.Required.Default, NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    [Newtonsoft.Json.JsonConverter(typeof(DateFormatConverter))]
    public DateTimeOffset Date { get; set; }

    public float ChargeAmount { get; set; }
    public float DischargeAmount { get; set; }

    public string? BatteryMode { get; set; }

    public float SoC { get; set; }

    public bool PricePredicted { get; set; } // if true Price is price prediction past day ahead prices
    public double Price { get; set; }
    public float Part_of_hour { get; set; } // 1 is 1 hour intrevals, 4 is 15 minute intervals, we be filled in when used

}

public class SolverResults
{
    public int Id
    {
        get; set;
    }
    public string? Name { get; set; }
    public bool IsComplete { get; set; }

    public string? ResultStatus { get; set; }

    public float ChargePrice { get; set; }
    public float DischargePrice { get; set; }

    public List<Result>? Results { get; set; }
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

