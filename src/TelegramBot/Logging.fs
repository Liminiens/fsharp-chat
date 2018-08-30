namespace FSharpChat.Bot 

open Serilog
open Serilog.Exceptions
open Serilog.Events

module Logger = 
    let setup logDebug = 
        let loggerConfiguration = new LoggerConfiguration()
        loggerConfiguration
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithExceptionDetails()
            .WriteTo.ColoredConsole(if logDebug then LogEventLevel.Debug else LogEventLevel.Information)
            .CreateLogger() :> ILogger