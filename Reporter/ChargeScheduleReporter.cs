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
    public double CombinedBatteryCapacity { get; init; }
    public double CurrentStateOfCharge { get; init; }
    public double ChargingEfficiency { get; init; }
    public double DischargingEfficiency { get; init; }
}



public static class OptimizeSchedule
{
    public const bool charge_with_solar_only = true;
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
                    Math.Min( ((tariff.pv - 0.300) >= 0) ? (tariff.pv - 0.300) : 0 , scheduleVariables.MaxChargingRate),
                   // ((tariff.pv - 0.300) >= 0) ? (tariff.pv - 0.300) : 0,
                    $"charge_{tariff.Date}");
            
            else
                scheduleVariables.ChargeAmount[tariff.Date] = solver.MakeNumVar(0.0, scheduleVariables.MaxChargingRate, $"charge_{tariff.Date}");   

            scheduleVariables.DischargeAmount[tariff.Date] = solver.MakeNumVar(0.0, scheduleVariables.MaxDischargingRate, $"discharge_{tariff.Date}");
            scheduleVariables.StateOfCharge[tariff.Date] = solver.MakeNumVar(0.0, scheduleVariables.CombinedBatteryCapacity, $"soc_{tariff.Date}");
            scheduleVariables.IsCharging[tariff.Date] = solver.MakeIntVar(0, 1, $"isCharging_{tariff.Date}");

            // Always charge at maximum rate during negative tariffs
            if (tariff.Price < 0)
            {
                solver.Add(scheduleVariables.ChargeAmount[tariff.Date] == scheduleVariables.MaxChargingRate);
            }


            // Prevent charging and discharging at the same time
            if (charge_with_solar_only)
                solver.Add(scheduleVariables.ChargeAmount[tariff.Date] <= (((tariff.pv - 0.300) >= 0) ? (tariff.pv - 0.300) : 0) * scheduleVariables.IsCharging[tariff.Date]);
            else
                solver.Add(scheduleVariables.ChargeAmount[tariff.Date] <= scheduleVariables.MaxChargingRate * scheduleVariables.IsCharging[tariff.Date]);
            solver.Add(scheduleVariables.DischargeAmount[tariff.Date] <= (scheduleVariables.MaxDischargingRate) * (1 - scheduleVariables.IsCharging[tariff.Date]));
            // Prevent charging and discharging at the same time

            // Prevent discharging if SoC is 0
            solver.Add(scheduleVariables.DischargeAmount[tariff.Date] <= tariff.part_of_hour * scheduleVariables.StateOfCharge[tariff.Date]);
            //solver.Add(scheduleVariables.DischargeAmount[tariff.Date] >= 0.3);

            // Cost of charging and value of discharging

            if (false /*charge_with_solar_only*/)
                objective.SetCoefficient(scheduleVariables.ChargeAmount[tariff.Date], (tariff.pv - 0.300) >= 0 ? 0.0 : (tariff.Price - taxes));
            else
                objective.SetCoefficient(scheduleVariables.ChargeAmount[tariff.Date], (tariff.Price - taxes));

            objective.SetCoefficient(scheduleVariables.DischargeAmount[tariff.Date], -tariff.Price * DischargeFactor);
        }

        // Set initial state of charge
        solver.Add(scheduleVariables.StateOfCharge[scheduleVariables.Tariffs[0].Date] == scheduleVariables.CurrentStateOfCharge);

        // Battery state evolution
        for (var i = 1; i < scheduleVariables.Tariffs.Count; i++)
        {
            var prevHour = scheduleVariables.Tariffs[i - 1].Date;
            var currHour = scheduleVariables.Tariffs[i].Date;
            float delta = 1.0f / scheduleVariables.Tariffs[i-1].part_of_hour;

            solver.Add(scheduleVariables.StateOfCharge[currHour] == scheduleVariables.StateOfCharge[prevHour]
                + delta * scheduleVariables.ChargeAmount[prevHour] * scheduleVariables.ChargingEfficiency
                - delta * scheduleVariables.DischargeAmount[prevHour] / scheduleVariables.DischargingEfficiency);
        }

        objective.SetMinimization();

        return solver.Solve();
    }
}

public class ChargeScheduleReporter
{
    private const string SystemTimeZone = "W. Europe Standard Time";
    private const float taxes = 0.1088f;

    // Use a slightly increased factor to avoid floating point precision issues in the solver
    private const double RoundingFactor = 0.01;

    private SolverModel config;

    public ChargeScheduleReporter(IOptionsMonitor<SolverModel> _config)
    {
        config = _config.CurrentValue;
    }

    public async Task RunAsync(SolverModel todo, bool generateChart = false)
    {
        await Task.Delay(1);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(SystemTimeZone);

        Console.WriteLine("Starting calculation of optimal charging schedule...");

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
            Console.WriteLine("No current tariffs available in the tariff list.");
            return;
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
        var maxDischargingRate = batteryCfg.MaxDischargeRateKWh;

        var chargingEfficiency = batteryCfg.ChargingEfficiency;
        var dischargingEfficiency = batteryCfg.DischargingEfficiency;

        var combinedBatteryCapacity = batteryCfg.Batteries.Sum(s => s.CapacityKWh);
        var currentStateOfCharge = batteryCfg.Batteries.Select((soc, index) => soc.StateOfChargePercentage * batteryCfg.Batteries[index].CapacityKWh / 100.0).Sum();

        if (currentStateOfCharge is null)
        {
            Console.WriteLine("No current state of charge is stored in the configuration file. Please let the application read the state of charge from the Homewizard battery first before running the report or chart function.");
            return;
        }

        for (var i = 1; i < tariffs.Count; i++)
        {
            var prevHour = tariffs[i - 1].Date;
            var currHour = tariffs[i].Date;
            float delta = (currHour.ToUnixTimeSeconds() - prevHour.ToUnixTimeSeconds()) / 3600.0f;
            tariffs[i - 1].part_of_hour = 1.0f / delta;
        }
        tariffs[tariffs.Count - 1].part_of_hour = tariffs[tariffs.Count - 2].part_of_hour;


        var scheduleVariables = new ScheduleVariables
        {
            Tariffs = tariffs,
            MaxChargingRate = maxChargingRate,
            MaxDischargingRate = maxDischargingRate,
            CombinedBatteryCapacity = combinedBatteryCapacity,
            CurrentStateOfCharge = (double)currentStateOfCharge,
            ChargingEfficiency = chargingEfficiency,
            DischargingEfficiency = dischargingEfficiency
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

        //var currentBatteryMode = p1.BatteryMode;

        Console.WriteLine("-----------------------------------------------------------");
        //Console.WriteLine($"Current battery mode:                         {currentBatteryMode}");
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
        using var solver = Google.OrTools.LinearSolver.Solver.CreateSolver("SCIP");
        if (solver == null)
        {
            throw new InvalidOperationException("Failed to create SCIP solver");
        }
        // Sets a time limit of 10 seconds.
        solver.SetTimeLimit(30 * 1000);
        OptimizeSchedule.taxes = taxes;

        var resultStatus = OptimizeSchedule.Calculate(solver, scheduleVariables);

        stopWatch.Stop();
        TimeSpan ts = stopWatch.Elapsed;
        Console.WriteLine("-----------------------------------------------------------");
        Console.WriteLine("finsihed after {0:F2} ms", ts.TotalMilliseconds);
        
        // Display results
        if (resultStatus is Google.OrTools.LinearSolver.Solver.ResultStatus.OPTIMAL or Google.OrTools.LinearSolver.Solver.ResultStatus.FEASIBLE)
        {
            Console.WriteLine("Resultstatus: {0}", resultStatus.ToString());
            Console.WriteLine("-----------------------------------------------------------");
            Console.WriteLine("Local time  | C | D |  CQ   |  DQ   |  SoC | Tariff | pv");

            foreach (var tariff in tariffs)
            {
                var charge = Math.Round(scheduleVariables.ChargeAmount[tariff.Date].SolutionValue(), 2);
                var discharge = Math.Round(scheduleVariables.DischargeAmount[tariff.Date].SolutionValue(), 2);
                charge = charge == 0.0 ? 0.0 : charge;
                discharge = discharge == 0.0 ? 0.0 : discharge;

                var soc = Math.Round(scheduleVariables.StateOfCharge[tariff.Date].SolutionValue(), 2);

                var chargingStatus = charge > RoundingFactor ? "Y" : " ";
                var dischargingStatus = discharge > RoundingFactor ? "Y" : " ";

                Console.WriteLine(
                    $"{TimeZoneInfo.ConvertTime(tariff.Date, timeZone):dd/MM HH:mm} | {chargingStatus} | {dischargingStatus} | {charge,5:F2} | {discharge,5:F2} | {soc,3:F2} | {tariff.Price:F5}| {tariff.pv:F5}");
            }

            // Calculate net cost
            var totalCost = 0.0;
            var totalValue = 0.0;
            for (int i = 0; i < tariffs.Count; i++)
            {
                
                //totalCost += delta * ((tariffs[i].pv - 0.300) > 0 ? 0.0 : tariffs[i].Price) * scheduleVariables.ChargeAmount[tariffs[i].Date].SolutionValue();
                totalCost += (tariffs[i].Price - taxes) * scheduleVariables.ChargeAmount[tariffs[i].Date].SolutionValue() / tariffs[i].part_of_hour;
                totalValue += tariffs[i].Price * scheduleVariables.DischargeAmount[tariffs[i].Date].SolutionValue() / tariffs[i].part_of_hour;
            }

            Console.WriteLine("-----------------------------------------------------------");
            Console.WriteLine($"Total charging cost:   € {totalCost,5:F2}", totalCost);
            Console.WriteLine($"Total discharge cost:  € {totalValue,5:F2}", totalValue);
            Console.WriteLine($"Net cost:              € {totalCost - totalValue,5:F2}");
            Console.WriteLine("-----------------------------------------------------------");

                //if (generateChart)
                //    CreateBatterySchedulePlot(tariffs, scheduleVariables.ChargeAmount, scheduleVariables.DischargeAmount, scheduleVariables.StateOfCharge);
        }
        else
        {
            Console.WriteLine("No solution found. Setting battery to zero charging mode.");
        }
    }

    private static void CreateBatterySchedulePlot(List<Tariff> tariffs,
        Dictionary<DateTimeOffset, Variable> chargeAmount,
        Dictionary<DateTimeOffset, Variable> dischargeAmount,
        Dictionary<DateTimeOffset, Variable> stateOfCharge)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(SystemTimeZone);

        // convert tariff times to local time zone for plotting
        foreach (var tariff in tariffs)
        {
            tariff.Date = TimeZoneInfo.ConvertTime(tariff.Date, timeZone);
        }

        // Create arrays for plotting
        var times = new double[tariffs.Count];
        var socValues = new double[tariffs.Count];
        var chargeValues = new double[tariffs.Count];
        var dischargeValues = new double[tariffs.Count];
        var tariffValues = new double[tariffs.Count];
        var timeLabels = new string[tariffs.Count];

        // Fill arrays with data
        for (var i = 0; i < tariffs.Count; i++)
        {
            var tariff = tariffs[i];
            times[i] = i;
            socValues[i] = stateOfCharge[tariff.Date].SolutionValue();
            chargeValues[i] = chargeAmount[tariff.Date].SolutionValue();
            dischargeValues[i] = dischargeAmount[tariff.Date].SolutionValue();
            tariffValues[i] = tariff.Price;
            timeLabels[i] = tariff.Date.ToString("HH");
        }
#if false
        // Create plot
        var plot = new Plot();

        var fontLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new InvalidOperationException("Cannot find executing assembly location."), @"Fonts/Roboto-VariableFont.ttf");
        if (!File.Exists(fontLocation))
            throw new InvalidOperationException($"Font file not found at {fontLocation}. Cannot create chart.");

        Console.WriteLine("Using font file at " + fontLocation);

        // Add a font file to use its typeface for fonts with a given name
        Fonts.AddFontFile(
            name: "Roboto",
            path: Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new InvalidOperationException("Cannot find fonts."), @"Fonts/Roboto-VariableFont.ttf"));

        plot.Font.Set("Roboto");

        // change figure colors for dark mode
        plot.FigureBackground.Color = Color.FromHex("#181818");
        plot.DataBackground.Color = Color.FromHex("#1f1f1f");

        // change axis and grid colors for dark mode
        plot.Axes.Color(Color.FromHex("#d7d7d7"));
        plot.Grid.MajorLineColor = Color.FromHex("#404040");

        // change legend colors for dark mode
        plot.Legend.BackgroundColor = Color.FromHex("#404040").WithAlpha(0.7); ;
        plot.Legend.FontColor = Color.FromHex("#d7d7d7");
        plot.Legend.OutlineColor = Color.FromHex("#d7d7d7");

        // Add state of charge as a line with markers
        var socPlot = plot.Add.Scatter(times, socValues);
        socPlot.MarkerShape = MarkerShape.FilledCircle;
        socPlot.MarkerSize = 5;
        socPlot.LineWidth = 2;
        socPlot.LineColor = Colors.Blue;
        socPlot.MarkerColor = Colors.Blue;
        socPlot.LegendText = "State of Charge";

        for (var i = 0; i < times.Length; i++)
        {
            Bar chargeBar = new()
            {
                Value = chargeValues[i],
                Position = times[i],
                Size = 0.3,
                FillColor = Colors.Purple.WithAlpha(0.7),
                LineColor = Colors.Purple,
            };

            var barPlot = plot.Add.Bar(chargeBar);
            if (i == 0)
                barPlot.LegendText = "Charge";
        }

        for (var i = 0; i < times.Length; i++)
        {
            Bar dischargeBar = new()
            {
                Value = dischargeValues[i] * -1,
                Position = times[i],
                Size = 0.3,
                FillColor = Colors.Green.WithAlpha(0.7),
                LineColor = Colors.Green,
            };

            var barPlot = plot.Add.Bar(dischargeBar);
            if (i == 0)
                barPlot.LegendText = "Discharge";
        }

        // Create a second y-axis for tariff values
        var rightAxis = plot.Axes.Right;
        rightAxis.Label.Text = "Tariff (cents)";
        rightAxis.IsVisible = true;

        // Add tariff values on the right axis
        var tariffPlot = plot.Add.Scatter(times, tariffValues);
        tariffPlot.LineWidth = 2;
        tariffPlot.LineColor = Colors.Orange;
        tariffPlot.MarkerShape = MarkerShape.FilledDiamond;
        tariffPlot.MarkerSize = 5;
        tariffPlot.MarkerColor = Colors.Orange;
        tariffPlot.LegendText = "Tariff";
        tariffPlot.Axes.YAxis = plot.Axes.Right;

        // Configure axes
        plot.Axes.Bottom.Label.Text = "Time";
        plot.Axes.Left.Label.Text = "Energy (kWh)";

        // Add custom tick labels
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            positions: times,
            labels: timeLabels);
        plot.Axes.Bottom.MajorTickStyle.Length = 0;

        // Add legend
        plot.Legend.IsVisible = true;
        plot.Legend.Alignment = Alignment.UpperLeft;

        // Add title
        plot.Title("Battery Charge/Discharge Planned Schedule");

        // set the color palette used when coloring new items added to the plot
        plot.Add.Palette = new ScottPlot.Palettes.Penumbra();

        var plotPath = Path.Combine(Environment.CurrentDirectory, $"battery-schedule-{DateTimeOffset.Now:dd-MM-yyyy-HHmmss}.png");

        // Save the plot
        plot.SavePng(plotPath, 1200, 600);

        Console.WriteLine($"Chart saved to {plotPath}");
#endif
    }
}