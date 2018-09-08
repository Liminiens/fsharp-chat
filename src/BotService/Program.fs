namespace BotService

open System
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Hosting
open Serilog

module Program =

    let exitCode = 0

    let CreateWebHostBuilder args =
        WebHost
            .CreateDefaultBuilder(args)
            .UseKestrel()
            .UseSerilog()
            .UseStartup<Startup>();

    [<EntryPoint>]
    let main args =
        Console.OutputEncoding <- System.Text.Encoding.UTF8;
        Log.Logger <- Logger.setup true
        
        CreateWebHostBuilder(args).Build().Run()

        exitCode
