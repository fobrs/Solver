using System.Reflection;
using System.Diagnostics;
using Google.OrTools.LinearSolver;

using Solver.Models;
using Microsoft.Extensions.Options;

namespace HWChargeOptimizer.Reporter;



public class ScheduleVariables
{
    // Solver variables
    public Dictionary<DateTimeOffset, Variable> ChargeAmount { get; } = new();
    public Dictionary<DateTimeOffset, Variable> DischargeAmount { get; } = new();
    public Dictionary<DateTimeOffset, Variable> StateOfCharge { get; } = new();
    public Dictionary<DateTimeOffset, Variable> IsCharging { get; } = new();

    // Input parameters
    public List<Tariff> Tariffs { get; init; } = [];
    public double MaxChargingRate { get; init; }
    public double MaxDischargingRate { get; init; }
    public double MaxDischargingRateCfg { get; init; }
    public double CombinedBatteryCapacity { get; init; }
    public double CurrentStateOfCharge { get; init; }
    public double ChargingEfficiency { get; init; }
    public double DischargingEfficiency { get; init; }
    public double DefaultConsumptionWithSolar { get; set; }
}



public static class OptimizeSchedule
{
    public static bool charge_with_solar_only = false;
    // Use a slightly increased factor to favor discharging on financially interesting moments
    private const double DischargeFactor = 1.01;

    public static float taxes;

    public static
    Google.OrTools.LinearSolver.Solver.ResultStatus
    Calculate(Google.OrTools.LinearSolver.Solver solver, ScheduleVariables scheduleVariables)
    {
        // Input validation
        if (scheduleVariables.Tariffs.Count == 0 || scheduleVariables.CombinedBatteryCapacity <= 0 || scheduleVariables.CurrentStateOfCharge < 0)
        {
            throw new ArgumentException("Invalid input parameters for optimization");
        }

        // Objective: minimize cost (negative discharge value = maximize profit)
        var objective = solver.Objective();

       
        // Create constraints and objective coefficients
        foreach (var tariff in scheduleVariables.Tariffs)
        {
            if (charge_with_solar_only)
                scheduleVariables.ChargeAmount[tariff.Date] = solver.MakeNumVar(0.0,
                    Math.Min(((tariff.PvMinUsed) >= 0) ?
                        (tariff.PvMinUsed) : 0, scheduleVariables.MaxChargingRate),
                    $"charge_{tariff.Date}");

            else
            {
                var minV = 0.0;
                //if (tariff.PvMinUsed >= 0)
                //    minV = tariff.PvMinUsed;
                //if (minV > scheduleVariables.MaxChargingRate)
                //    minV = scheduleVariables.MaxChargingRate;
                scheduleVariables.ChargeAmount[tariff.Date] = solver.MakeNumVar(
                    minV,
                scheduleVariables.MaxChargingRate, $"charge_{tariff.Date}");
            }

            var maxD = (tariff.Consumption + tariff.ConsumptionStDev) / 1000.0f + scheduleVariables.MaxDischargingRate;
            maxD = Math.Min(maxD, scheduleVariables.MaxDischargingRateCfg);
            scheduleVariables.DischargeAmount[tariff.Date] = solver.MakeNumVar(0.0, maxD, $"discharge_{tariff.Date}");
            scheduleVariables.StateOfCharge[tariff.Date] = solver.MakeNumVar(0.0, scheduleVariables.CombinedBatteryCapacity, $"soc_{tariff.Date}");
            scheduleVariables.IsCharging[tariff.Date] = solver.MakeIntVar(0, 1, $"isCharging_{tariff.Date}");

            // Always charge at maximum rate during negative tariffs
            if (tariff.Price < 0)
            {
                solver.Add(scheduleVariables.ChargeAmount[tariff.Date] == scheduleVariables.MaxChargingRate);
            }
            //else
            {


                // Prevent charging and discharging at the same time
                if (charge_with_solar_only)
                    solver.Add(scheduleVariables.ChargeAmount[tariff.Date] <= (((tariff.PvMinUsed) >= 0) ? (tariff.PvMinUsed) : 0) * scheduleVariables.IsCharging[tariff.Date]);
                else
                {
                    if (tariff.PvMinUsed > 0)
                        solver.Add(scheduleVariables.ChargeAmount[tariff.Date] <= (((tariff.PvMinUsed) >= 0) ? (tariff.PvMinUsed) : 0) * scheduleVariables.IsCharging[tariff.Date]);
                    else
                        solver.Add(scheduleVariables.ChargeAmount[tariff.Date] <= scheduleVariables.MaxChargingRate * scheduleVariables.IsCharging[tariff.Date]);
                }
            }
                
            solver.Add(scheduleVariables.DischargeAmount[tariff.Date] <= ((tariff.Consumption+tariff.ConsumptionStDev)/1000.0 + scheduleVariables.MaxDischargingRate) * (1 - scheduleVariables.IsCharging[tariff.Date]));
            // Prevent charging and discharging at the same time

            // Prevent discharging if SoC is 0
            solver.Add(scheduleVariables.DischargeAmount[tariff.Date] <= tariff.Part_of_hour * scheduleVariables.StateOfCharge[tariff.Date]);
            // solver.Add(scheduleVariables.DischargeAmount[tariff.Date] >= 0.3);

            // Cost of charging and value of discharging

            //if (false /*charge_with_solar_only*/)
            //    objective.SetCoefficient(scheduleVariables.ChargeAmount[tariff.Date], (tariff.PvMinUsed) >= 0 ? 0.0 : (tariff.Price - taxes));
            //else

            if (false /*charge_with_solar_only*/)
            {
                objective.SetCoefficient(scheduleVariables.DischargeAmount[tariff.Date], (tariff.PriceExported));
                objective.SetCoefficient(scheduleVariables.ChargeAmount[tariff.Date], -tariff.Price * DischargeFactor);
            }
            else
            {
                if (tariff.PvMinUsed > 0)
                    objective.SetCoefficient(scheduleVariables.ChargeAmount[tariff.Date], tariff.PriceExported );
                else
                     objective.SetCoefficient(scheduleVariables.ChargeAmount[tariff.Date], tariff.Price);
                objective.SetCoefficient(scheduleVariables.DischargeAmount[tariff.Date], -tariff.Price * DischargeFactor);
            }
        }

        // Set initial state of charge
        solver.Add(scheduleVariables.StateOfCharge[scheduleVariables.Tariffs[0].Date] == scheduleVariables.CurrentStateOfCharge);

        // Battery state evolution
        for (var i = 1; i < scheduleVariables.Tariffs.Count; i++)
        {
            var prevHour = scheduleVariables.Tariffs[i - 1].Date;
            var currHour = scheduleVariables.Tariffs[i].Date;
            float delta = 1.0f / scheduleVariables.Tariffs[i-1].Part_of_hour;

            solver.Add(scheduleVariables.StateOfCharge[currHour] == scheduleVariables.StateOfCharge[prevHour]
                + delta * scheduleVariables.ChargeAmount[prevHour] * scheduleVariables.ChargingEfficiency
                - delta * scheduleVariables.DischargeAmount[prevHour] / scheduleVariables.DischargingEfficiency);
        }

        objective.SetMinimization();

#if DEBUG
        var str = solver.ExportModelAsLpFormat(false);
        Console.WriteLine(str);
#endif

        return solver.Solve();
    }
}

public class ChargeScheduleReporter
{
    private const string SystemTimeZone = "W. Europe Standard Time";
    private const double VAT = 1.21;


    // Use a slightly increased factor to avoid floating point precision issues in the solver
    private const double RoundingFactor = 0.01;


    private SolverModel config;

    public ChargeScheduleReporter(IOptionsMonitor<SolverModel> _config)
    {
        config = _config.CurrentValue;
    }

    public async Task<SolverResults> RunAsync(SolverModel todo)
    {
        await Task.Delay(1);
        SolverResults res = new SolverResults();
        res.Name = "Solver";
        res.IsComplete = false;

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(SystemTimeZone);

        //Console.WriteLine("Starting calculation of optimal charging schedule...");

        var currentUtcDateTime = DateTimeOffset.UtcNow;
        currentUtcDateTime = new DateTimeOffset(currentUtcDateTime.Year, currentUtcDateTime.Month, currentUtcDateTime.Day, currentUtcDateTime.Hour, 0, 0, currentUtcDateTime.Offset);

        var cfg = todo;
        var batteryCfg = cfg.SolverConfig.BatteryConfiguration;
        //var p1 = cfg.Homewizard.P1;

        //var tariffs = cfg.SolverConfig.Tariffs?.Where(t => t.Date >= currentUtcDateTime).ToList() ?? [];
        //if (tariffs.Count == 0)
        //{
        //    Console.WriteLine("No current tariffs available in the Zonneplan tariff list.");
        //    return;
        //}
        var tariffs = cfg.SolverConfig.Tariffs;
        if (tariffs == null || tariffs.Count == 0)
        {
            res.ResultStatus = "No current tariffs available in the tariff list.";
            Console.WriteLine(res.ResultStatus);
            return res;
        }
        // double last_price = 0.0;
        // for (int i = 0; i < tariffs.Count; i++)
        //{
        //  if (tariffs[i].Price_ != null)
        // {
        //    tariffs[i].Price = last_price = tariffs[i].Price_ ?? last_price;
        // }
        //else
        //   tariffs[i].Price = last_price;
        //}

        var maxChargingRate = batteryCfg.MaxChargeRateKWh;
        var maxDischargingRateCfg = batteryCfg.MaxDischargeRateKWh;
        var maxDischargingRate = cfg.SolverConfig.MaxDischarge / 1000.0;
        var defaultConsumptionWithSolar = cfg.SolverConfig.DefaultConsumptionWithSolar / 1000.0;

        var chargingEfficiency = batteryCfg.ChargingEfficiency;
        var dischargingEfficiency = batteryCfg.DischargingEfficiency;

        var combinedBatteryCapacity = batteryCfg.Batteries.Sum(s => s.CapacityKWh);
        var currentStateOfCharge = batteryCfg.Batteries.Select((soc, index) => soc.StateOfChargePercentage * batteryCfg.Batteries[index].CapacityKWh / 100.0).Sum();

        if (currentStateOfCharge is null)
        {
            res.ResultStatus = "No current state of charge is stored in the configuration file. Please let the application read the state of charge from the Homewizard battery first before running the report or chart function.";
            Console.WriteLine(res.ResultStatus);
            return res;
        }
        // fill in part_of_hour
        for (var i = 1; i < tariffs.Count; i++)
        {
            var prevHour = tariffs[i - 1].Date;
            var currHour = tariffs[i].Date;
            float delta = (currHour.ToUnixTimeSeconds() - prevHour.ToUnixTimeSeconds()) / 3600.0f;
            tariffs[i - 1].Part_of_hour = 1.0f / delta;
        }
        tariffs[tariffs.Count - 1].Part_of_hour = tariffs[tariffs.Count - 2].Part_of_hour;

        for (var i = 0; i < tariffs.Count; i++)
        {
            tariffs[i].PriceExported = tariffs[i].Price;
            if (cfg.SolverConfig.Taxes >= 0)
            {
                tariffs[i].PriceExported = (tariffs[i].Price - cfg.SolverConfig.Taxes);
                if (defaultConsumptionWithSolar == 0)
                    tariffs[i].PvMinUsed = tariffs[i].Pv - (tariffs[i].Consumption + tariffs[i].ConsumptionStDev) / 1000.0;
                else
                    tariffs[i].PvMinUsed = tariffs[i].Pv - defaultConsumptionWithSolar;
                if (tariffs[i].PvMinUsed < 0.0)
                    tariffs[i].PvMinUsed = 0.0;
            }
        }


        var scheduleVariables = new ScheduleVariables
        {
            Tariffs = tariffs,
            MaxChargingRate = maxChargingRate,
            MaxDischargingRate = maxDischargingRate,
            MaxDischargingRateCfg = maxDischargingRateCfg,
            CombinedBatteryCapacity = combinedBatteryCapacity,
            CurrentStateOfCharge = (double)currentStateOfCharge,
            ChargingEfficiency = chargingEfficiency,
            DischargingEfficiency = dischargingEfficiency,
            DefaultConsumptionWithSolar = defaultConsumptionWithSolar
        };

        // var currentHousePowerUsage = await batteryController.GetLatestPowerMeasurementAsync();

        // lowest and highest tariff today
        //var todayTariffs = tariffs.Where(t => TimeZoneInfo.ConvertTime(t.Date, timeZone).Date == TimeZoneInfo.ConvertTime(currentUtcDateTime.Date, timeZone)).ToList();
        var lowestTariff = tariffs.Min(t => t.Price);
        var highestTariff = tariffs.Max(t => t.Price);
        var averageTariff = tariffs.Average(t => t.Price);

        //var currentTariff = tariffs.SingleOrDefault(s => s.Date == currentUtcDateTime);
        //if (currentTariff == null)
        //{
        //    Console.WriteLine($"No current tariff found for the current hour {currentUtcDateTime}. This should never happen.");
        //    return;
        //}

        var currentBatteryMode = batteryCfg.BatteryMode;

        Console.WriteLine("-----------------------------------------------------------");
        Console.WriteLine($"Name:                                         {cfg.SolverConfig.Name}");
        Console.WriteLine($"Current battery mode:                         {currentBatteryMode}");
        Console.WriteLine($"Total battery capacity:                       {combinedBatteryCapacity} kWh");
        Console.WriteLine($"Current state of charge (combined):           {currentStateOfCharge:F4} kWh");
        //Console.WriteLine($"Current house power consumption / production: {currentHousePowerUsage} Watt");
        //Console.WriteLine($"Current tariff:                               {currentTariff.Price:F4} / kWh");
        Console.WriteLine($"Lowest tariff today:                          {lowestTariff:F4} / kWh");
        Console.WriteLine($"Highest tariff today:                         {highestTariff:F4} / kWh");
        Console.WriteLine($"Average tariff today:                         {averageTariff:F4} / kWh");
        Console.WriteLine($"Charging efficiency:                          {chargingEfficiency * 100} %");
        Console.WriteLine($"Discharging efficiency:                       {dischargingEfficiency * 100} %");
        Console.WriteLine("-----------------------------------------------------------");

        Console.WriteLine("Starting calculation of optimal charging schedule...");
        Stopwatch stopWatch = new Stopwatch();
        stopWatch.Start();
        // Create the solver that will calculate the most efficient charging
        using var solver = Google.OrTools.LinearSolver.Solver.CreateSolver("GLOP") ?? throw new InvalidOperationException("Failed to create SCIP solver");
        // Sets a time limit of 30 seconds.

        solver.SetTimeLimit(30 * 1000);
        OptimizeSchedule.taxes = cfg.SolverConfig.Taxes;
        OptimizeSchedule.charge_with_solar_only = cfg.SolverConfig.UseSolarPowerOnly;

#if DEBUG
        // logging during solving
        solver.EnableOutput();
#endif
        var resultStatus = OptimizeSchedule.Calculate(solver, scheduleVariables);

        stopWatch.Stop();
        TimeSpan ts = stopWatch.Elapsed;
        Console.WriteLine("-----------------------------------------------------------");
        Console.WriteLine("finsihed after {0:F2} ms, {1}", ts.TotalMilliseconds, resultStatus);

        // Display results
        if (resultStatus is Google.OrTools.LinearSolver.Solver.ResultStatus.OPTIMAL or Google.OrTools.LinearSolver.Solver.ResultStatus.FEASIBLE)
        {
            //Console.WriteLine("Resultstatus: {0}", resultStatus.ToString());
            //Console.WriteLine("-----------------------------------------------------------");
            //Console.WriteLine("Local time  | C | D |  CQ   |  DQ   |  SoC | Tariff | pv");


            res.ResultStatus = resultStatus.ToString();
            res.Results = new List<Result>();

            foreach (var tariff in tariffs)
            {
                var startCharging = false;
                var startDischarging = false;
                var charge = Math.Round(scheduleVariables.ChargeAmount[tariff.Date].SolutionValue(), 2);
                var discharge = Math.Round(scheduleVariables.DischargeAmount[tariff.Date].SolutionValue(), 2);
                var soc = Math.Round(scheduleVariables.StateOfCharge[tariff.Date].SolutionValue(), 2);

                var chargingStatus = (charge > discharge + RoundingFactor) ? "Y" : " ";
                var dischargingStatus = discharge > (charge + RoundingFactor) ? "Y" : " ";
                if (chargingStatus == "Y")
                    startCharging = true;
                else if (dischargingStatus == "Y")
                    startDischarging = true;
                var bat_mode = BatteryMode.Standby;
                if (startCharging)
                    bat_mode = BatteryMode.ToFull;
                else if (startDischarging)
                    bat_mode = BatteryMode.Zero;

                res.Results.Add(new Result
                {
                    Date = tariff.Date,
                    Price = tariff.Price,
                    Part_of_hour = tariff.Part_of_hour,
                    PricePredicted = tariff.PricePredicted,
                    ChargeAmount = (float)charge,
                    DischargeAmount = (float)discharge,
                    SoC = (float)(soc / combinedBatteryCapacity),
                    BatteryMode = bat_mode
                });


                //Console.WriteLine(
                //    $"{TimeZoneInfo.ConvertTime(tariff.Date, timeZone):dd/MM HH:mm} | {chargingStatus} | {dischargingStatus} | {charge,5:F2} | {discharge,5:F2} | {soc,3:F2} | {tariff.Price:F5}| {tariff.Pv:F5}");
            }

            // Calculate net cost
            var totalCost = 0.0;
            var totalValue = 0.0;
            for (int i = 0; i < tariffs.Count; i++)
            {

                //totalCost += delta * ((tariffs[i].Pv - 0.300) > 0 ? 0.0 : tariffs[i].Price) * scheduleVariables.ChargeAmount[tariffs[i].Date].SolutionValue();
                totalCost += ((tariffs[i].PvMinUsed > 0) ? tariffs[i].PriceExported : tariffs[i].Price) * scheduleVariables.ChargeAmount[tariffs[i].Date].SolutionValue() / tariffs[i].Part_of_hour;
                totalValue += tariffs[i].Price * scheduleVariables.DischargeAmount[tariffs[i].Date].SolutionValue() / tariffs[i].Part_of_hour;
            }

            res.IsComplete = true;
            res.ChargePrice = (float)totalCost;
            res.DischargePrice = (float)totalValue;

            //Console.WriteLine("-----------------------------------------------------------");
            //Console.WriteLine($"Total charging cost:   € {totalCost,5:F2}", totalCost);
            //Console.WriteLine($"Total discharge cost:  € {totalValue,5:F2}", totalValue);
            Console.WriteLine($"Net cost:              € {totalCost - totalValue,5:F2}");
            Console.WriteLine("-----------------------------------------------------------");

        }
        else
        {
            res.ResultStatus = "No solution found. Setting battery to zero charging mode.";
#if DEBUG
            solver.VerifySolution(-1.0, true);
#endif
        }
        return res;
    }
   
 }