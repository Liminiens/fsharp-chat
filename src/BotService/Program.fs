namespace BotService

open System
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Hosting
open Serilog

module Logger = 
    open Serilog.Exceptions
    open Serilog.Events

    let setup = 
        let loggerConfiguration = new LoggerConfiguration()
        loggerConfiguration
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithExceptionDetails()
            .WriteTo.Console()
            .CreateLogger() :> ILogger

module Program =
    open Microsoft.Extensions.Configuration

    let exitCode = 0

    let CreateWebHostBuilder args =        
        let configureBotSettingsFile (builder: IConfigurationBuilder) = 
            builder.AddJsonFile("botconfig.json", false, false) |> ignore
    
        WebHost
            .CreateDefaultBuilder(args)
            .UseKestrel()
            .UseSerilog()
            .ConfigureAppConfiguration(configureBotSettingsFile)
            .UseStartup<Startup>();

    [<EntryPoint>]
    let main args =
        Console.OutputEncoding <- System.Text.Encoding.UTF8;
        Log.Logger <- Logger.setup
        
        CreateWebHostBuilder(args).Build().Run()

        exitCode
