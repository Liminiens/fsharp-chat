namespace FSharpChat.Bot 

open Serilog
open Serilog.Exceptions
open Serilog.Events
open Serilog.Formatting.Compact

module Logger = 
    let setup = 
        let loggerConfiguration = new LoggerConfiguration()
        loggerConfiguration
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithExceptionDetails()
            .WriteTo.ColoredConsole(LogEventLevel.Debug)
            (*.WriteTo.Async(fun config -> 
                config.File(new CompactJsonFormatter(), "./logs/log-.json", rollingInterval = RollingInterval.Day) |> ignore)*)
            .CreateLogger() :> ILogger